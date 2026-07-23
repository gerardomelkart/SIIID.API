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
                    WHEN sc.estado IN (N'RECHAZADO', N'RECHAZADO_ADMIN') THEN sc.mensaje_error
                    ELSE NULL
                END AS MotivoRechazo,
                usuario_resolucion.usuario AS UsuarioResolucion
            FROM dbo.semanal_carga sc
            INNER JOIN dbo.usuario u
                ON u.id_usuario = sc.id_usuario_carga
            LEFT JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = sc.id_entidad_federativa
            LEFT JOIN dbo.usuario usuario_resolucion
                ON usuario_resolucion.id_usuario = sc.id_usuario_confirmacion
            WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado IN
              (
                  N'VALIDADO_PENDIENTE',
                  N'VALIDADO_PENDIENTE_ACTUALIZACION',
                  N'PENDIENTE_APROBACION',
                  N'CONFIRMADO',
                  N'CONFIRMADO_ACTUALIZACION',
                  N'RECHAZADO',
                  N'RECHAZADO_ADMIN',
                  N'EXPIRADO'
              )
              AND sc.activo = 1
              AND (@EsSuperUsuario = 1 OR sc.id_entidad_federativa = @IdEntidadFederativaUsuario)
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND (@AnioSemana IS NULL OR sc.anio_semana = @AnioSemana)
              AND (@NumeroSemana IS NULL OR sc.numero_semana = @NumeroSemana)
              AND (@TipoCarga IS NULL OR sc.tipo_carga = @TipoCarga)
              AND (@Estado IS NULL OR sc.estado = @Estado)
            ORDER BY
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