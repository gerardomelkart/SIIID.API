using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SIIID2.Api.Repositories;

public class ActualizacionRepository : IActualizacionRepository
{
    private class ActualizacionConfirmacionInfo
    {
        public long IdCarga { get; set; }
        public string CodigoReferencia { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public DateTime? FechaExpiracion { get; set; }
        public int? IdEntidadFederativaCarga { get; set; }
        public int? IdEntidadFederativaUsuario { get; set; }
        public bool EsSuperUsuario { get; set; }
        public bool HabilitaModificacion { get; set; }
    }

    private class DuplicadoActivoValidacion
    {
        public string Seccion { get; set; } = string.Empty;

        public int TotalGruposDuplicados { get; set; }
    }

    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly ILogger<ActualizacionRepository> _logger;

    public ActualizacionRepository(
        IDbConnectionFactory dbConnectionFactory,
        ILogger<ActualizacionRepository> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _logger = logger;
    }

    public async Task<ConfirmarCargaResponse> ConfirmarActualizacionAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerActualizacionConfirmacionAsync(
                connection,
                transaction,
                codigoReferencia,
                idUsuarioConfirmacion);

            if (carga == null)
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = "NO_ENCONTRADA",
                    Mensaje = "No se encontró una actualización válida para confirmar."
                };
            }

            if (!string.Equals(carga.Estado, "VALIDADO_PENDIENTE_ACTUALIZACION", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = carga.Estado,
                    Mensaje = "La actualización no se encuentra en estado VALIDADO_PENDIENTE_ACTUALIZACION."
                };
            }

            if (carga.FechaExpiracion.HasValue && carga.FechaExpiracion.Value < DateTime.Now)
            {
                await ActualizarActualizacionExpiradaAsync(
                    connection,
                    transaction,
                    carga.IdCarga);

                await transaction.CommitAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = "EXPIRADO_ACTUALIZACION",
                    Mensaje = "La actualización ya expiró. Debe validar nuevamente los archivos."
                };
            }

            if (!carga.HabilitaModificacion)
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = carga.Estado,
                    Mensaje = "El usuario no tiene habilitada la modificación de información."
                };
            }

            if (!carga.EsSuperUsuario &&
                carga.IdEntidadFederativaUsuario.HasValue &&
                carga.IdEntidadFederativaCarga.HasValue &&
                carga.IdEntidadFederativaUsuario.Value != carga.IdEntidadFederativaCarga.Value)
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = carga.Estado,
                    Mensaje = "El usuario no puede confirmar actualizaciones de otra entidad federativa."
                };
            }

            if (!aceptar)
            {
                await RechazarActualizacionAsync(
                    connection,
                    transaction,
                    carga.IdCarga,
                    idUsuarioConfirmacion);

                await transaction.CommitAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = true,
                    CodigoReferencia = codigoReferencia,
                    Estado = "RECHAZADO_USUARIO_ACTUALIZACION",
                    Mensaje = "La actualización fue rechazada por el usuario."
                };
            }

            await AplicarActualizacionCompletaAsync(
                connection,
                transaction,
                carga.IdCarga,
                idUsuarioConfirmacion);

            await ConfirmarActualizacionFinalAsync(
                connection,
                transaction,
                carga.IdCarga,
                idUsuarioConfirmacion);

            await transaction.CommitAsync();

            return new ConfirmarCargaResponse
            {
                EsValido = true,
                CodigoReferencia = codigoReferencia,
                Estado = "CONFIRMADO_ACTUALIZACION",
                Mensaje = "La actualización fue confirmada correctamente."
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<ActualizacionConfirmacionInfo?> ObtenerActualizacionConfirmacionAsync(SqlConnection connection, SqlTransaction transaction, string codigoReferencia, int idUsuarioConfirmacion)
    {
        var sql = @"
        SELECT
            c.id_carga AS IdCarga,
            c.codigo_referencia AS CodigoReferencia,
            c.estado AS Estado,
            c.fecha_expiracion AS FechaExpiracion,
            c.id_entidad_federativa AS IdEntidadFederativaCarga,
            u.id_entidad_federativa AS IdEntidadFederativaUsuario,
            CASE WHEN r.rol = 'SUPER_USUARIO' THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS EsSuperUsuario,
            ISNULL(h.habilita_modificacion, 0) AS HabilitaModificacion
        FROM carga c
        INNER JOIN usuario u
            ON u.id_usuario = @IdUsuarioConfirmacion
           AND u.activo = 1
        INNER JOIN roles r
            ON r.id_rol = u.id_rol
           AND r.activo = 1
        LEFT JOIN habilita_carga_modificacion h
            ON h.id_usuario = u.id_usuario
           AND h.activo = 1
        WHERE c.codigo_referencia = @CodigoReferencia
          AND c.tipo_carga = 'ACTUALIZACION'
          AND c.activo = 1;
    ";

        return await connection.QueryFirstOrDefaultAsync<ActualizacionConfirmacionInfo>(
            sql,
            new
            {
                CodigoReferencia = codigoReferencia,
                IdUsuarioConfirmacion = idUsuarioConfirmacion
            },
            transaction);
    }

    private async Task RechazarActualizacionAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, int idUsuarioConfirmacion)
    {
        var sql = @"
        UPDATE carga
        SET estado = 'RECHAZADO_USUARIO_ACTUALIZACION',
            fecha_confirmacion = SYSDATETIME(),
            id_usuario_confirmacion = @IdUsuarioConfirmacion
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_carpeta
        SET estado = 'RECHAZADO_USUARIO_ACTUALIZACION',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_delito
        SET estado = 'RECHAZADO_USUARIO_ACTUALIZACION',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_victima
        SET estado = 'RECHAZADO_USUARIO_ACTUALIZACION',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;
    ";

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdCarga = idCarga,
                IdUsuarioConfirmacion = idUsuarioConfirmacion
            },
            transaction);
    }

    private async Task ActualizarActualizacionExpiradaAsync(SqlConnection connection, SqlTransaction transaction, long idCarga)
    {
        var sql = @"
        UPDATE carga
        SET estado = 'EXPIRADO_ACTUALIZACION',
            mensaje_error = 'La actualización expiró antes de ser confirmada.'
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_carpeta
        SET estado = 'EXPIRADO_ACTUALIZACION',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_delito
        SET estado = 'EXPIRADO_ACTUALIZACION',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_victima
        SET estado = 'EXPIRADO_ACTUALIZACION',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;
    ";

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdCarga = idCarga
            },
            transaction);
    }

    private async Task InsertarHistoricoCarpetasModificadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        carpetas_actuales AS (
            SELECT
                ci.*,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, ci.id_carga DESC, ci.id_carpeta_investigacion DESC
                ) AS rn
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
        ),
        carpetas_modificadas AS (
            SELECT ca.*
            FROM carpetas_actuales ca
            INNER JOIN carga_tmp_carpeta ct
                ON ct.id_ci = ca.identificador_carpeta_fiscalia
               AND ct.id_carga = @IdCargaActualizacion
               AND ct.activo = 1
            WHERE ca.rn = 1
              AND (
                    ISNULL(ca.nomenclatura_carpeta_fiscalia, '') <> ISNULL(ct.ntra_ci, '')
                    OR ISNULL(CONVERT(varchar(19), ca.fecha_inicio, 120), '') <> ISNULL(CONVERT(varchar(19), COALESCE(
                    TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), '')), 103),
                    TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''))),
                    CASE
                        WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) IS NOT NULL
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) >= 0
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) < 1
                        THEN DATEADD(
                            SECOND,
                            CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) * 86400, 0)),
                            COALESCE(
                                TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                                TRY_CONVERT(datetime2, ct.fha_de_ini)
                            )
                        )
                    END,
                    TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                    TRY_CONVERT(datetime2, ct.fha_de_ini)
                ), 120), '')
                    OR ISNULL(ca.resumen_hechos, '') <> ISNULL(ct.rmen_de_hchos, '')
                  )
        )
        INSERT INTO carpeta_investigacion_historico (
            id_carpeta_investigacion,
            identificador_carpeta_fiscalia,
            nomenclatura_carpeta_fiscalia,
            fecha_inicio,
            resumen_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            id_usuario_modificacion,
            id_carga_nueva,
            tipo_movimiento,
            fecha_modificacion,
            activo
        )
        SELECT
            id_carpeta_investigacion,
            identificador_carpeta_fiscalia,
            nomenclatura_carpeta_fiscalia,
            fecha_inicio,
            resumen_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            @IdUsuarioConfirmacion,
            @IdCargaActualizacion,
            'MODIFICADO',
            SYSDATETIME(),
            activo
        FROM carpetas_modificadas;
    ";

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdCargaActualizacion = idCargaActualizacion,
                IdUsuarioConfirmacion = idUsuarioConfirmacion
            },
            transaction);
    }

    private async Task ActualizarCarpetasModificadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        carpetas_actuales AS (
            SELECT
                ci.id_carpeta_investigacion,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, ci.id_carga DESC, ci.id_carpeta_investigacion DESC
                ) AS rn
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
        )
        UPDATE ci
        SET ci.nomenclatura_carpeta_fiscalia = ct.ntra_ci,
            ci.fecha_inicio = COALESCE(
                TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), '')), 103),
                TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''))),
                CASE
                    WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) IS NOT NULL
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) >= 0
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) < 1
                    THEN DATEADD(
                        SECOND,
                        CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) * 86400, 0)),
                        COALESCE(
                            TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                            TRY_CONVERT(datetime2, ct.fha_de_ini)
                        )
                    )
                END,
                TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                TRY_CONVERT(datetime2, ct.fha_de_ini)
            ),
            ci.resumen_hechos = ct.rmen_de_hchos,
            ci.id_carga = @IdCargaActualizacion
        FROM carpeta_investigacion ci
        INNER JOIN carpetas_actuales ca
            ON ca.id_carpeta_investigacion = ci.id_carpeta_investigacion
           AND ca.rn = 1
        INNER JOIN carga_tmp_carpeta ct
            ON ct.id_ci = ci.identificador_carpeta_fiscalia
           AND ct.id_carga = @IdCargaActualizacion
           AND ct.activo = 1
        WHERE ci.activo = 1
          AND (
                ISNULL(ci.nomenclatura_carpeta_fiscalia, '') <> ISNULL(ct.ntra_ci, '')
                OR ISNULL(CONVERT(varchar(19), ci.fecha_inicio, 120), '') <> ISNULL(CONVERT(varchar(19), COALESCE(
                TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), '')), 103),
                TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''))),
                CASE
                    WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) IS NOT NULL
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) >= 0
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) < 1
                    THEN DATEADD(
                        SECOND,
                        CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) * 86400, 0)),
                        COALESCE(
                            TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                            TRY_CONVERT(datetime2, ct.fha_de_ini)
                        )
                    )
                END,
                TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                TRY_CONVERT(datetime2, ct.fha_de_ini)
            ), 120), '')
                OR ISNULL(ci.resumen_hechos, '') <> ISNULL(ct.rmen_de_hchos, '')
              );
    ";

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdCargaActualizacion = idCargaActualizacion
            },
            transaction);
    }

    private async Task InsertarCarpetasNuevasActualizacionAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        )
        INSERT INTO carpeta_investigacion (
            identificador_carpeta_fiscalia,
            nomenclatura_carpeta_fiscalia,
            fecha_inicio,
            resumen_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            activo
        )
        SELECT
            ct.id_ci,
            ct.ntra_ci,
            COALESCE(
                TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), '')), 103),
                TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''))),
                CASE
                    WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) IS NOT NULL
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) >= 0
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) < 1
                    THEN DATEADD(
                        SECOND,
                        CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) * 86400, 0)),
                        COALESCE(
                            TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                            TRY_CONVERT(datetime2, ct.fha_de_ini)
                        )
                    )
                END,
                TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                TRY_CONVERT(datetime2, ct.fha_de_ini)
            ),
            ct.rmen_de_hchos,
            @IdUsuarioConfirmacion,
            SYSDATETIME(),
            @IdCargaActualizacion,
            1
        FROM carga_tmp_carpeta ct
        WHERE ct.id_carga = @IdCargaActualizacion
          AND ct.activo = 1
          AND NOT EXISTS (
              SELECT 1
              FROM carpeta_investigacion ci
              LEFT JOIN cargas_periodo cp
                  ON cp.id_carga = ci.id_carga
              WHERE ci.identificador_carpeta_fiscalia = ct.id_ci
                AND ci.activo = 1
                AND (
                      cp.id_carga IS NOT NULL
                      OR ci.id_carga = @IdCargaActualizacion
                    )
           );
    ";

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdCargaActualizacion = idCargaActualizacion,
                IdUsuarioConfirmacion = idUsuarioConfirmacion
            },
            transaction);
    }

    private async Task InsertarHistoricoCarpetasEliminadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        carpetas_actuales AS (
            SELECT
                ci.*,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, ci.id_carga DESC, ci.id_carpeta_investigacion DESC
                ) AS rn
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
        ),
        carpetas_eliminadas AS (
            SELECT ca.*
            FROM carpetas_actuales ca
            LEFT JOIN carga_tmp_carpeta ct
                ON ct.id_ci = ca.identificador_carpeta_fiscalia
               AND ct.id_carga = @IdCargaActualizacion
               AND ct.activo = 1
            WHERE ca.rn = 1
              AND ct.id_ci IS NULL
        )
        INSERT INTO carpeta_investigacion_historico (
            id_carpeta_investigacion,
            identificador_carpeta_fiscalia,
            nomenclatura_carpeta_fiscalia,
            fecha_inicio,
            resumen_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            id_usuario_modificacion,
            id_carga_nueva,
            tipo_movimiento,
            fecha_modificacion,
            activo
        )
        SELECT
            id_carpeta_investigacion,
            identificador_carpeta_fiscalia,
            nomenclatura_carpeta_fiscalia,
            fecha_inicio,
            resumen_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            @IdUsuarioConfirmacion,
            @IdCargaActualizacion,
            'ELIMINADO',
            SYSDATETIME(),
            activo
        FROM carpetas_eliminadas;
    ";

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdCargaActualizacion = idCargaActualizacion,
                IdUsuarioConfirmacion = idUsuarioConfirmacion
            },
            transaction);
    }

    private async Task DesactivarCarpetasEliminadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        carpetas_actuales AS (
            SELECT
                ci.id_carpeta_investigacion,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, ci.id_carga DESC, ci.id_carpeta_investigacion DESC
                ) AS rn
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
        )
        UPDATE ci
        SET ci.activo = 0,
            ci.id_carga = @IdCargaActualizacion
        FROM carpeta_investigacion ci
        INNER JOIN carpetas_actuales ca
            ON ca.id_carpeta_investigacion = ci.id_carpeta_investigacion
           AND ca.rn = 1
        LEFT JOIN carga_tmp_carpeta ct
            ON ct.id_ci = ci.identificador_carpeta_fiscalia
           AND ct.id_carga = @IdCargaActualizacion
           AND ct.activo = 1
        WHERE ci.activo = 1
          AND ct.id_ci IS NULL;
    ";

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdCargaActualizacion = idCargaActualizacion
            },
            transaction);
    }

    private async Task InsertarHistoricoDelitosModificadosAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        delitos_actuales AS (
            SELECT
                d.*,
                ci.identificador_carpeta_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, d.id_carga DESC, d.id_delito DESC
                ) AS rn
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            WHERE d.activo = 1
        ),
        delitos_tmp AS (
            SELECT
                d.id_ci,
                d.id_delito,
                d.dto,
                d.moda_dto,
                fa.id_forma_accion,
                COALESCE(
                    TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), '')), 103),
                    TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''))),
                    CASE
                        WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) IS NOT NULL
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) >= 0
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) < 1
                        THEN DATEADD(
                            SECOND,
                            CONVERT(int, FLOOR(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) * 86400)),
                            COALESCE(
                                TRY_CONVERT(datetime2, d.fha_de_hchos, 103),
                                TRY_CONVERT(datetime2, d.fha_de_hchos)
                            )
                        )
                    END,
                    TRY_CONVERT(datetime2, d.fha_de_hchos, 103),
                    TRY_CONVERT(datetime2, d.fha_de_hchos)
                ) AS fecha_hechos,
                ic.id_instrumento_comision,
                gc.id_grado_consumacion,
                md.id_modalidad_delito,
                ef.id_entidad_federativa,
                mun.id_municipio,
                d.id_loc_hchos,
                d.nom_loc_hchos,
                d.id_col_hchos,
                d.nom_col_hchos,
                cp.id_codigo_postal,
                TRY_CONVERT(decimal(10,6), NULLIF(d.coord_x, '')) AS coordenada_x,
                TRY_CONVERT(decimal(10,6), NULLIF(d.coord_y, '')) AS coordenada_y,
                d.dom_hchos
            FROM carga_tmp_delito d
            INNER JOIN catalogo_modalidad_delito md
                ON md.clave4 = d.clasf_de_dto
               AND md.activo = 1
            INNER JOIN catalogo_forma_accion fa
                ON fa.clave = TRY_CONVERT(tinyint, d.forma_acc)
               AND fa.activo = 1
            INNER JOIN catalogo_instrumento_comision ic
                ON ic.clave = TRY_CONVERT(tinyint, d.emto_com_dto)
               AND ic.activo = 1
            INNER JOIN catalogo_grado_consumacion gc
                ON gc.clave = TRY_CONVERT(tinyint, d.grdo_cons)
               AND gc.activo = 1
            INNER JOIN catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos)
               AND ef.activo = 1
            INNER JOIN catalogo_municipio mun
                ON mun.id_entidad_federativa = ef.id_entidad_federativa
               AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos)
               AND mun.activo = 1
            OUTER APPLY (
                SELECT TOP 1
                    ccp.id_codigo_postal
                FROM catalogo_codigo_postal ccp
                WHERE ccp.codigo_postal = RIGHT('00000' + LTRIM(RTRIM(d.cp)), 5)
                  AND ccp.id_municipio = mun.id_municipio
                  AND ccp.activo = 1
                ORDER BY ccp.id_codigo_postal
            ) cp
            WHERE d.id_carga = @IdCargaActualizacion
              AND d.activo = 1
        ),
        delitos_modificados AS (
            SELECT da.*
            FROM delitos_actuales da
            INNER JOIN delitos_tmp dt
                ON dt.id_ci = da.identificador_carpeta_fiscalia
               AND dt.id_delito = da.identificador_delito_fiscalia
            WHERE da.rn = 1
              AND (
                    ISNULL(da.delito_fiscalia, '') <> ISNULL(dt.dto, '')
                    OR ISNULL(da.modalidad_delito_fiscalia, '') <> ISNULL(dt.moda_dto, '')
                    OR ISNULL(da.id_forma_accion, 0) <> ISNULL(dt.id_forma_accion, 0)
                    OR ISNULL(CONVERT(varchar(19), da.fecha_hechos, 120), '') <> ISNULL(CONVERT(varchar(19), dt.fecha_hechos, 120), '')
                    OR ISNULL(da.id_instrumento_comision, 0) <> ISNULL(dt.id_instrumento_comision, 0)
                    OR ISNULL(da.id_grado_consumacion, 0) <> ISNULL(dt.id_grado_consumacion, 0)
                    OR ISNULL(da.id_modalidad_delito, 0) <> ISNULL(dt.id_modalidad_delito, 0)
                    OR ISNULL(da.id_entidad_federativa, 0) <> ISNULL(dt.id_entidad_federativa, 0)
                    OR ISNULL(da.id_municipio, 0) <> ISNULL(dt.id_municipio, 0)
                    OR ISNULL(da.id_localidad_fiscalia, '') <> ISNULL(dt.id_loc_hchos, '')
                    OR ISNULL(da.localidad_fiscalia_nombre, '') <> ISNULL(dt.nom_loc_hchos, '')
                    OR ISNULL(da.id_colonia_fiscalia, '') <> ISNULL(dt.id_col_hchos, '')
                    OR ISNULL(da.colonia_fiscalia_nombre, '') <> ISNULL(dt.nom_col_hchos, '')
                    OR ISNULL(da.id_codigo_postal, 0) <> ISNULL(dt.id_codigo_postal, 0)
                    OR ISNULL(da.coordenada_x, 0) <> ISNULL(dt.coordenada_x, 0)
                    OR ISNULL(da.coordenada_y, 0) <> ISNULL(dt.coordenada_y, 0)
                    OR ISNULL(da.domicilio_hechos, '') <> ISNULL(dt.dom_hchos, '')
                  )
        )
        INSERT INTO delito_historico (
            id_delito,
            id_carpeta_investigacion,
            identificador_delito_fiscalia,
            delito_fiscalia,
            modalidad_delito_fiscalia,
            id_forma_accion,
            fecha_hechos,
            id_instrumento_comision,
            id_grado_consumacion,
            id_modalidad_delito,
            id_entidad_federativa,
            id_municipio,
            id_localidad_fiscalia,
            localidad_fiscalia_nombre,
            id_colonia_fiscalia,
            colonia_fiscalia_nombre,
            id_codigo_postal,
            coordenada_x,
            coordenada_y,
            domicilio_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            id_usuario_modificacion,
            id_carga_nueva,
            tipo_movimiento,
            fecha_modificacion,
            activo
        )
        SELECT
            id_delito,
            id_carpeta_investigacion,
            identificador_delito_fiscalia,
            delito_fiscalia,
            modalidad_delito_fiscalia,
            id_forma_accion,
            fecha_hechos,
            id_instrumento_comision,
            id_grado_consumacion,
            id_modalidad_delito,
            id_entidad_federativa,
            id_municipio,
            id_localidad_fiscalia,
            localidad_fiscalia_nombre,
            id_colonia_fiscalia,
            colonia_fiscalia_nombre,
            id_codigo_postal,
            coordenada_x,
            coordenada_y,
            domicilio_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            @IdUsuarioConfirmacion,
            @IdCargaActualizacion,
            'MODIFICADO',
            SYSDATETIME(),
            activo
        FROM delitos_modificados;
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion,
            IdUsuarioConfirmacion = idUsuarioConfirmacion
        }, transaction);
    }

    private async Task ActualizarDelitosModificadosAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        delitos_actuales AS (
            SELECT
                d.id_delito,
                ci.identificador_carpeta_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, d.id_carga DESC, d.id_delito DESC
                ) AS rn
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            WHERE d.activo = 1
        ),
        delitos_tmp AS (
            SELECT
                d.id_ci,
                d.id_delito,
                d.dto,
                d.moda_dto,
                fa.id_forma_accion,
                COALESCE(
                    TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), '')), 103),
                    TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''))),
                    CASE
                        WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) IS NOT NULL
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) >= 0
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) < 1
                        THEN DATEADD(
                            SECOND,
                            CONVERT(int, FLOOR(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) * 86400)),
                            COALESCE(
                                TRY_CONVERT(datetime2, d.fha_de_hchos, 103),
                                TRY_CONVERT(datetime2, d.fha_de_hchos)
                            )
                        )
                    END,
                    TRY_CONVERT(datetime2, d.fha_de_hchos, 103),
                    TRY_CONVERT(datetime2, d.fha_de_hchos)
                ) AS fecha_hechos,
                ic.id_instrumento_comision,
                gc.id_grado_consumacion,
                md.id_modalidad_delito,
                ef.id_entidad_federativa,
                mun.id_municipio,
                d.id_loc_hchos,
                d.nom_loc_hchos,
                d.id_col_hchos,
                d.nom_col_hchos,
                cp.id_codigo_postal,
                TRY_CONVERT(decimal(10,6), NULLIF(d.coord_x, '')) AS coordenada_x,
                TRY_CONVERT(decimal(10,6), NULLIF(d.coord_y, '')) AS coordenada_y,
                d.dom_hchos
            FROM carga_tmp_delito d
            INNER JOIN catalogo_modalidad_delito md
                ON md.clave4 = d.clasf_de_dto
               AND md.activo = 1
            INNER JOIN catalogo_forma_accion fa
                ON fa.clave = TRY_CONVERT(tinyint, d.forma_acc)
               AND fa.activo = 1
            INNER JOIN catalogo_instrumento_comision ic
                ON ic.clave = TRY_CONVERT(tinyint, d.emto_com_dto)
               AND ic.activo = 1
            INNER JOIN catalogo_grado_consumacion gc
                ON gc.clave = TRY_CONVERT(tinyint, d.grdo_cons)
               AND gc.activo = 1
            INNER JOIN catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos)
               AND ef.activo = 1
            INNER JOIN catalogo_municipio mun
                ON mun.id_entidad_federativa = ef.id_entidad_federativa
               AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos)
               AND mun.activo = 1
            OUTER APPLY (
                SELECT TOP 1
                    ccp.id_codigo_postal
                FROM catalogo_codigo_postal ccp
                WHERE ccp.codigo_postal = RIGHT('00000' + LTRIM(RTRIM(d.cp)), 5)
                  AND ccp.id_municipio = mun.id_municipio
                  AND ccp.activo = 1
                ORDER BY ccp.id_codigo_postal
            ) cp
            WHERE d.id_carga = @IdCargaActualizacion
              AND d.activo = 1
        )
        UPDATE de
        SET de.delito_fiscalia = dt.dto,
            de.modalidad_delito_fiscalia = dt.moda_dto,
            de.id_forma_accion = dt.id_forma_accion,
            de.fecha_hechos = dt.fecha_hechos,
            de.id_instrumento_comision = dt.id_instrumento_comision,
            de.id_grado_consumacion = dt.id_grado_consumacion,
            de.id_modalidad_delito = dt.id_modalidad_delito,
            de.id_entidad_federativa = dt.id_entidad_federativa,
            de.id_municipio = dt.id_municipio,
            de.id_localidad_fiscalia = dt.id_loc_hchos,
            de.localidad_fiscalia_nombre = dt.nom_loc_hchos,
            de.id_colonia_fiscalia = dt.id_col_hchos,
            de.colonia_fiscalia_nombre = dt.nom_col_hchos,
            de.id_codigo_postal = dt.id_codigo_postal,
            de.coordenada_x = dt.coordenada_x,
            de.coordenada_y = dt.coordenada_y,
            de.domicilio_hechos = dt.dom_hchos,
            de.id_carga = @IdCargaActualizacion
        FROM delito de
        INNER JOIN delitos_actuales da
            ON da.id_delito = de.id_delito
           AND da.rn = 1
        INNER JOIN delitos_tmp dt
            ON dt.id_ci = da.identificador_carpeta_fiscalia
           AND dt.id_delito = de.identificador_delito_fiscalia
        WHERE de.activo = 1
          AND (
                ISNULL(de.delito_fiscalia, '') <> ISNULL(dt.dto, '')
                OR ISNULL(de.modalidad_delito_fiscalia, '') <> ISNULL(dt.moda_dto, '')
                OR ISNULL(de.id_forma_accion, 0) <> ISNULL(dt.id_forma_accion, 0)
                OR ISNULL(CONVERT(varchar(19), de.fecha_hechos, 120), '') <> ISNULL(CONVERT(varchar(19), dt.fecha_hechos, 120), '')
                OR ISNULL(de.id_instrumento_comision, 0) <> ISNULL(dt.id_instrumento_comision, 0)
                OR ISNULL(de.id_grado_consumacion, 0) <> ISNULL(dt.id_grado_consumacion, 0)
                OR ISNULL(de.id_modalidad_delito, 0) <> ISNULL(dt.id_modalidad_delito, 0)
                OR ISNULL(de.id_entidad_federativa, 0) <> ISNULL(dt.id_entidad_federativa, 0)
                OR ISNULL(de.id_municipio, 0) <> ISNULL(dt.id_municipio, 0)
                OR ISNULL(de.id_localidad_fiscalia, '') <> ISNULL(dt.id_loc_hchos, '')
                OR ISNULL(de.localidad_fiscalia_nombre, '') <> ISNULL(dt.nom_loc_hchos, '')
                OR ISNULL(de.id_colonia_fiscalia, '') <> ISNULL(dt.id_col_hchos, '')
                OR ISNULL(de.colonia_fiscalia_nombre, '') <> ISNULL(dt.nom_col_hchos, '')
                OR ISNULL(de.id_codigo_postal, 0) <> ISNULL(dt.id_codigo_postal, 0)
                OR ISNULL(de.coordenada_x, 0) <> ISNULL(dt.coordenada_x, 0)
                OR ISNULL(de.coordenada_y, 0) <> ISNULL(dt.coordenada_y, 0)
                OR ISNULL(de.domicilio_hechos, '') <> ISNULL(dt.dom_hchos, '')
              );
         ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion
        }, transaction);
    }

    private async Task InsertarDelitosNuevosActualizacionAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1

            UNION

            SELECT @IdCargaActualizacion
        ),
        carpetas_vigentes AS (
            SELECT
                ci.id_carpeta_investigacion,
                ci.identificador_carpeta_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia
                    ORDER BY ci.id_carga DESC, ci.id_carpeta_investigacion DESC
                ) AS rn
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
        )
        INSERT INTO delito (
            id_carpeta_investigacion,
            identificador_delito_fiscalia,
            delito_fiscalia,
            modalidad_delito_fiscalia,
            id_forma_accion,
            fecha_hechos,
            id_instrumento_comision,
            id_grado_consumacion,
            id_modalidad_delito,
            id_entidad_federativa,
            id_municipio,
            id_localidad_fiscalia,
            localidad_fiscalia_nombre,
            id_colonia_fiscalia,
            colonia_fiscalia_nombre,
            id_codigo_postal,
            coordenada_x,
            coordenada_y,
            domicilio_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            activo
        )
        SELECT
            cv.id_carpeta_investigacion,
            d.id_delito,
            d.dto,
            d.moda_dto,
            fa.id_forma_accion,
            COALESCE(
                TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), '')), 103),
                TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''))),
                CASE
                    WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) IS NOT NULL
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) >= 0
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) < 1
                    THEN DATEADD(
                        SECOND,
                        CONVERT(int, FLOOR(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) * 86400)),
                        COALESCE(
                            TRY_CONVERT(datetime2, d.fha_de_hchos, 103),
                            TRY_CONVERT(datetime2, d.fha_de_hchos)
                        )
                    )
                END,
                TRY_CONVERT(datetime2, d.fha_de_hchos, 103),
                TRY_CONVERT(datetime2, d.fha_de_hchos)
            ),
            ic.id_instrumento_comision,
            gc.id_grado_consumacion,
            md.id_modalidad_delito,
            ef.id_entidad_federativa,
            mun.id_municipio,
            d.id_loc_hchos,
            d.nom_loc_hchos,
            d.id_col_hchos,
            d.nom_col_hchos,
            cp.id_codigo_postal,
            TRY_CONVERT(decimal(10,6), NULLIF(d.coord_x, '')),
            TRY_CONVERT(decimal(10,6), NULLIF(d.coord_y, '')),
            d.dom_hchos,
            @IdUsuarioConfirmacion,
            SYSDATETIME(),
            @IdCargaActualizacion,
            1
        FROM carga_tmp_delito d
        INNER JOIN carpetas_vigentes cv
            ON cv.identificador_carpeta_fiscalia = d.id_ci
           AND cv.rn = 1
        INNER JOIN catalogo_modalidad_delito md
            ON md.clave4 = d.clasf_de_dto
           AND md.activo = 1
        INNER JOIN catalogo_forma_accion fa
            ON fa.clave = TRY_CONVERT(tinyint, d.forma_acc)
           AND fa.activo = 1
        INNER JOIN catalogo_instrumento_comision ic
            ON ic.clave = TRY_CONVERT(tinyint, d.emto_com_dto)
           AND ic.activo = 1
        INNER JOIN catalogo_grado_consumacion gc
            ON gc.clave = TRY_CONVERT(tinyint, d.grdo_cons)
           AND gc.activo = 1
        INNER JOIN catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos)
           AND ef.activo = 1
        INNER JOIN catalogo_municipio mun
            ON mun.id_entidad_federativa = ef.id_entidad_federativa
           AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos)
           AND mun.activo = 1
        OUTER APPLY (
            SELECT TOP 1
                ccp.id_codigo_postal
            FROM catalogo_codigo_postal ccp
            WHERE ccp.codigo_postal = RIGHT('00000' + LTRIM(RTRIM(d.cp)), 5)
              AND ccp.id_municipio = mun.id_municipio
              AND ccp.activo = 1
            ORDER BY ccp.id_codigo_postal
        ) cp
        WHERE d.id_carga = @IdCargaActualizacion
          AND d.activo = 1
          AND NOT EXISTS (
                SELECT 1
                FROM delito de
                WHERE de.id_carpeta_investigacion = cv.id_carpeta_investigacion
                  AND de.identificador_delito_fiscalia = d.id_delito
                  AND de.activo = 1
          );
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion,
            IdUsuarioConfirmacion = idUsuarioConfirmacion
        }, transaction);
    }

    private async Task InsertarHistoricoDelitosEliminadosAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        delitos_actuales AS (
            SELECT
                d.*,
                ci.identificador_carpeta_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, d.id_carga DESC, d.id_delito DESC
                ) AS rn
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            WHERE d.activo = 1
        ),
        delitos_eliminados AS (
            SELECT da.*
            FROM delitos_actuales da
            LEFT JOIN carga_tmp_delito dt
                ON dt.id_ci = da.identificador_carpeta_fiscalia
               AND dt.id_delito = da.identificador_delito_fiscalia
               AND dt.id_carga = @IdCargaActualizacion
               AND dt.activo = 1
            WHERE da.rn = 1
              AND dt.id_delito IS NULL
        )
        INSERT INTO delito_historico (
            id_delito,
            id_carpeta_investigacion,
            identificador_delito_fiscalia,
            delito_fiscalia,
            modalidad_delito_fiscalia,
            id_forma_accion,
            fecha_hechos,
            id_instrumento_comision,
            id_grado_consumacion,
            id_modalidad_delito,
            id_entidad_federativa,
            id_municipio,
            id_localidad_fiscalia,
            localidad_fiscalia_nombre,
            id_colonia_fiscalia,
            colonia_fiscalia_nombre,
            id_codigo_postal,
            coordenada_x,
            coordenada_y,
            domicilio_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            id_usuario_modificacion,
            id_carga_nueva,
            tipo_movimiento,
            fecha_modificacion,
            activo
        )
        SELECT
            id_delito,
            id_carpeta_investigacion,
            identificador_delito_fiscalia,
            delito_fiscalia,
            modalidad_delito_fiscalia,
            id_forma_accion,
            fecha_hechos,
            id_instrumento_comision,
            id_grado_consumacion,
            id_modalidad_delito,
            id_entidad_federativa,
            id_municipio,
            id_localidad_fiscalia,
            localidad_fiscalia_nombre,
            id_colonia_fiscalia,
            colonia_fiscalia_nombre,
            id_codigo_postal,
            coordenada_x,
            coordenada_y,
            domicilio_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            @IdUsuarioConfirmacion,
            @IdCargaActualizacion,
            'ELIMINADO',
            SYSDATETIME(),
            activo
        FROM delitos_eliminados;
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion,
            IdUsuarioConfirmacion = idUsuarioConfirmacion
        }, transaction);
    }

    private async Task DesactivarDelitosEliminadosAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        delitos_actuales AS (
            SELECT
                d.id_delito,
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, d.id_carga DESC, d.id_delito DESC
                ) AS rn
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            WHERE d.activo = 1
        )
        UPDATE de
        SET de.activo = 0,
            de.id_carga = @IdCargaActualizacion
        FROM delito de
        INNER JOIN delitos_actuales da
            ON da.id_delito = de.id_delito
           AND da.rn = 1
        LEFT JOIN carga_tmp_delito dt
            ON dt.id_ci = da.identificador_carpeta_fiscalia
           AND dt.id_delito = da.identificador_delito_fiscalia
           AND dt.id_carga = @IdCargaActualizacion
           AND dt.activo = 1
        WHERE de.activo = 1
          AND dt.id_delito IS NULL;
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion
        }, transaction);
    }

    private async Task InsertarHistoricoVictimasModificadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        victimas_actuales AS (
            SELECT
                v.*,
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia, v.identificador_victima_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, v.id_carga DESC, v.id_victima DESC
                ) AS rn
            FROM victima v
            INNER JOIN delito d
                ON d.id_delito = v.id_delito
               AND d.activo = 1
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = v.id_carga
            WHERE v.activo = 1
        ),
        victimas_tmp AS (
            SELECT
                v.id_ci,
                v.id_delito,
                v.id_vicf,
                tv.id_tipo_victima,
                tvm.id_tipo_victima_moral,
                sx.id_sexo,
                gen.id_genero,
                nac.id_nacionalidad,
                pob.id_pertenece_poblacion_indigena,
                disc.id_presenta_discapacidad,
                COALESCE(
                    TRY_CONVERT(date, NULLIF(v.fha_nac, ''), 103),
                    TRY_CONVERT(date, NULLIF(v.fha_nac, ''))
                ) AS fecha_nacimiento,
                CASE
                    WHEN TRY_CONVERT(int, NULLIF(v.edad, '')) = 999 THEN NULL
                    ELSE TRY_CONVERT(tinyint, NULLIF(v.edad, ''))
                END AS edad
            FROM carga_tmp_victima v
            INNER JOIN catalogo_tipo_victima tv
                ON tv.clave = TRY_CONVERT(tinyint, v.id_tv)
               AND tv.activo = 1
            LEFT JOIN catalogo_tipo_victima_moral tvm
                ON tvm.clave = TRY_CONVERT(tinyint, NULLIF(v.id_tpm, ''))
               AND tvm.activo = 1
            LEFT JOIN catalogo_sexo sx
                ON sx.clave = TRY_CONVERT(tinyint, NULLIF(v.sexo, ''))
               AND sx.activo = 1
            LEFT JOIN catalogo_genero gen
                ON gen.clave = TRY_CONVERT(tinyint, NULLIF(v.genero, ''))
               AND gen.activo = 1
            LEFT JOIN catalogo_nacionalidad nac
                ON TRY_CONVERT(int, nac.clave) = TRY_CONVERT(int, NULLIF(v.nacional, ''))
               AND nac.activo = 1
            LEFT JOIN catalogo_pertenece_poblacion_indigena pob
                ON pob.clave = TRY_CONVERT(tinyint, NULLIF(v.pob, ''))
               AND pob.activo = 1
            LEFT JOIN catalogo_presenta_discapacidad disc
                ON disc.clave = TRY_CONVERT(tinyint, NULLIF(v.disc, ''))
               AND disc.activo = 1
            WHERE v.id_carga = @IdCargaActualizacion
              AND v.activo = 1
        ),
        victimas_modificadas AS (
            SELECT va.*
            FROM victimas_actuales va
            INNER JOIN victimas_tmp vt
                ON vt.id_ci = va.identificador_carpeta_fiscalia
               AND vt.id_delito = va.identificador_delito_fiscalia
               AND vt.id_vicf = va.identificador_victima_fiscalia
            WHERE va.rn = 1
              AND (
                    ISNULL(va.id_tipo_victima, 0) <> ISNULL(vt.id_tipo_victima, 0)
                    OR ISNULL(va.id_tipo_victima_moral, 0) <> ISNULL(vt.id_tipo_victima_moral, 0)
                    OR ISNULL(va.id_sexo, 0) <> ISNULL(vt.id_sexo, 0)
                    OR ISNULL(va.id_genero, 0) <> ISNULL(vt.id_genero, 0)
                    OR ISNULL(va.id_nacionalidad, 0) <> ISNULL(vt.id_nacionalidad, 0)
                    OR ISNULL(va.id_pertenece_poblacion_indigena, 0) <> ISNULL(vt.id_pertenece_poblacion_indigena, 0)
                    OR ISNULL(va.id_presenta_discapacidad, 0) <> ISNULL(vt.id_presenta_discapacidad, 0)
                    OR ISNULL(CONVERT(varchar(10), va.fecha_nacimiento, 120), '') <> ISNULL(CONVERT(varchar(10), vt.fecha_nacimiento, 120), '')
                    OR ISNULL(va.edad, 0) <> ISNULL(vt.edad, 0)
                  )
        )
        INSERT INTO victima_historico (
            id_victima,
            id_delito,
            identificador_victima_fiscalia,
            id_tipo_victima,
            id_tipo_victima_moral,
            id_sexo,
            id_genero,
            id_nacionalidad,
            id_pertenece_poblacion_indigena,
            id_presenta_discapacidad,
            fecha_nacimiento,
            edad,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            id_usuario_modificacion,
            id_carga_nueva,
            tipo_movimiento,
            fecha_modificacion,
            activo
        )
        SELECT
            id_victima,
            id_delito,
            identificador_victima_fiscalia,
            id_tipo_victima,
            id_tipo_victima_moral,
            id_sexo,
            id_genero,
            id_nacionalidad,
            id_pertenece_poblacion_indigena,
            id_presenta_discapacidad,
            fecha_nacimiento,
            edad,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            @IdUsuarioConfirmacion,
            @IdCargaActualizacion,
            'MODIFICADO',
            SYSDATETIME(),
            activo
        FROM victimas_modificadas;
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion,
            IdUsuarioConfirmacion = idUsuarioConfirmacion
        }, transaction);
    }

    private async Task ActualizarVictimasModificadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        victimas_actuales AS (
            SELECT
                v.id_victima,
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia, v.identificador_victima_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, v.id_carga DESC, v.id_victima DESC
                ) AS rn
            FROM victima v
            INNER JOIN delito d
                ON d.id_delito = v.id_delito
               AND d.activo = 1
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = v.id_carga
            WHERE v.activo = 1
        ),
        victimas_tmp AS (
            SELECT
                v.id_ci,
                v.id_delito,
                v.id_vicf,
                tv.id_tipo_victima,
                tvm.id_tipo_victima_moral,
                sx.id_sexo,
                gen.id_genero,
                nac.id_nacionalidad,
                pob.id_pertenece_poblacion_indigena,
                disc.id_presenta_discapacidad,
                COALESCE(
                    TRY_CONVERT(date, NULLIF(v.fha_nac, ''), 103),
                    TRY_CONVERT(date, NULLIF(v.fha_nac, ''))
                ) AS fecha_nacimiento,
                CASE
                    WHEN TRY_CONVERT(int, NULLIF(v.edad, '')) = 999 THEN NULL
                    ELSE TRY_CONVERT(tinyint, NULLIF(v.edad, ''))
                END AS edad
            FROM carga_tmp_victima v
            INNER JOIN catalogo_tipo_victima tv
                ON tv.clave = TRY_CONVERT(tinyint, v.id_tv)
               AND tv.activo = 1
            LEFT JOIN catalogo_tipo_victima_moral tvm
                ON tvm.clave = TRY_CONVERT(tinyint, NULLIF(v.id_tpm, ''))
               AND tvm.activo = 1
            LEFT JOIN catalogo_sexo sx
                ON sx.clave = TRY_CONVERT(tinyint, NULLIF(v.sexo, ''))
               AND sx.activo = 1
            LEFT JOIN catalogo_genero gen
                ON gen.clave = TRY_CONVERT(tinyint, NULLIF(v.genero, ''))
               AND gen.activo = 1
            LEFT JOIN catalogo_nacionalidad nac
                ON TRY_CONVERT(int, nac.clave) = TRY_CONVERT(int, NULLIF(v.nacional, ''))
               AND nac.activo = 1
            LEFT JOIN catalogo_pertenece_poblacion_indigena pob
                ON pob.clave = TRY_CONVERT(tinyint, NULLIF(v.pob, ''))
               AND pob.activo = 1
            LEFT JOIN catalogo_presenta_discapacidad disc
                ON disc.clave = TRY_CONVERT(tinyint, NULLIF(v.disc, ''))
               AND disc.activo = 1
            WHERE v.id_carga = @IdCargaActualizacion
              AND v.activo = 1
        )
        UPDATE vi
        SET vi.id_tipo_victima = vt.id_tipo_victima,
            vi.id_tipo_victima_moral = vt.id_tipo_victima_moral,
            vi.id_sexo = vt.id_sexo,
            vi.id_genero = vt.id_genero,
            vi.id_nacionalidad = vt.id_nacionalidad,
            vi.id_pertenece_poblacion_indigena = vt.id_pertenece_poblacion_indigena,
            vi.id_presenta_discapacidad = vt.id_presenta_discapacidad,
            vi.fecha_nacimiento = vt.fecha_nacimiento,
            vi.edad = vt.edad,
            vi.id_carga = @IdCargaActualizacion
        FROM victima vi
        INNER JOIN victimas_actuales va
            ON va.id_victima = vi.id_victima
           AND va.rn = 1
        INNER JOIN victimas_tmp vt
            ON vt.id_ci = va.identificador_carpeta_fiscalia
           AND vt.id_delito = va.identificador_delito_fiscalia
           AND vt.id_vicf = vi.identificador_victima_fiscalia
        WHERE vi.activo = 1
          AND (
                ISNULL(vi.id_tipo_victima, 0) <> ISNULL(vt.id_tipo_victima, 0)
                OR ISNULL(vi.id_tipo_victima_moral, 0) <> ISNULL(vt.id_tipo_victima_moral, 0)
                OR ISNULL(vi.id_sexo, 0) <> ISNULL(vt.id_sexo, 0)
                OR ISNULL(vi.id_genero, 0) <> ISNULL(vt.id_genero, 0)
                OR ISNULL(vi.id_nacionalidad, 0) <> ISNULL(vt.id_nacionalidad, 0)
                OR ISNULL(vi.id_pertenece_poblacion_indigena, 0) <> ISNULL(vt.id_pertenece_poblacion_indigena, 0)
                OR ISNULL(vi.id_presenta_discapacidad, 0) <> ISNULL(vt.id_presenta_discapacidad, 0)
                OR ISNULL(CONVERT(varchar(10), vi.fecha_nacimiento, 120), '') <> ISNULL(CONVERT(varchar(10), vt.fecha_nacimiento, 120), '')
                OR ISNULL(vi.edad, 0) <> ISNULL(vt.edad, 0)
              );
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion
        }, transaction);
    }

    private async Task InsertarVictimasNuevasActualizacionAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1

            UNION

            SELECT @IdCargaActualizacion
        ),
        delitos_vigentes AS (
            SELECT
                d.id_delito,
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                    ORDER BY d.id_carga DESC, d.id_delito DESC
                ) AS rn
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            WHERE d.activo = 1
        )
        INSERT INTO victima (
            id_delito,
            identificador_victima_fiscalia,
            id_tipo_victima,
            id_tipo_victima_moral,
            id_sexo,
            id_genero,
            id_nacionalidad,
            id_pertenece_poblacion_indigena,
            id_presenta_discapacidad,
            fecha_nacimiento,
            edad,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            activo
        )
        SELECT
            dv.id_delito,
            v.id_vicf,
            tv.id_tipo_victima,
            tvm.id_tipo_victima_moral,
            sx.id_sexo,
            gen.id_genero,
            nac.id_nacionalidad,
            pob.id_pertenece_poblacion_indigena,
            disc.id_presenta_discapacidad,
            COALESCE(
                TRY_CONVERT(date, NULLIF(v.fha_nac, ''), 103),
                TRY_CONVERT(date, NULLIF(v.fha_nac, ''))
            ),
            CASE
                WHEN TRY_CONVERT(int, NULLIF(v.edad, '')) = 999 THEN NULL
                ELSE TRY_CONVERT(tinyint, NULLIF(v.edad, ''))
            END,
            @IdUsuarioConfirmacion,
            SYSDATETIME(),
            @IdCargaActualizacion,
            1
        FROM carga_tmp_victima v
        INNER JOIN delitos_vigentes dv
            ON dv.identificador_carpeta_fiscalia = v.id_ci
           AND dv.identificador_delito_fiscalia = v.id_delito
           AND dv.rn = 1
        INNER JOIN catalogo_tipo_victima tv
            ON tv.clave = TRY_CONVERT(tinyint, v.id_tv)
           AND tv.activo = 1
        LEFT JOIN catalogo_tipo_victima_moral tvm
            ON tvm.clave = TRY_CONVERT(tinyint, NULLIF(v.id_tpm, ''))
           AND tvm.activo = 1
        LEFT JOIN catalogo_sexo sx
            ON sx.clave = TRY_CONVERT(tinyint, NULLIF(v.sexo, ''))
           AND sx.activo = 1
        LEFT JOIN catalogo_genero gen
            ON gen.clave = TRY_CONVERT(tinyint, NULLIF(v.genero, ''))
           AND gen.activo = 1
        LEFT JOIN catalogo_nacionalidad nac
            ON TRY_CONVERT(int, nac.clave) = TRY_CONVERT(int, NULLIF(v.nacional, ''))
           AND nac.activo = 1
        LEFT JOIN catalogo_pertenece_poblacion_indigena pob
            ON pob.clave = TRY_CONVERT(tinyint, NULLIF(v.pob, ''))
           AND pob.activo = 1
        LEFT JOIN catalogo_presenta_discapacidad disc
            ON disc.clave = TRY_CONVERT(tinyint, NULLIF(v.disc, ''))
           AND disc.activo = 1
        WHERE v.id_carga = @IdCargaActualizacion
          AND v.activo = 1
          AND NOT EXISTS (
                SELECT 1
                FROM victima vi
                WHERE vi.id_delito = dv.id_delito
                  AND vi.identificador_victima_fiscalia = v.id_vicf
                  AND vi.activo = 1
          );
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion,
            IdUsuarioConfirmacion = idUsuarioConfirmacion
        }, transaction);
    }

    private async Task InsertarHistoricoVictimasEliminadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        victimas_actuales AS (
            SELECT
                v.*,
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia, v.identificador_victima_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, v.id_carga DESC, v.id_victima DESC
                ) AS rn
            FROM victima v
            INNER JOIN delito d
                ON d.id_delito = v.id_delito
               AND d.activo = 1
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = v.id_carga
            WHERE v.activo = 1
        ),
        victimas_eliminadas AS (
            SELECT va.*
            FROM victimas_actuales va
            LEFT JOIN carga_tmp_victima vt
                ON vt.id_ci = va.identificador_carpeta_fiscalia
               AND vt.id_delito = va.identificador_delito_fiscalia
               AND vt.id_vicf = va.identificador_victima_fiscalia
               AND vt.id_carga = @IdCargaActualizacion
               AND vt.activo = 1
            WHERE va.rn = 1
              AND vt.id_vicf IS NULL
        )
        INSERT INTO victima_historico (
            id_victima,
            id_delito,
            identificador_victima_fiscalia,
            id_tipo_victima,
            id_tipo_victima_moral,
            id_sexo,
            id_genero,
            id_nacionalidad,
            id_pertenece_poblacion_indigena,
            id_presenta_discapacidad,
            fecha_nacimiento,
            edad,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            id_usuario_modificacion,
            id_carga_nueva,
            tipo_movimiento,
            fecha_modificacion,
            activo
        )
        SELECT
            id_victima,
            id_delito,
            identificador_victima_fiscalia,
            id_tipo_victima,
            id_tipo_victima_moral,
            id_sexo,
            id_genero,
            id_nacionalidad,
            id_pertenece_poblacion_indigena,
            id_presenta_discapacidad,
            fecha_nacimiento,
            edad,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            @IdUsuarioConfirmacion,
            @IdCargaActualizacion,
            'ELIMINADO',
            SYSDATETIME(),
            activo
        FROM victimas_eliminadas;
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion,
            IdUsuarioConfirmacion = idUsuarioConfirmacion
        }, transaction);
    }

    private async Task DesactivarVictimasEliminadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        victimas_actuales AS (
            SELECT
                v.id_victima,
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                v.identificador_victima_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia, v.identificador_victima_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, v.id_carga DESC, v.id_victima DESC
                ) AS rn
            FROM victima v
            INNER JOIN delito d
                ON d.id_delito = v.id_delito
               AND d.activo = 1
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = v.id_carga
            WHERE v.activo = 1
        )
        UPDATE vi
        SET vi.activo = 0,
            vi.id_carga = @IdCargaActualizacion
        FROM victima vi
        INNER JOIN victimas_actuales va
            ON va.id_victima = vi.id_victima
           AND va.rn = 1
        LEFT JOIN carga_tmp_victima vt
            ON vt.id_ci = va.identificador_carpeta_fiscalia
           AND vt.id_delito = va.identificador_delito_fiscalia
           AND vt.id_vicf = va.identificador_victima_fiscalia
           AND vt.id_carga = @IdCargaActualizacion
           AND vt.activo = 1
        WHERE vi.activo = 1
          AND vt.id_vicf IS NULL;
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion
        }, transaction);
    }

    private async Task ValidarDuplicadosActivosPeriodoAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT
                id_carga,
                id_entidad_federativa,
                mes_corte,
                anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1

            UNION

            SELECT @IdCargaActualizacion
        ),
        duplicados_carpetas AS (
            SELECT
                ci.identificador_carpeta_fiscalia,
                COUNT(1) AS total
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
            GROUP BY
                ci.identificador_carpeta_fiscalia
            HAVING COUNT(1) > 1
        ),
        duplicados_delitos AS (
            SELECT
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                COUNT(1) AS total
            FROM delito d
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            WHERE d.activo = 1
            GROUP BY
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia
            HAVING COUNT(1) > 1
        ),
        duplicados_victimas AS (
            SELECT
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                v.identificador_victima_fiscalia,
                COUNT(1) AS total
            FROM victima v
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = v.id_carga
            INNER JOIN delito d
                ON d.id_delito = v.id_delito
               AND d.activo = 1
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            WHERE v.activo = 1
            GROUP BY
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                v.identificador_victima_fiscalia
            HAVING COUNT(1) > 1
        )
        SELECT
            'carpetas' AS Seccion,
            COUNT(1) AS TotalGruposDuplicados
        FROM duplicados_carpetas

        UNION ALL

        SELECT
            'delitos' AS Seccion,
            COUNT(1) AS TotalGruposDuplicados
        FROM duplicados_delitos

        UNION ALL

        SELECT
            'victimas' AS Seccion,
            COUNT(1) AS TotalGruposDuplicados
        FROM duplicados_victimas;
    ";

        var duplicados = (await connection.QueryAsync<DuplicadoActivoValidacion>(
                sql,
                new
                {
                    IdCargaActualizacion = idCargaActualizacion
                },
                transaction))
            .Where(x => x.TotalGruposDuplicados > 0)
            .ToList();

        if (duplicados.Count == 0)
        {
            return;
        }

        var detalle = string.Join(
            ", ",
            duplicados.Select(x => $"{x.Seccion}: {x.TotalGruposDuplicados}"));

        throw new InvalidOperationException(
            $"La actualización no puede confirmarse porque dejaría registros activos duplicados en el periodo. Detalle: {detalle}.");
    }

    private async Task AplicarActualizacionCompletaAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
    {
        var relojTotal = Stopwatch.StartNew();

        await EjecutarPasoActualizacionAsync(
            "InsertarHistoricoCarpetasModificadas",
            idCargaActualizacion,
            () => InsertarHistoricoCarpetasModificadasAsync(connection, transaction, idCargaActualizacion, idUsuarioConfirmacion));

        await EjecutarPasoActualizacionAsync(
            "ActualizarCarpetasModificadas",
            idCargaActualizacion,
            () => ActualizarCarpetasModificadasAsync(connection, transaction, idCargaActualizacion));

        await EjecutarPasoActualizacionAsync(
            "InsertarCarpetasNuevasActualizacion",
            idCargaActualizacion,
            () => InsertarCarpetasNuevasActualizacionAsync(connection, transaction, idCargaActualizacion, idUsuarioConfirmacion));

        await EjecutarPasoActualizacionAsync(
            "InsertarHistoricoDelitosModificados",
            idCargaActualizacion,
            () => InsertarHistoricoDelitosModificadosAsync(connection, transaction, idCargaActualizacion, idUsuarioConfirmacion));

        await EjecutarPasoActualizacionAsync(
            "ActualizarDelitosModificados",
            idCargaActualizacion,
            () => ActualizarDelitosModificadosAsync(connection, transaction, idCargaActualizacion));

        await EjecutarPasoActualizacionAsync(
            "InsertarDelitosNuevosActualizacion",
            idCargaActualizacion,
            () => InsertarDelitosNuevosActualizacionAsync(connection, transaction, idCargaActualizacion, idUsuarioConfirmacion));

        await EjecutarPasoActualizacionAsync(
            "InsertarHistoricoVictimasModificadas",
            idCargaActualizacion,
            () => InsertarHistoricoVictimasModificadasAsync(connection, transaction, idCargaActualizacion, idUsuarioConfirmacion));

        await EjecutarPasoActualizacionAsync(
            "ActualizarVictimasModificadas",
            idCargaActualizacion,
            () => ActualizarVictimasModificadasAsync(connection, transaction, idCargaActualizacion));

        await EjecutarPasoActualizacionAsync(
            "InsertarVictimasNuevasActualizacion",
            idCargaActualizacion,
            () => InsertarVictimasNuevasActualizacionAsync(connection, transaction, idCargaActualizacion, idUsuarioConfirmacion));

        await EjecutarPasoActualizacionAsync(
            "InsertarHistoricoVictimasEliminadas",
            idCargaActualizacion,
            () => InsertarHistoricoVictimasEliminadasAsync(connection, transaction, idCargaActualizacion, idUsuarioConfirmacion));

        await EjecutarPasoActualizacionAsync(
            "DesactivarVictimasEliminadas",
            idCargaActualizacion,
            () => DesactivarVictimasEliminadasAsync(connection, transaction, idCargaActualizacion));

        await EjecutarPasoActualizacionAsync(
            "InsertarHistoricoDelitosEliminados",
            idCargaActualizacion,
            () => InsertarHistoricoDelitosEliminadosAsync(connection, transaction, idCargaActualizacion, idUsuarioConfirmacion));

        await EjecutarPasoActualizacionAsync(
            "DesactivarDelitosEliminados",
            idCargaActualizacion,
            () => DesactivarDelitosEliminadosAsync(connection, transaction, idCargaActualizacion));

        await EjecutarPasoActualizacionAsync(
            "InsertarHistoricoCarpetasEliminadas",
            idCargaActualizacion,
            () => InsertarHistoricoCarpetasEliminadasAsync(connection, transaction, idCargaActualizacion, idUsuarioConfirmacion));

        await EjecutarPasoActualizacionAsync(
            "DesactivarCarpetasEliminadas",
            idCargaActualizacion,
            () => DesactivarCarpetasEliminadasAsync(connection, transaction, idCargaActualizacion));

        await EjecutarPasoActualizacionAsync(
             "ValidarDuplicadosActivosPeriodo",
             idCargaActualizacion,
             () => ValidarDuplicadosActivosPeriodoAsync(connection, transaction, idCargaActualizacion));

        relojTotal.Stop();

        _logger.LogInformation(
            "PERFORMANCE_ACTUALIZACION_TOTAL idCargaActualizacion={IdCargaActualizacion} tiempoMs={TiempoMs}",
            idCargaActualizacion,
            relojTotal.ElapsedMilliseconds);
    }

    private async Task EjecutarPasoActualizacionAsync(string nombrePaso, long idCargaActualizacion, Func<Task> accion)
    {
        var reloj = Stopwatch.StartNew();

        try
        {
            await accion();

            reloj.Stop();

            _logger.LogInformation(
                "PERFORMANCE_ACTUALIZACION_PASO idCargaActualizacion={IdCargaActualizacion} paso={Paso} tiempoMs={TiempoMs}",
                idCargaActualizacion,
                nombrePaso,
                reloj.ElapsedMilliseconds);
        }
        catch
        {
            reloj.Stop();

            _logger.LogError(
                "PERFORMANCE_ACTUALIZACION_ERROR idCargaActualizacion={IdCargaActualizacion} paso={Paso} tiempoMs={TiempoMs}",
                idCargaActualizacion,
                nombrePaso,
                reloj.ElapsedMilliseconds);

            throw;
        }
    }

    private async Task ConfirmarActualizacionFinalAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, int idUsuarioConfirmacion)
    {
        var sql = @"
        UPDATE carga
        SET estado = 'CONFIRMADO_ACTUALIZACION',
            fecha_confirmacion = SYSDATETIME(),
            id_usuario_confirmacion = @IdUsuarioConfirmacion,
            mensaje_error = NULL
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_carpeta
        SET estado = 'PROCESADO',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_delito
        SET estado = 'PROCESADO',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_victima
        SET estado = 'PROCESADO',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;
    ";


        await connection.ExecuteAsync(
            sql,
            new
            {
                IdCarga = idCarga,
                IdUsuarioConfirmacion = idUsuarioConfirmacion
            },
            transaction);
    }

}