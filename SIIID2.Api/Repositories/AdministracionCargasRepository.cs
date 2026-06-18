using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class AdministracionCargasRepository : IAdministracionCargasRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public AdministracionCargasRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<List<CargaPendienteAdministracionItem>> ObtenerPendientesAsync()
    {
        const string sql = @"
            SELECT
                c.id_carga AS IdCarga,
                c.codigo_referencia AS CodigoReferencia,
                c.tipo_carga AS TipoCarga,
                c.id_entidad_federativa AS IdEntidadFederativa,
                ISNULL(ef.nombre, '') AS EntidadFederativa,
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte,
                c.fecha_validacion AS FechaValidacion,
                c.id_usuario_carga AS IdUsuarioCarga,
                u.usuario AS UsuarioCarga,
                LTRIM(RTRIM(
                    CONCAT(
                        u.nombre,
                        ' ',
                        u.primer_apellido,
                        CASE
                            WHEN NULLIF(u.segundo_apellido, '') IS NULL
                                THEN ''
                            ELSE CONCAT(' ', u.segundo_apellido)
                        END
                    )
                )) AS NombreUsuarioCarga,
                c.total_carpetas_investigacion AS TotalCarpetas,
                c.total_delitos AS TotalDelitos,
                c.total_victimas AS TotalVictimas,
                (
                    SELECT COUNT(1)
                    FROM dbo.carga_advertencia ca
                    WHERE ca.id_carga = c.id_carga
                      AND ca.activo = 1
                ) AS TotalAdvertencias
            FROM dbo.carga c
            INNER JOIN dbo.usuario u
                ON u.id_usuario = c.id_usuario_carga
            LEFT JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa =
                   c.id_entidad_federativa
            WHERE c.estado = 'PENDIENTE_APROBACION'
              AND c.activo = 1
            ORDER BY
                c.fecha_validacion ASC,
                c.id_carga ASC;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        var registros = await connection.QueryAsync<CargaPendienteAdministracionItem>(sql);
        return registros.ToList();
    }

    public async Task<CargaPendienteAdministracionDetalle?> ObtenerDetalleAsync(string codigoReferencia)
    {
        const string sqlCarga = @"
            SELECT TOP 1
                c.id_carga AS IdCarga,
                c.codigo_referencia AS CodigoReferencia,
                c.tipo_carga AS TipoCarga,
                c.id_entidad_federativa AS IdEntidadFederativa,
                ISNULL(ef.nombre, '') AS EntidadFederativa,
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte,
                c.fecha_validacion AS FechaValidacion,
                c.id_usuario_carga AS IdUsuarioCarga,
                u.usuario AS UsuarioCarga,
                LTRIM(RTRIM(
                    CONCAT(
                        u.nombre,
                        ' ',
                        u.primer_apellido,
                        CASE
                            WHEN NULLIF(u.segundo_apellido, '') IS NULL
                                THEN ''
                            ELSE CONCAT(' ', u.segundo_apellido)
                        END
                    )
                )) AS NombreUsuarioCarga,
                c.total_carpetas_investigacion AS TotalCarpetas,
                c.total_delitos AS TotalDelitos,
                c.total_victimas AS TotalVictimas,
                (
                    SELECT COUNT(1)
                    FROM dbo.carga_advertencia ca
                    WHERE ca.id_carga = c.id_carga
                      AND ca.activo = 1
                ) AS TotalAdvertencias
            FROM dbo.carga c
            INNER JOIN dbo.usuario u
                ON u.id_usuario = c.id_usuario_carga
            LEFT JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa =
                   c.id_entidad_federativa
            WHERE c.codigo_referencia = @CodigoReferencia
              AND c.estado = 'PENDIENTE_APROBACION'
              AND c.activo = 1;
        ";

        const string sqlAdvertencias = @"
            SELECT
                ca.id_carga_advertencia AS IdCargaAdvertencia,
                ca.codigo AS Codigo,
                ca.archivo AS Archivo,
                ca.numero_fila AS NumeroFila,
                ca.columna AS Columna,
                ca.campo AS Campo,
                ca.valor AS Valor,
                ca.descripcion_resumen AS DescripcionResumen,
                ca.mensaje AS Mensaje,
                ca.aceptada_usuario AS AceptadaUsuario,
                ca.fecha_aceptacion AS FechaAceptacion
            FROM dbo.carga_advertencia ca
            WHERE ca.id_carga = @IdCarga
              AND ca.activo = 1
            ORDER BY
                ca.archivo,
                ca.numero_fila,
                ca.id_carga_advertencia;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        var carga = await connection.QueryFirstOrDefaultAsync<CargaPendienteAdministracionDetalle>(sqlCarga, new{CodigoReferencia = codigoReferencia});
        if (carga == null)
        {
            return null;
        }
        var advertencias = await connection.QueryAsync<CargaAdvertenciaAdministracionItem>(sqlAdvertencias, new {carga.IdCarga});
        carga.Advertencias = advertencias.ToList();
        return carga;
    }

    public async Task<CargaReferenciaAdministracionInfo?> ObtenerReferenciaAsync(string codigoReferencia)
    {
        const string sql = @"
        SELECT TOP 1
            c.codigo_referencia AS CodigoReferencia,
            c.tipo_carga AS TipoCarga,
            c.estado AS Estado
        FROM dbo.carga c
        WHERE c.codigo_referencia = @CodigoReferencia
          AND c.activo = 1;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<CargaReferenciaAdministracionInfo>(sql, new {CodigoReferencia = codigoReferencia});
    }
}