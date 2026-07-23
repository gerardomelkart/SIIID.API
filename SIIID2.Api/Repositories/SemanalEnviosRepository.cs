using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class SemanalEnviosRepository : ISemanalEnviosRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SemanalEnviosRepository(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    public async Task<List<SemanalEnvioItem>> ObtenerEnviosAsync(bool esSuperUsuario, int? idEntidadFederativaUsuario, int? idEntidadFederativa, int? anioSemana, int? numeroSemana, string? tipoCarga, string? estado)
    {
        const string sql = @"
        WITH ultimo_visible AS
        (
            SELECT
                sc.id_semanal_carga,
                ROW_NUMBER() OVER
                (
                    PARTITION BY
                        sc.id_entidad_federativa,
                        sc.anio_semana,
                        sc.numero_semana
                    ORDER BY
                        COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) DESC,
                        sc.id_semanal_carga DESC
                ) AS rn
            FROM dbo.semanal_carga sc
            WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado NOT LIKE N'RECHAZADO%'
              AND sc.activo = 1
        ),
        rechazo_visible AS
        (
            SELECT
                sc.id_semanal_carga
            FROM dbo.semanal_carga sc
            WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado = N'RECHAZADO_ADMIN'
              AND sc.activo = 1
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.semanal_carga posterior
                  WHERE ISNULL(posterior.id_entidad_federativa, 0) = ISNULL(sc.id_entidad_federativa, 0)
                    AND posterior.anio_semana = sc.anio_semana
                    AND posterior.numero_semana = sc.numero_semana
                    AND ISNULL(posterior.tipo_carga, N'') = ISNULL(sc.tipo_carga, N'')
                    AND posterior.id_semanal_carga > sc.id_semanal_carga
                    AND posterior.activo = 1
                    AND posterior.estado IN
                    (
                        N'VALIDADO_PENDIENTE',
                        N'VALIDADO_PENDIENTE_ACTUALIZACION',
                        N'PENDIENTE_APROBACION',
                        N'RECHAZADO_ADMIN',
                        N'CONFIRMADO',
                        N'CONFIRMADO_ACTUALIZACION'
                    )
              )
        ),
        cargas_visibles AS
        (
            SELECT id_semanal_carga
            FROM ultimo_visible
            WHERE rn = 1

            UNION ALL

            SELECT id_semanal_carga
            FROM rechazo_visible
        )
        SELECT
            sc.id_semanal_carga AS IdSemanalCarga,
            sc.codigo_referencia AS CodigoReferencia,
            sc.tipo_carga AS TipoCarga,
            sc.id_entidad_federativa AS IdEntidadFederativa,
            ISNULL(ef.nombre, N'') AS EntidadFederativa,
            ISNULL(ef.clave, N'') AS ClaveEntidad,
            sc.anio_semana AS AnioSemana,
            sc.numero_semana AS NumeroSemana,
            sc.fecha_inicio_semana AS FechaInicioSemana,
            sc.fecha_fin_semana AS FechaFinSemana,
            sc.fecha_inicio_tramo AS FechaInicioTramo,
            sc.fecha_fin_tramo AS FechaFinTramo,
            sc.mes_corte AS MesCorte,
            sc.anio_corte AS AnioCorte,
            sc.id_usuario_carga AS IdUsuarioCarga,
            u.usuario AS UsuarioCarga,
            COALESCE
            (
                NULLIF
                (
                    LTRIM(RTRIM(CONCAT
                    (
                        u.nombre,
                        N' ',
                        u.primer_apellido,
                        CASE
                            WHEN NULLIF(u.segundo_apellido, N'') IS NULL THEN N''
                            ELSE CONCAT(N' ', u.segundo_apellido)
                        END
                    ))),
                    N''
                ),
                u.usuario
            ) AS NombreUsuarioCarga,
            sc.total_carpetas_incluidas AS TotalCarpetasIncluidas,
            sc.total_delitos_incluidos AS TotalDelitosIncluidos,
            sc.total_victimas_incluidas AS TotalVictimasIncluidas,
            (
                SELECT COUNT(1)
                FROM dbo.semanal_carga_advertencia advertencia
                WHERE advertencia.id_semanal_carga = sc.id_semanal_carga
                  AND advertencia.activo = 1
            ) AS TotalAdvertencias,
            sc.estado AS Estado,
            sc.fecha_carga AS FechaCarga,
            sc.fecha_validacion AS FechaValidacion,
            sc.fecha_confirmacion AS FechaConfirmacion,
            COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) AS FechaMovimiento,
            CASE
                WHEN sc.estado = N'RECHAZADO_ADMIN' THEN sc.mensaje_error
                ELSE NULL
            END AS MotivoRechazo,
            usuario_resolucion.usuario AS UsuarioResolucion,
            CONVERT
            (
                bit,
                CASE
                    WHEN EXISTS
                    (
                        SELECT 1
                        FROM dbo.semanal_carga_tmp_carpeta carpeta
                        WHERE carpeta.id_semanal_carga = sc.id_semanal_carga
                          AND carpeta.activo = 1
                    )
                    OR EXISTS
                    (
                        SELECT 1
                        FROM dbo.semanal_carga_tmp_delito delito
                        WHERE delito.id_semanal_carga = sc.id_semanal_carga
                          AND delito.activo = 1
                    )
                    OR EXISTS
                    (
                        SELECT 1
                        FROM dbo.semanal_carga_tmp_victima victima
                        WHERE victima.id_semanal_carga = sc.id_semanal_carga
                          AND victima.activo = 1
                    )
                    THEN 1
                    ELSE 0
                END
            ) AS TieneStagingDisponible
        FROM cargas_visibles visible
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = visible.id_semanal_carga
        INNER JOIN dbo.usuario u
            ON u.id_usuario = sc.id_usuario_carga
        LEFT JOIN dbo.catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = sc.id_entidad_federativa
        LEFT JOIN dbo.usuario usuario_resolucion
            ON usuario_resolucion.id_usuario = sc.id_usuario_confirmacion
        WHERE (@EsSuperUsuario = 1 OR sc.id_entidad_federativa = @IdEntidadFederativaUsuario)
          AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
          AND (@AnioSemana IS NULL OR sc.anio_semana = @AnioSemana)
          AND (@NumeroSemana IS NULL OR sc.numero_semana = @NumeroSemana)
          AND (@TipoCarga IS NULL OR sc.tipo_carga = @TipoCarga)
          AND (@Estado IS NULL OR sc.estado = @Estado)
        ORDER BY
            ef.nombre,
            sc.anio_semana DESC,
            sc.numero_semana DESC,
            COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) DESC,
            sc.id_semanal_carga DESC;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return (await connection.QueryAsync<SemanalEnvioItem>(sql, new
        {
            EsSuperUsuario = esSuperUsuario,
            IdEntidadFederativaUsuario = idEntidadFederativaUsuario,
            IdEntidadFederativa = esSuperUsuario ? idEntidadFederativa : null,
            AnioSemana = anioSemana,
            NumeroSemana = numeroSemana,
            TipoCarga = tipoCarga,
            Estado = estado
        })).ToList();
    }

    public async Task<SemanalEnvioReferenciaInfo?> ObtenerReferenciaAsync(string codigoReferencia)
    {
        const string sql = @"
            SELECT TOP (1)
                sc.id_semanal_carga AS IdSemanalCarga,
                sc.codigo_referencia AS CodigoReferencia,
                sc.tipo_carga AS TipoCarga,
                sc.estado AS Estado,
                sc.id_entidad_federativa AS IdEntidadFederativa,
                ISNULL(ef.nombre, N'') AS EntidadFederativa,
                sc.anio_semana AS AnioSemana,
                sc.numero_semana AS NumeroSemana
            FROM dbo.semanal_carga sc
            LEFT JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = sc.id_entidad_federativa
            WHERE sc.codigo_referencia = @CodigoReferencia
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.activo = 1;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QueryFirstOrDefaultAsync<SemanalEnvioReferenciaInfo>(sql, new { CodigoReferencia = codigoReferencia });
    }
}