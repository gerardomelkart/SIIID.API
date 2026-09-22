using System.Data;
using System.Text.Json;
using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public sealed class BanciActualizacionRepository(IDbConnectionFactory factory)
{
    public async Task<IReadOnlyList<BanciVictimaIdentificada>> ResolverAsync(int entidad, IEnumerable<Dictionary<string, string?>> filas)
    {
        using var connection = factory.CrearConexion();
        // Sólo se llama después de verificar permiso de modificación y entidad en el servicio.
        return (await connection.QueryAsync<BanciVictimaIdentificada>("""
            SELECT CONVERT(int, j.[key]) AS Indice, v.no_banci AS NoBanci, v.id_delito AS IdDelito, v.id_vicf AS IdVicf, v.curp AS Curp, v.folio_rnpdno AS FolioRnpdno
            FROM OPENJSON(@Datos) j
            CROSS APPLY OPENJSON(j.value) WITH (identificador nvarchar(250), no_banci nvarchar(40), id_delito nvarchar(250), id_vicf nvarchar(250)) k
            CROSS APPLY (
                SELECT TOP (2) v.no_banci, v.id_delito, v.id_vicf, v.curp, v.folio_rnpdno
                FROM dbo.banci_vw_victimas_v2 v
                WHERE v.id_entidad_federativa = @Entidad
                  AND (k.no_banci IS NULL OR v.no_banci = k.no_banci)
                  AND (k.id_delito IS NULL OR v.id_delito = k.id_delito)
                  AND (k.id_vicf IS NULL OR v.id_vicf = k.id_vicf)
                  AND (k.identificador IS NULL OR v.no_banci = k.identificador OR v.curp = k.identificador OR v.folio_rnpdno = k.identificador)
                ORDER BY v.id_banci_victima
            ) v;
            """, new { Entidad = entidad, Datos = JsonSerializer.Serialize(filas) }, commandTimeout: 300)).AsList();
    }

    public async Task<BanciActualizacionResultado> PrepararAsync(int usuario, int entidad, string origen, List<Dictionary<string, string?>> datos, List<BanciCargaValidacionError> advertencias)
    {
        using var connection = factory.CrearConexion();
        var resultado = await connection.QuerySingleAsync<BanciActualizacionSql>("dbo.sp_banci_preparar_actualizacion_v2", new { IdUsuario = usuario, IdEntidad = entidad, Origen = origen, DatosJson = JsonSerializer.Serialize(datos), AdvertenciasJson = JsonSerializer.Serialize(advertencias) }, commandType: CommandType.StoredProcedure, commandTimeout: 300);
        return resultado.Resultado();
    }

    public async Task<BanciActualizacionResultado> VistaPreviaAsync(Guid referencia, int usuario)
    {
        using var connection = factory.CrearConexion();
        return (await connection.QuerySingleAsync<BanciActualizacionSql>("dbo.sp_banci_vista_previa_actualizacion_v2", new { CodigoReferencia = referencia, IdUsuario = usuario }, commandType: CommandType.StoredProcedure, commandTimeout: 300)).Resultado();
    }

    public async Task<object> ConfirmarAsync(Guid referencia, int usuario, BanciActualizacionConfirmacion request)
    {
        using var connection = factory.CrearConexion();
        var resultado = await connection.QuerySingleAsync("dbo.sp_banci_confirmar_actualizacion_v2", new { CodigoReferencia = referencia, IdUsuario = usuario, request.Aceptar, request.HuellaVistaPrevia, request.AceptarAdvertencias }, commandType: CommandType.StoredProcedure, commandTimeout: 300);
        return new { codigoReferencia = (Guid)resultado.CodigoReferencia, estado = (string)resultado.Estado, yaResuelta = (bool)resultado.YaResuelta, totalCambios = (int?)resultado.TotalCambios };
    }

    public async Task<object> BuscarAsync(int usuario, string texto, int? entidad, int pagina, int tamano)
    {
        using var connection = factory.CrearConexion();
        var filas = (await connection.QueryAsync("dbo.sp_banci_buscar_victimas_v2", new { IdUsuario = usuario, Texto = texto, IdEntidad = entidad, Pagina = pagina, Tamano = tamano }, commandType: CommandType.StoredProcedure, commandTimeout: 120)).AsList();
        var datos = filas.Select(f => ((IDictionary<string, object>)f).Where(p => p.Key != "Total" && p.Key != "version_banci").ToDictionary(p => p.Key, p => p.Value)).ToList();
        return new { total = filas.Count == 0 ? 0L : (long)filas[0].Total, pagina, tamanoPagina = tamano, victimas = datos };
    }

    public async Task<object> EstadosAsync(int usuario, int? entidadAlcance, Guid? referencia = null)
    {
        using var connection = factory.CrearConexion();
        // El servicio comprueba acceso vigente; siempre se restringe al autor.
        return (await connection.QueryAsync("""
            SELECT codigo_referencia AS codigoReferencia, estado, origen, id_entidad_federativa AS idEntidadFederativa,
                   fecha_registro AS fechaRegistro, fecha_decision AS fechaDecision, total_cambios AS totalCambios
            FROM dbo.banci_actualizacion_v2
            WHERE id_usuario = @Usuario AND (@Entidad IS NULL OR id_entidad_federativa = @Entidad)
              AND ((@Referencia IS NULL AND estado = N'PENDIENTE') OR codigo_referencia = @Referencia)
            ORDER BY fecha_registro DESC;
            """, new { Usuario = usuario, Entidad = entidadAlcance, Referencia = referencia })).AsList();
    }
}
