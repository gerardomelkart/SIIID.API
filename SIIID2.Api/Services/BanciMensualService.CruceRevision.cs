using Dapper;
using System.Text.Json;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public sealed partial class BanciMensualService
{
    // El llamador ya verificó el permiso mensual de revisión o el remitente de la actualización.
    internal async Task<BanciCruceResumen?> ResumenCruceAsync(long carga)
    {
        using var db = factory.CrearConexion();
        var op = await db.QuerySingleOrDefaultAsync<BanciOperacion>(BanciMensualSql.Operaciones + " WHERE b.id_banci_carga=(SELECT id_banci_carga FROM dbo.banci_operacion_vinculada WHERE id_carga_consolidado=@carga);", new
        {
            carga
        });
        if(op == null)
            return null;
        var propuesta = await PropuestaAsync(db, op);
        var origen = JsonSerializer.Deserialize<BanciLibro>(op.Snapshot ?? "{}", Json) ?? new();
        var previas = origen.Victimas.ToDictionary(v => Llave(v, 3));
        var resumen = new BanciCruceResumen
        {
            Referencia = op.Referencia,
            Carpetas = propuesta.Carpetas.Count,
            Victimas = propuesta.Victimas.Count,
            Nuevas = propuesta.Victimas.Count(v => !previas.ContainsKey(Llave(v, 3)))
        };
        var condiciones = (await db.QueryAsync<(string Codigo, string Clave)>("SELECT codigo_regla AS Codigo,CONVERT(varchar(20),clave) AS Clave FROM dbo.banci_catalogo_constitutiva_delito WHERE activo=1;")).ToDictionary(x => x.Codigo, x => x.Clave);
        var desapariciones = (await db.QueryAsync<string>(ClavesDesaparicionSql)).ToHashSet();
        resumen.Reclasificadas = propuesta.Victimas.Count(v => EsReclasificada(v, condiciones["NO_DELITO"], condiciones["DELITO"], desapariciones));
        foreach(var fila in propuesta.Victimas)
        {
            previas.TryGetValue(Llave(fila, 3), out var previa);
            foreach(var campo in ColumnasVictima.Where(c => c != "id_vicf"))
            {
                var antes = previa == null ? "" : Valor(previa, campo);
                var despues = Valor(fila, campo);
                if(antes == despues)
                    continue;
                resumen.TotalCambios++;
                if(resumen.Cambios.Count < 100)
                    resumen.Cambios.Add(new(Valor(fila, "id_ci"), Valor(fila, "id_delito"), Valor(fila, "id_vicf"), campo, antes, despues));
            }
        }
        return resumen;
    }
}
