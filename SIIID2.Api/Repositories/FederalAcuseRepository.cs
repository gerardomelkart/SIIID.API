using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class FederalAcuseRepository : IFederalAcuseRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public FederalAcuseRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<CargaAcuseInfo?> ObtenerCargaParaAcuseAsync(string codigoReferencia)
    {
        const string sql = """
            SELECT
                c.id_federal_carga AS IdCarga,
                c.codigo_referencia AS CodigoReferencia,
                CAST(NULL AS INT) AS IdEntidadFederativa,
                N'Ámbito nacional' AS EntidadFederativa,
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte,
                c.total_carpetas_investigacion AS TotalCarpetasInvestigacion,
                c.total_delitos AS TotalDelitos,
                c.total_victimas AS TotalVictimas,
                CASE WHEN c.estado = N'RECHAZADO_ADMIN' THEN N'VALIDADO_PENDIENTE' ELSE c.estado END AS Estado,
                c.fecha_validacion AS FechaValidacion,
                CASE WHEN c.estado = N'RECHAZADO_ADMIN' THEN NULL ELSE c.fecha_confirmacion END AS FechaConfirmacion,
                c.id_usuario_carga AS IdUsuarioCarga,
                u.usuario AS UsuarioCarga
            FROM dbo.federal_carga c
            INNER JOIN dbo.usuario u
                ON u.id_usuario = c.id_usuario_carga
            WHERE c.codigo_referencia = @CodigoReferencia
              AND c.activo = 1;
            """;

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QueryFirstOrDefaultAsync<CargaAcuseInfo>(sql, new { CodigoReferencia = codigoReferencia });
    }

    public async Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseAsync(long idFederalCarga)
    {
        const string sql = """
            SELECT
                s.clave2_sabana AS ClaveDelito,
                s.delito_sabana AS TipoDelito,
                s.clave3_sabana AS ClaveSubtipo,
                s.subtipo_delito_sabana AS SubtipoDelito,
                COUNT(DISTINCT d.id_federal_carga_tmp_delito) AS TotalDelitos,
                COUNT(DISTINCT v.id_federal_carga_tmp_victima) AS TotalVictimas,
                MIN(s.id_delito_sabana) AS Orden,
                MIN(o.OrdenClave1) AS OrdenClave1,
                MIN(o.OrdenClave2) AS OrdenClave2,
                MIN(o.OrdenClave3) AS OrdenClave3
            FROM dbo.federal_catalogo_delito_sabana s
            CROSS APPLY
            (
                SELECT ClaveSubtipoLimpia = LTRIM(RTRIM(s.clave3_sabana))
            ) cs
            CROSS APPLY
            (
                SELECT
                    PrimerPunto = CHARINDEX('.', cs.ClaveSubtipoLimpia),
                    SegundoPunto = CHARINDEX('.', cs.ClaveSubtipoLimpia, CHARINDEX('.', cs.ClaveSubtipoLimpia) + 1)
            ) p
            CROSS APPLY
            (
                SELECT
                    OrdenClave1 = TRY_CONVERT(int, CASE WHEN p.PrimerPunto = 0 THEN cs.ClaveSubtipoLimpia ELSE LEFT(cs.ClaveSubtipoLimpia, p.PrimerPunto - 1) END),
                    OrdenClave2 = TRY_CONVERT(int, CASE WHEN p.PrimerPunto = 0 THEN '0' WHEN p.SegundoPunto = 0 THEN SUBSTRING(cs.ClaveSubtipoLimpia, p.PrimerPunto + 1, 50) ELSE SUBSTRING(cs.ClaveSubtipoLimpia, p.PrimerPunto + 1, p.SegundoPunto - p.PrimerPunto - 1) END),
                    OrdenClave3 = TRY_CONVERT(int, CASE WHEN p.SegundoPunto = 0 THEN '0' ELSE SUBSTRING(cs.ClaveSubtipoLimpia, p.SegundoPunto + 1, 50) END)
            ) o
            INNER JOIN dbo.federal_catalogo_modalidad_delito m
                ON m.id_modalidad_delito = s.id_modalidad_delito
               AND m.activo = 1
            LEFT JOIN dbo.federal_carga_tmp_delito d
                ON d.id_federal_carga = @IdFederalCarga
               AND d.clasf_de_dto = m.clave4
               AND TRY_CONVERT(INT, d.grdo_cons) = s.id_grado_consumacion
               AND TRY_CONVERT(INT, d.emto_com_dto) = s.id_instrumento_comision
               AND TRY_CONVERT(INT, d.forma_acc) = s.id_forma_accion
               AND d.activo = 1
            LEFT JOIN dbo.federal_carga_tmp_victima v
                ON v.id_federal_carga = d.id_federal_carga
               AND v.id_ci = d.id_ci
               AND v.id_delito = d.id_delito
               AND v.activo = 1
            WHERE s.activo = 1
            GROUP BY
                s.clave2_sabana,
                s.delito_sabana,
                s.clave3_sabana,
                s.subtipo_delito_sabana
            ORDER BY
                OrdenClave1,
                OrdenClave2,
                OrdenClave3,
                Orden;
            """;

        using var connection = _dbConnectionFactory.CrearConexion();
        var resumen = await connection.QueryAsync<CargaAcuseResumenItem>(sql, new { IdFederalCarga = idFederalCarga });
        return resumen.ToList();
    }

    public async Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseConfirmadoAsync(long idFederalCarga)
    {
        const string sql = """
            SELECT
                s.clave2_sabana AS ClaveDelito,
                s.delito_sabana AS TipoDelito,
                s.clave3_sabana AS ClaveSubtipo,
                s.subtipo_delito_sabana AS SubtipoDelito,
                COUNT(DISTINCT d.id_federal_delito) AS TotalDelitos,
                COUNT(DISTINCT v.id_federal_victima) AS TotalVictimas,
                MIN(s.id_delito_sabana) AS Orden,
                MIN(o.OrdenClave1) AS OrdenClave1,
                MIN(o.OrdenClave2) AS OrdenClave2,
                MIN(o.OrdenClave3) AS OrdenClave3
            FROM dbo.federal_catalogo_delito_sabana s
            CROSS APPLY
            (
                SELECT ClaveSubtipoLimpia = LTRIM(RTRIM(s.clave3_sabana))
            ) cs
            CROSS APPLY
            (
                SELECT
                    PrimerPunto = CHARINDEX('.', cs.ClaveSubtipoLimpia),
                    SegundoPunto = CHARINDEX('.', cs.ClaveSubtipoLimpia, CHARINDEX('.', cs.ClaveSubtipoLimpia) + 1)
            ) p
            CROSS APPLY
            (
                SELECT
                    OrdenClave1 = TRY_CONVERT(int, CASE WHEN p.PrimerPunto = 0 THEN cs.ClaveSubtipoLimpia ELSE LEFT(cs.ClaveSubtipoLimpia, p.PrimerPunto - 1) END),
                    OrdenClave2 = TRY_CONVERT(int, CASE WHEN p.PrimerPunto = 0 THEN '0' WHEN p.SegundoPunto = 0 THEN SUBSTRING(cs.ClaveSubtipoLimpia, p.PrimerPunto + 1, 50) ELSE SUBSTRING(cs.ClaveSubtipoLimpia, p.PrimerPunto + 1, p.SegundoPunto - p.PrimerPunto - 1) END),
                    OrdenClave3 = TRY_CONVERT(int, CASE WHEN p.SegundoPunto = 0 THEN '0' ELSE SUBSTRING(cs.ClaveSubtipoLimpia, p.SegundoPunto + 1, 50) END)
            ) o
            LEFT JOIN dbo.federal_delito d
                ON d.id_federal_carga = @IdFederalCarga
               AND d.id_modalidad_delito = s.id_modalidad_delito
               AND d.id_grado_consumacion = s.id_grado_consumacion
               AND d.id_instrumento_comision = s.id_instrumento_comision
               AND d.id_forma_accion = s.id_forma_accion
               AND d.activo = 1
            LEFT JOIN dbo.federal_victima v
                ON v.id_federal_carga = @IdFederalCarga
               AND v.id_federal_delito = d.id_federal_delito
               AND v.activo = 1
            WHERE s.activo = 1
            GROUP BY
                s.clave2_sabana,
                s.delito_sabana,
                s.clave3_sabana,
                s.subtipo_delito_sabana
            ORDER BY
                OrdenClave1,
                OrdenClave2,
                OrdenClave3,
                Orden;
            """;

        using var connection = _dbConnectionFactory.CrearConexion();
        var resumen = await connection.QueryAsync<CargaAcuseResumenItem>(sql, new { IdFederalCarga = idFederalCarga });
        return resumen.ToList();
    }
}
