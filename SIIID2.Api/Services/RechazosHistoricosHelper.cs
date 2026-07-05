using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public static class RechazosHistoricosHelper
{
    public static async Task AplicarDisponibilidadAsync(IDbConnectionFactory dbConnectionFactory, List<InformeEnvioItem> rechazos)
    {
        var ids = rechazos.Where(x => x.TieneStagingDisponible).Select(x => x.IdCarga).Distinct().ToArray();
        if (ids.Length == 0) return;

        const string sql = @"
            SELECT c.id_carga
            FROM dbo.carga c
            WHERE c.id_carga IN @Ids
              AND EXISTS (
                  SELECT 1
                  FROM dbo.carga c2
                  WHERE ISNULL(c2.id_entidad_federativa, 0) = ISNULL(c.id_entidad_federativa, 0)
                    AND c2.mes_corte = c.mes_corte
                    AND c2.anio_corte = c.anio_corte
                    AND ISNULL(c2.tipo_carga, N'') = ISNULL(c.tipo_carga, N'')
                    AND c2.activo = 1
                    AND c2.id_carga > c.id_carga
                    AND c2.estado IN (N'VALIDADO_PENDIENTE', N'VALIDADO_PENDIENTE_ACTUALIZACION', N'PENDIENTE_APROBACION', N'RECHAZADO_ADMIN', N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
              );";

        using var connection = dbConnectionFactory.CrearConexion();
        var historicos = (await connection.QueryAsync<long>(sql, new { Ids = ids })).ToHashSet();

        foreach (var rechazo in rechazos)
        {
            if (historicos.Contains(rechazo.IdCarga)) rechazo.TieneStagingDisponible = false;
        }
    }

    public static async Task<bool> TieneIntentoPosteriorAsync(IDbConnectionFactory dbConnectionFactory, long idCarga)
    {
        const string sql = @"
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM dbo.carga c
                INNER JOIN dbo.carga c2
                    ON ISNULL(c2.id_entidad_federativa, 0) = ISNULL(c.id_entidad_federativa, 0)
                   AND c2.mes_corte = c.mes_corte
                   AND c2.anio_corte = c.anio_corte
                   AND ISNULL(c2.tipo_carga, N'') = ISNULL(c.tipo_carga, N'')
                   AND c2.activo = 1
                   AND c2.id_carga > c.id_carga
                   AND c2.estado IN (N'VALIDADO_PENDIENTE', N'VALIDADO_PENDIENTE_ACTUALIZACION', N'PENDIENTE_APROBACION', N'RECHAZADO_ADMIN', N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                WHERE c.id_carga = @IdCarga
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;";

        using var connection = dbConnectionFactory.CrearConexion();
        return await connection.ExecuteScalarAsync<bool>(sql, new { IdCarga = idCarga });
    }
}
