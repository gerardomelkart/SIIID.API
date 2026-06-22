using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class AcuseRepository : IAcuseRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public AcuseRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<CargaAcuseInfo?> ObtenerCargaParaAcuseAsync(string codigoReferencia)
    {
        // Obtiene datos generales de la carga para el acuse previo.
        var sql = @"
        SELECT
            c.id_carga AS IdCarga,
            c.codigo_referencia AS CodigoReferencia,
            c.id_entidad_federativa AS IdEntidadFederativa,
            ISNULL(e.nombre, '') AS EntidadFederativa,
            c.mes_corte AS MesCorte,
            c.anio_corte AS AnioCorte,
            c.total_carpetas_investigacion AS TotalCarpetasInvestigacion,
            c.total_delitos AS TotalDelitos,
            c.total_victimas AS TotalVictimas,
            c.estado AS Estado,
            c.fecha_validacion AS FechaValidacion,
            c.fecha_confirmacion AS FechaConfirmacion,
            c.id_usuario_carga AS IdUsuarioCarga,
            u.usuario AS UsuarioCarga
        FROM carga c
        INNER JOIN usuario u
            ON u.id_usuario = c.id_usuario_carga
        LEFT JOIN catalogo_entidad_federativa e
            ON e.id_entidad_federativa = c.id_entidad_federativa
        WHERE c.codigo_referencia = @CodigoReferencia
          AND c.activo = 1;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<CargaAcuseInfo>(sql, new
        {
            CodigoReferencia = codigoReferencia
        });
    }

    public async Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseAsync(long idCarga)
    {
        // El acuse parte de catalogo_delito_sabana para que salgan también registros en cero.
        // La relación se hace contra modalidad, grado, instrumento y forma de acción.
        var sql = @"
        SELECT
            s.clave2_sabana AS ClaveDelito,
            s.delito_sabana AS TipoDelito,
            s.clave3_sabana AS ClaveSubtipo,
            s.subtipo_delito_sabana AS SubtipoDelito,
            COUNT(DISTINCT d.id_carga_tmp_delito) AS TotalDelitos,
            COUNT(DISTINCT v.id_carga_tmp_victima) AS TotalVictimas,
            MIN(s.id_delito_sabana) AS Orden
        FROM catalogo_delito_sabana s
        LEFT JOIN catalogo_modalidad_delito m
            ON m.id_modalidad_delito = s.id_modalidad_delito
        LEFT JOIN carga_tmp_delito d
            ON d.id_carga = @IdCarga
           AND d.clasf_de_dto = m.clave4
           AND TRY_CONVERT(INT, d.grdo_cons) = s.id_grado_consumacion
           AND TRY_CONVERT(INT, d.emto_com_dto) = s.id_instrumento_comision
           AND TRY_CONVERT(INT, d.forma_acc) = s.id_forma_accion
           AND d.activo = 1
        LEFT JOIN carga_tmp_victima v
            ON v.id_carga = d.id_carga
           AND v.id_ci = d.id_ci
           AND v.id_delito = d.id_delito
           AND v.activo = 1
        GROUP BY
            s.clave2_sabana,
            s.delito_sabana,
            s.clave3_sabana,
            s.subtipo_delito_sabana
        ORDER BY
            Orden;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var resumen = await connection.QueryAsync<CargaAcuseResumenItem>(sql, new
        {
            IdCarga = idCarga
        });

        return resumen.ToList();
    }

    public async Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseConfirmadoAsync(long idCarga)
    {
        // El acuse confirmado parte de catalogo_delito_sabana para que salgan registros en cero.
        // Pero los conteos ya se toman de tablas finales: delito y victima.
        var sql = @"
        SELECT
            s.clave2_sabana AS ClaveDelito,
            s.delito_sabana AS TipoDelito,
            s.clave3_sabana AS ClaveSubtipo,
            s.subtipo_delito_sabana AS SubtipoDelito,
            COUNT(DISTINCT d.id_delito) AS TotalDelitos,
            COUNT(DISTINCT v.id_victima) AS TotalVictimas,
            MIN(s.id_delito_sabana) AS Orden
        FROM catalogo_delito_sabana s
        LEFT JOIN delito d
            ON d.id_carga = @IdCarga
           AND d.id_modalidad_delito = s.id_modalidad_delito
           AND d.id_grado_consumacion = s.id_grado_consumacion
           AND d.id_instrumento_comision = s.id_instrumento_comision
           AND d.id_forma_accion = s.id_forma_accion
           AND d.activo = 1
        LEFT JOIN victima v
            ON v.id_carga = d.id_carga
           AND v.id_delito = d.id_delito
           AND v.activo = 1
        WHERE s.activo = 1
        GROUP BY
            s.clave2_sabana,
            s.delito_sabana,
            s.clave3_sabana,
            s.subtipo_delito_sabana
        ORDER BY
            Orden;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var resumen = await connection.QueryAsync<CargaAcuseResumenItem>(sql, new
        {
            IdCarga = idCarga
        });

        return resumen.ToList();
    }

    public async Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseConfirmadoActualizacionAsync(long idCargaActualizacion)
    {
        // Genera el resumen del acuse confirmado de actualización.
        // A diferencia del acuse confirmado normal, aquí no basta con d.id_carga = @IdCargaActualizacion,
        // porque los registros sin cambios pueden seguir ligados a la carga inicial o a una actualización previa.
        //
        // Por eso se obtiene el periodo de la actualización y se toman las versiones activas vigentes
        // de carpetas, delitos y víctimas para ese corte completo.

        var sql = @"
            DECLARE @IdEntidadFederativa TINYINT;
            DECLARE @MesCorte TINYINT;
            DECLARE @AnioCorte SMALLINT;

            SELECT
                @IdEntidadFederativa = id_entidad_federativa,
                @MesCorte = mes_corte,
                @AnioCorte = anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion;

            ;WITH delitos_periodo AS (
                SELECT
                    d.id_delito,
                    d.id_modalidad_delito,
                    d.id_grado_consumacion,
                    d.id_instrumento_comision,
                    d.id_forma_accion
                FROM delito d
                INNER JOIN carga c
                    ON c.id_carga = d.id_carga
                INNER JOIN carpeta_investigacion ci
                    ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
                   AND ci.activo = 1
                WHERE c.id_entidad_federativa = @IdEntidadFederativa
                  AND c.mes_corte = @MesCorte
                  AND c.anio_corte = @AnioCorte
                  AND c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
                  AND c.activo = 1
                  AND d.activo = 1
            ),
            delitos_resumen AS (
                SELECT
                    id_modalidad_delito,
                    id_grado_consumacion,
                    id_instrumento_comision,
                    id_forma_accion,
                    COUNT_BIG(1) AS TotalDelitos
                FROM delitos_periodo
                GROUP BY
                    id_modalidad_delito,
                    id_grado_consumacion,
                    id_instrumento_comision,
                    id_forma_accion
            ),
            victimas_resumen AS (
                SELECT
                    dp.id_modalidad_delito,
                    dp.id_grado_consumacion,
                    dp.id_instrumento_comision,
                    dp.id_forma_accion,
                    COUNT_BIG(1) AS TotalVictimas
                FROM delitos_periodo dp
                INNER JOIN victima v
                    ON v.id_delito = dp.id_delito
                   AND v.activo = 1
                GROUP BY
                    dp.id_modalidad_delito,
                    dp.id_grado_consumacion,
                    dp.id_instrumento_comision,
                    dp.id_forma_accion
            )
            SELECT
                s.clave2_sabana AS ClaveDelito,
                s.delito_sabana AS TipoDelito,
                s.clave3_sabana AS ClaveSubtipo,
                s.subtipo_delito_sabana AS SubtipoDelito,
                CONVERT(int, ISNULL(dr.TotalDelitos, 0)) AS TotalDelitos,
                CONVERT(int, ISNULL(vr.TotalVictimas, 0)) AS TotalVictimas,
                MIN(s.id_delito_sabana) AS Orden
            FROM catalogo_delito_sabana s
            LEFT JOIN delitos_resumen dr
                ON dr.id_modalidad_delito = s.id_modalidad_delito
               AND dr.id_grado_consumacion = s.id_grado_consumacion
               AND dr.id_instrumento_comision = s.id_instrumento_comision
               AND dr.id_forma_accion = s.id_forma_accion
            LEFT JOIN victimas_resumen vr
                ON vr.id_modalidad_delito = s.id_modalidad_delito
               AND vr.id_grado_consumacion = s.id_grado_consumacion
               AND vr.id_instrumento_comision = s.id_instrumento_comision
               AND vr.id_forma_accion = s.id_forma_accion
            WHERE s.activo = 1
            GROUP BY
                s.clave2_sabana,
                s.delito_sabana,
                s.clave3_sabana,
                s.subtipo_delito_sabana,
                dr.TotalDelitos,
                vr.TotalVictimas
            ORDER BY
                Orden
            OPTION (RECOMPILE);
            ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var resumen = await connection.QueryAsync<CargaAcuseResumenItem>(
            sql,
            new
            {
                IdCargaActualizacion = idCargaActualizacion
            },
            commandTimeout: 180);

        return resumen.ToList();
    }
}