using System.Data;
using System.Text.Json;
using Dapper;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public sealed partial class BanciMensualService
{
    // El motivo usa los campos existentes: clave2/clave4 del delito o motivo de no delito.
    public static bool EsReclasificada(ArchivoFila victima, string noDelito, string delito, ISet<string> desapariciones)
        => Valor(victima, "constitutiva_delito") == noDelito
        || Valor(victima, "constitutiva_delito") == delito && Valor(victima, "motivo_desaparicion") != ""
            && !desapariciones.Contains(Valor(victima, "motivo_desaparicion"));

    private async Task<List<CargaValidacionError>> ValidarClasificacionReclasificacionAsync(BanciLibro libro)
    {
        using var db = factory.CrearConexion();
        var motivos = (await db.QueryAsync<(byte Condicion, string Clave)>(BanciMensualSql.Motivos))
            .Select(x => (x.Condicion.ToString(System.Globalization.CultureInfo.InvariantCulture), x.Clave)).ToHashSet();
        return ValidarClasificacionReclasificacion(libro, reglas.Clave("banci_catalogo_localizacion", "LOCALIZADO"), motivos);
    }

    public static List<CargaValidacionError> ValidarClasificacionReclasificacion(BanciLibro libro, string localizado, ISet<(string Condicion, string Motivo)> motivos)
    {
        var errores = new List<CargaValidacionError>();
        foreach(var fila in libro.Victimas)
        {
            var condicion = Valor(fila, "constitutiva_delito");
            var motivo = Valor(fila, "motivo_desaparicion");
            if(condicion == "" && motivo == "")
                continue;
            if(Valor(fila, "localizado_o_no_localizado") != localizado || !motivos.Contains((condicion, motivo)))
                errores.Add(Error("victimas", fila, "motivo_desaparicion", "La clasificación requiere Persona localizada y un delito o motivo vigente correspondiente a la condición seleccionada."));
        }
        return errores;
    }

    private const string ClavesDesaparicionSql = """
        SELECT bc.clave FROM dbo.banci_catalogo_clasificacion_delito bc WHERE bc.activo=1
        UNION SELECT cd.clave2 FROM dbo.banci_catalogo_clasificacion_delito bc
        JOIN dbo.catalogo_modalidad_delito md ON md.clave4=bc.clave
        JOIN dbo.catalogo_subtipo_delito sd ON sd.id_subtipo_delito=md.id_subtipo_delito
        JOIN dbo.catalogo_delito cd ON cd.id_delito=sd.id_delito WHERE bc.activo=1;
        """;

    private static async Task RegistrarReclasificacionesAsync(IDbConnection db, IDbTransaction tx, BanciOperacion op, BanciLibro propuesta, int usuario)
    {
        var condiciones = (await db.QueryAsync<(string Codigo, string Clave)>("SELECT codigo_regla AS Codigo,CONVERT(varchar(20),clave) AS Clave FROM dbo.banci_catalogo_constitutiva_delito WHERE activo=1;", transaction: tx)).ToDictionary(x => x.Codigo, x => x.Clave);
        var desapariciones = (await db.QueryAsync<string>(ClavesDesaparicionSql, transaction: tx)).ToHashSet();
        var origen = JsonSerializer.Deserialize<BanciLibro>(op.Snapshot ?? "{}", Json) ?? new();
        var previas = origen.Victimas.GroupBy(v => Llave(v, 3)).ToDictionary(g => g.Key, g => g.First());
        foreach(var fila in propuesta.Victimas)
        {
            var anterior = await db.QuerySingleAsync<EstadoReclasificacion>("""
                SELECT v.id_banci_victima AS Id,v.id_banci_delito AS Delito,v.reclasificada AS Reclasificada
                FROM dbo.banci_victima v JOIN dbo.banci_delito d ON d.id_banci_delito=v.id_banci_delito
                JOIN dbo.banci_carpeta_investigacion ci ON ci.id_banci_carpeta_investigacion=d.id_banci_carpeta_investigacion
                WHERE ci.id_entidad_federativa=@Entidad AND ci.mes_corte=@Mes AND ci.anio_corte=@Anio
                  AND ci.id_ci=@ci AND d.id_delito=@delito AND v.id_vicf=@victima;
                """, new
            {
                op.Entidad,
                op.Mes,
                op.Anio,
                ci = Valor(fila, "id_ci"),
                delito = Valor(fila, "id_delito"),
                victima = Valor(fila, "id_vicf")
            }, tx);
            var reclasificada = EsReclasificada(fila, condiciones["NO_DELITO"], condiciones["DELITO"], desapariciones);
            previas.TryGetValue(Llave(fila, 3), out var previa);
            var cambioClasificacion = previa != null && (Valor(previa, "constitutiva_delito") != Valor(fila, "constitutiva_delito") || Valor(previa, "motivo_desaparicion") != Valor(fila, "motivo_desaparicion"));
            if(anterior.Reclasificada == reclasificada && !(reclasificada && cambioClasificacion))
                continue;
            // El snapshot conserva claves y datos reportados; el historial de campos conserva sus valores anteriores.
            await db.ExecuteAsync("""
                INSERT dbo.banci_reclasificado(id_banci_delito,id_banci_victima,id_banci_carga,constitutiva_delito,motivo,snapshot_previo,id_usuario,es_reclasificacion)
                VALUES(@Delito,@Id,@carga,@condicion,@motivo,@snapshot,@usuario,@reclasificada);
                INSERT dbo.banci_historial_cambio(id_banci_carga,tipo_registro,identificador_registro,campo,valor_anterior,valor_nuevo,id_usuario)
                VALUES(@carga,'VICTIMA',@Id,'reclasificada',@antes,@despues,@usuario);
                UPDATE dbo.banci_victima SET reclasificada=@reclasificada WHERE id_banci_victima=@Id;
                """, new
            {
                anterior.Delito,
                anterior.Id,
                carga = op.Id,
                condicion = Valor(fila, "constitutiva_delito") == "" ? condiciones["DELITO"] : Valor(fila, "constitutiva_delito"),
                motivo = Valor(fila, "motivo_desaparicion") == "" ? "Reversión a desaparición vigente" : Valor(fila, "motivo_desaparicion"),
                snapshot = Serializar(new
                {
                    anterior = previa,
                    propuesto = fila
                }),
                usuario,
                reclasificada,
                antes = anterior.Reclasificada ? "1" : "0",
                despues = reclasificada ? "1" : "0"
            }, tx);
        }
    }
    private sealed class EstadoReclasificacion
    {
        public long Id
        {
            get; set;
        }
        public long Delito
        {
            get; set;
        }
        public bool Reclasificada
        {
            get; set;
        }
    }
    private async Task ValidarRenapoBanciAsync(BanciLibro libro, List<CargaValidacionError> errores, List<CargaValidacionError> advertencias)
    {
        if(errores.Count == 0 && configuracion.Activa("BANCI", "RENAPO"))
        {
            var grupos = libro.Victimas.Where(f => Valor(f, "curp") != "").GroupBy(f => Valor(f, "curp")).ToArray();
            foreach(var lote in grupos.Chunk(4))
            {
                var resultados = await Task.WhenAll(lote.Select(async g => new { Grupo = g, Resultado = await renapo.ConsultarAsync(g.Key) }));
                foreach(var consulta in resultados)
                {
                    if(consulta.Resultado.Estado == EstadoConsultaRenapo.Encontrada)
                        continue;
                    if(consulta.Resultado.Estado == EstadoConsultaRenapo.NoDisponible)
                        advertencias.Add(Error("victimas", consulta.Grupo.First(), "curp", "RENAPO no está disponible. La CURP solo pudo validarse localmente."));
                    else
                        errores.Add(Error("victimas", consulta.Grupo.First(), "curp", "RENAPO no validó la CURP. Revise el dato y la disponibilidad del servicio."));
                }

                if(advertencias.Count > 0)
                    break;
            }
        }
    }

}
