using SIIID2.Api.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SIIID2.Api.Repositories;

// Coordinación y dependencias compartidas; las operaciones están separadas en archivos parciales.
public partial class ActualizacionRepository : IActualizacionRepository
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
        public int IdUsuarioCarga { get; set; }
    }

    private class DuplicadoActivoValidacion
    {
        public string Seccion { get; set; } = string.Empty;

        public int TotalGruposDuplicados { get; set; }
    }

    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly BanciCruceService _cruce;
    private readonly ILogger<ActualizacionRepository> _logger;

    public ActualizacionRepository(BanciCruceService cruce, IDbConnectionFactory dbConnectionFactory, ILogger<ActualizacionRepository> logger)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _cruce = cruce;
        _logger = logger;
    }

    public async Task<ConfirmarCargaResponse> ConfirmarActualizacionAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion)
    {
        using var connection =
            (SqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync();

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
                    Mensaje =
                        "No se encontro una actualizacion valida para continuar."
                };
            }

            if (!string.Equals(
                    carga.Estado,
                    "VALIDADO_PENDIENTE_ACTUALIZACION",
                    StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = carga.Estado,
                    Mensaje =
                        "La actualizacion no se encuentra en estado VALIDADO_PENDIENTE_ACTUALIZACION."
                };
            }

            if (carga.IdUsuarioCarga != idUsuarioConfirmacion)
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = carga.Estado,
                    Mensaje =
                        "Solo el usuario que realizo la actualizacion puede aceptar o rechazar esta validacion."
                };
            }

            if (carga.FechaExpiracion.HasValue &&
                carga.FechaExpiracion.Value < DateTime.Now)
            {
                await ActualizarActualizacionExpiradaAsync(
                    connection,
                    transaction,
                    carga.IdCarga);

                await CargaAuditoriaSql.RegistrarCambioEstadoAsync(
                    connection,
                    transaction,
                    carga.IdCarga,
                    estadoAnterior: carga.Estado,
                    estadoNuevo: "EXPIRADO_ACTUALIZACION",
                    idUsuario: null,
                    comentario:
                        "La actualizacion expiro antes de que el usuario tomara una decision.");

                await transaction.CommitAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = "EXPIRADO_ACTUALIZACION",
                    Mensaje =
                        "La actualizacion ya expiro. Debe validar nuevamente los archivos."
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
                    Mensaje =
                        "El usuario no tiene habilitada la modificacion de informacion."
                };
            }

            if (!carga.EsSuperUsuario &&
                carga.IdEntidadFederativaUsuario.HasValue &&
                carga.IdEntidadFederativaCarga.HasValue &&
                carga.IdEntidadFederativaUsuario.Value !=
                carga.IdEntidadFederativaCarga.Value)
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = carga.Estado,
                    Mensaje =
                        "El usuario no puede procesar actualizaciones de otra entidad federativa."
                };
            }

            if (!aceptar)
            {
                await RechazarActualizacionAsync(
                    connection,
                    transaction,
                    carga.IdCarga,
                    idUsuarioConfirmacion);

                await CargaAuditoriaSql.RegistrarCambioEstadoAsync(
                    connection,
                    transaction,
                    carga.IdCarga,
                    estadoAnterior: carga.Estado,
                    estadoNuevo: "RECHAZADO_VALIDACION_ACTUALIZACION",
                    idUsuario: idUsuarioConfirmacion,
                    comentario:
                        "La actualizacion fue rechazada por el usuario despues de revisar el acuse previo, las diferencias y las validaciones.");

                await transaction.CommitAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = true,
                    CodigoReferencia = codigoReferencia,
                    Estado = "RECHAZADO_VALIDACION_ACTUALIZACION",
                    Mensaje =
                        "La actualizacion fue rechazada correctamente."
                };
            }

            await CargaAuditoriaSql.MarcarAdvertenciasAceptadasAsync(
                connection,
                transaction,
                carga.IdCarga,
                idUsuarioConfirmacion);

            if (!carga.EsSuperUsuario)
            {
                await EnviarActualizacionAprobacionAsync(
                    connection,
                    transaction,
                    carga.IdCarga);

                await CargaAuditoriaSql.RegistrarCambioEstadoAsync(
                    connection,
                    transaction,
                    carga.IdCarga,
                    estadoAnterior: carga.Estado,
                    estadoNuevo: "PENDIENTE_APROBACION",
                    idUsuario: idUsuarioConfirmacion,
                    comentario:
                        "El enlace estatal acepto la actualizacion y la envio a revision administrativa.");

                await transaction.CommitAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = true,
                    CodigoReferencia = codigoReferencia,
                    Estado = "PENDIENTE_APROBACION",
                    Mensaje =
                        "La actualizacion fue enviada correctamente a revision administrativa."
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

            await CargaAuditoriaSql.RegistrarCambioEstadoAsync(
                connection,
                transaction,
                carga.IdCarga,
                estadoAnterior: carga.Estado,
                estadoNuevo: "CONFIRMADO_ACTUALIZACION",
                idUsuario: idUsuarioConfirmacion,
                comentario:
                    "Actualizacion realizada y confirmada directamente por un superusuario.");

            await transaction.CommitAsync();

            return new ConfirmarCargaResponse
            {
                EsValido = true,
                CodigoReferencia = codigoReferencia,
                Estado = "CONFIRMADO_ACTUALIZACION",
                Mensaje =
                    "La actualizacion fue confirmada correctamente."
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
            c.id_usuario_carga AS IdUsuarioCarga,
            c.codigo_referencia AS CodigoReferencia,
            c.estado AS Estado,
            c.fecha_expiracion AS FechaExpiracion,
            c.id_entidad_federativa AS IdEntidadFederativaCarga,
            u.id_entidad_federativa AS IdEntidadFederativaUsuario,
            CASE WHEN r.rol = 'SUPER_USUARIO' THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS EsSuperUsuario,
            ISNULL(h.habilita_modificacion, 0) AS HabilitaModificacion
            FROM carga c WITH (UPDLOCK, HOLDLOCK)
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

    public async Task<ConfirmarCargaResponse> AprobarActualizacionPendienteAsync(string codigoReferencia, int idUsuarioAprobacion)
    {
        using var connection =
            (SqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerActualizacionConfirmacionAsync(
                connection,
                transaction,
                codigoReferencia,
                idUsuarioAprobacion);

            if (carga == null)
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = "NO_ENCONTRADA",
                    Mensaje =
                        "No se encontro la actualizacion pendiente."
                };
            }

            if (!carga.EsSuperUsuario)
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = carga.Estado,
                    Mensaje =
                        "Solo un superusuario puede aprobar actualizaciones."
                };
            }

            if (!string.Equals(
                    carga.Estado,
                    "PENDIENTE_APROBACION",
                    StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = carga.Estado,
                    Mensaje =
                        "La actualizacion ya no se encuentra pendiente de aprobacion."
                };
            }

            /*
                Los registros nuevos y los movimientos historicos
                conservan como responsable al usuario que envio
                la actualizacion.
            */
            await AplicarActualizacionCompletaAsync(
                connection,
                transaction,
                carga.IdCarga,
                carga.IdUsuarioCarga);

            /*
                La aprobacion final queda registrada a nombre
                del superusuario administrador.
            */
            await ConfirmarActualizacionFinalAsync(
                connection,
                transaction,
                carga.IdCarga,
                idUsuarioAprobacion);

            await CargaAuditoriaSql.RegistrarCambioEstadoAsync(
                connection,
                transaction,
                carga.IdCarga,
                estadoAnterior: "PENDIENTE_APROBACION",
                estadoNuevo: "CONFIRMADO_ACTUALIZACION",
                idUsuario: idUsuarioAprobacion,
                comentario:
                    "La actualizacion fue aprobada por el superusuario.");

            await transaction.CommitAsync();

            return new ConfirmarCargaResponse
            {
                EsValido = true,
                CodigoReferencia = codigoReferencia,
                Estado = "CONFIRMADO_ACTUALIZACION",
                Mensaje =
                    "La actualizacion fue aprobada y aplicada correctamente."
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ConfirmarCargaResponse> RechazarActualizacionPendienteAsync(string codigoReferencia, int idUsuarioRechazo, string motivo)
    {
        using var connection =
            (SqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerActualizacionConfirmacionAsync(
                connection,
                transaction,
                codigoReferencia,
                idUsuarioRechazo);

            if (carga == null)
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = "NO_ENCONTRADA",
                    Mensaje =
                        "No se encontro la actualizacion pendiente."
                };
            }

            if (!carga.EsSuperUsuario)
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = carga.Estado,
                    Mensaje =
                        "Solo un superusuario puede rechazar actualizaciones."
                };
            }

            if (!string.Equals(
                    carga.Estado,
                    "PENDIENTE_APROBACION",
                    StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = carga.Estado,
                    Mensaje =
                        "La actualizacion ya no se encuentra pendiente de aprobacion."
                };
            }

            await RechazarActualizacionAdministracionAsync(
                connection,
                transaction,
                carga.IdCarga,
                idUsuarioRechazo,
                motivo);

            await CargaAuditoriaSql.RegistrarCambioEstadoAsync(
                connection,
                transaction,
                carga.IdCarga,
                estadoAnterior: "PENDIENTE_APROBACION",
                estadoNuevo: "RECHAZADO_ADMIN",
                idUsuario: idUsuarioRechazo,
                comentario: motivo);

            await transaction.CommitAsync();

            return new ConfirmarCargaResponse
            {
                EsValido = true,
                CodigoReferencia = codigoReferencia,
                Estado = "RECHAZADO_ADMIN",
                Mensaje =
                    "La actualizacion fue rechazada por el administrador."
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task RechazarActualizacionAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, int idUsuarioConfirmacion)
    {
        var sql = @"
        UPDATE carga
        SET estado = 'RECHAZADO_VALIDACION_ACTUALIZACION',
            fecha_confirmacion = SYSDATETIME(),
            id_usuario_confirmacion = @IdUsuarioConfirmacion,
            mensaje_error = 'La actualización fue rechazada por el usuario después de revisar advertencias de validación.'
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_carpeta
        SET estado = 'RECHAZADO_VALIDACION_ACTUALIZACION',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_delito
        SET estado = 'RECHAZADO_VALIDACION_ACTUALIZACION',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_victima
        SET estado = 'RECHAZADO_VALIDACION_ACTUALIZACION',
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

    private static async Task RechazarActualizacionAdministracionAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, int idUsuarioRechazo, string motivo)
    {
        const string sql = @"
        UPDATE dbo.carga
        SET estado = 'RECHAZADO_ADMIN',
            fecha_confirmacion = SYSDATETIME(),
            fecha_expiracion = NULL,
            id_usuario_confirmacion = @IdUsuarioRechazo,
            mensaje_error = @Motivo,
            rechazo_visto = 0,
            fecha_rechazo_visto = NULL
        WHERE id_carga = @IdCarga;

        UPDATE dbo.carga_tmp_carpeta
        SET estado = 'RECHAZADO_ADMIN',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE dbo.carga_tmp_delito
        SET estado = 'RECHAZADO_ADMIN',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE dbo.carga_tmp_victima
        SET estado = 'RECHAZADO_ADMIN',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;
    ";

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdCarga = idCarga,
                IdUsuarioRechazo = idUsuarioRechazo,
                Motivo = motivo
            },
            transaction);
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

    // Orden intencional: guardar versiones previas antes de actualizar; desactivar hijos antes de padres.
    // Todas las etapas comparten la transacción recibida; no confirmar por separado dentro de un parcial.
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
        await _cruce.ConfirmarAsync(connection, transaction, idCarga);
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

    private static async Task EnviarActualizacionAprobacionAsync(SqlConnection connection, SqlTransaction transaction, long idCarga)
    {
        const string sql = @"
        UPDATE dbo.carga
        SET estado = 'PENDIENTE_APROBACION',
            fecha_expiracion = NULL,
            mensaje_error = NULL
        WHERE id_carga = @IdCarga;

        UPDATE dbo.carga_tmp_carpeta
        SET estado = 'PENDIENTE_APROBACION',
            fecha_procesamiento = NULL
        WHERE id_carga = @IdCarga;

        UPDATE dbo.carga_tmp_delito
        SET estado = 'PENDIENTE_APROBACION',
            fecha_procesamiento = NULL
        WHERE id_carga = @IdCarga;

        UPDATE dbo.carga_tmp_victima
        SET estado = 'PENDIENTE_APROBACION',
            fecha_procesamiento = NULL
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

}