using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class FederalCargaRepository : IFederalCargaRepository
{
    private class FederalCargaConfirmacionInfo
    {
        public long IdFederalCarga { get; set; }
        public string CodigoReferencia { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public DateTime? FechaExpiracion { get; set; }
        public bool EsSuperUsuario { get; set; }
        public bool HabilitaCarga { get; set; }
        public int IdUsuarioCarga { get; set; }
    }

    private readonly IDbConnectionFactory _dbConnectionFactory;

    public FederalCargaRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<UsuarioCargaInfo?> ObtenerUsuarioCargaAsync(int idUsuario)
    {
        const string sql = """
            SELECT
                u.id_usuario AS IdUsuario,
                u.id_entidad_federativa AS IdEntidadFederativa,
                r.rol AS Rol,
                CONVERT(bit, ISNULL(um.habilita_carga, 0)) AS HabilitaCarga,
                CONVERT(bit, ISNULL(um.habilita_modificacion, 0)) AS HabilitaModificacion
            FROM dbo.usuario u
            INNER JOIN dbo.roles r
                ON r.id_rol = u.id_rol
               AND r.activo = 1
            INNER JOIN dbo.catalogo_modulo m
                ON m.clave = N'FEDERAL'
               AND m.activo = 1
            INNER JOIN dbo.usuario_modulo um
                ON um.id_usuario = u.id_usuario
               AND um.id_modulo = m.id_modulo
               AND um.habilitado = 1
               AND um.activo = 1
            WHERE u.id_usuario = @IdUsuario
              AND u.activo = 1;
            """;

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QueryFirstOrDefaultAsync<UsuarioCargaInfo>(sql, new { IdUsuario = idUsuario });
    }

    public async Task<bool> ExisteCargaConfirmadaAsync(int mesCorte, int anioCorte)
    {
        const string sql = """
            SELECT COUNT(1)
            FROM dbo.federal_carga
            WHERE id_entidad_federativa IS NULL
              AND mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND tipo_carga = N'CARGA_INICIAL'
              AND estado = N'CONFIRMADO'
              AND activo = 1;
            """;

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.ExecuteScalarAsync<int>(sql, new { MesCorte = mesCorte, AnioCorte = anioCorte }) > 0;
    }

    public async Task<CargaPendienteInfo?> ObtenerCodigoCargaPendienteAsync(int mesCorte, int anioCorte)
    {
        const string sql = """
            SELECT TOP 1
                codigo_referencia AS CodigoReferencia,
                estado AS Estado
            FROM dbo.federal_carga
            WHERE id_entidad_federativa IS NULL
              AND mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND tipo_carga = N'CARGA_INICIAL'
              AND estado IN (N'VALIDADO_PENDIENTE', N'PENDIENTE_APROBACION')
              AND activo = 1
            ORDER BY fecha_validacion DESC;
            """;

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QueryFirstOrDefaultAsync<CargaPendienteInfo>(sql, new { MesCorte = mesCorte, AnioCorte = anioCorte });
    }

    public async Task<long> GuardarIntentoCargaAsync(int idUsuarioCarga, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError, List<CargaValidacionError> advertencias, List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var idFederalCarga = await CrearCargaAsync(connection, transaction, idUsuarioCarga, codigoReferencia, mesCorte, anioCorte, totalCarpetas, totalDelitos, totalVictimas, estado, mensajeError);

            await GuardarTmpCarpetasAsync(connection, transaction, idFederalCarga, filasCarpetas);
            await GuardarTmpDelitosAsync(connection, transaction, idFederalCarga, filasDelitos);
            await GuardarTmpVictimasAsync(connection, transaction, idFederalCarga, filasVictimas);
            await FederalCargaAuditoriaSql.GuardarAdvertenciasAsync(connection, transaction, idFederalCarga, advertencias);
            await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(connection, transaction, idFederalCarga, null, estado, idUsuarioCarga, estado == "VALIDADO_PENDIENTE" ? "Carga inicial federal validada y pendiente de decisión del usuario." : "Intento de carga inicial federal registrado con errores de validación.");

            await transaction.CommitAsync();
            return idFederalCarga;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ConfirmarCargaResponse> ConfirmarCargaAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerCargaConfirmacionAsync(connection, transaction, codigoReferencia, idUsuarioConfirmacion);

            if (carga == null)
            {
                await transaction.RollbackAsync();
                return Respuesta(false, codigoReferencia, "NO_ENCONTRADA", "No se encontró una carga federal válida para continuar.");
            }

            if (!string.Equals(carga.Estado, "VALIDADO_PENDIENTE", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();
                return Respuesta(false, codigoReferencia, carga.Estado, "La carga federal no se encuentra en estado VALIDADO_PENDIENTE.");
            }

            if (carga.IdUsuarioCarga != idUsuarioConfirmacion)
            {
                await transaction.RollbackAsync();
                return Respuesta(false, codigoReferencia, carga.Estado, "Solo el usuario que realizó la carga federal puede aceptar o rechazar esta validación.");
            }

            if (carga.FechaExpiracion.HasValue && carga.FechaExpiracion.Value < DateTime.Now)
            {
                await ActualizarCargaExpiradaAsync(connection, transaction, carga.IdFederalCarga);
                await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(connection, transaction, carga.IdFederalCarga, carga.Estado, "EXPIRADO", null, "La carga federal expiró antes de que el usuario tomara una decisión.");
                await transaction.CommitAsync();
                return Respuesta(false, codigoReferencia, "EXPIRADO", "La carga federal ya expiró. Debe validar nuevamente los archivos.");
            }

            if (!carga.HabilitaCarga)
            {
                await transaction.RollbackAsync();
                return Respuesta(false, codigoReferencia, carga.Estado, "El usuario no tiene habilitada la carga de información federal.");
            }

            if (!aceptar)
            {
                await RechazarCargaAsync(connection, transaction, carga.IdFederalCarga, idUsuarioConfirmacion);
                await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(connection, transaction, carga.IdFederalCarga, carga.Estado, "RECHAZADO_VALIDACION", idUsuarioConfirmacion, "La carga federal fue rechazada por el usuario después de revisar el acuse previo y las validaciones.");
                await transaction.CommitAsync();
                return Respuesta(true, codigoReferencia, "RECHAZADO_VALIDACION", "La carga federal fue rechazada correctamente.");
            }

            await FederalCargaAuditoriaSql.MarcarAdvertenciasAceptadasAsync(connection, transaction, carga.IdFederalCarga, idUsuarioConfirmacion);

            if (!carga.EsSuperUsuario)
            {
                await EnviarCargaAprobacionAsync(connection, transaction, carga.IdFederalCarga);
                await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(connection, transaction, carga.IdFederalCarga, carga.Estado, "PENDIENTE_APROBACION", idUsuarioConfirmacion, "El enlace federal aceptó la validación y envió la carga a revisión administrativa.");
                await transaction.CommitAsync();
                return Respuesta(true, codigoReferencia, "PENDIENTE_APROBACION", "La carga federal fue enviada correctamente a revisión administrativa.");
            }

            await InsertarCarpetasFinalesAsync(connection, transaction, carga.IdFederalCarga, idUsuarioConfirmacion);
            await InsertarDelitosFinalesAsync(connection, transaction, carga.IdFederalCarga, idUsuarioConfirmacion);
            await InsertarVictimasFinalesAsync(connection, transaction, carga.IdFederalCarga, idUsuarioConfirmacion);
            await ConfirmarCargaFinalAsync(connection, transaction, carga.IdFederalCarga, idUsuarioConfirmacion);
            await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(connection, transaction, carga.IdFederalCarga, carga.Estado, "CONFIRMADO", idUsuarioConfirmacion, "Carga federal realizada y confirmada directamente por un superusuario.");

            await transaction.CommitAsync();
            return Respuesta(true, codigoReferencia, "CONFIRMADO", "La carga federal fue confirmada correctamente.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<CargaPendienteAdministracionItem>> ObtenerPendientesAdministracionAsync()
    {
        const string sql = """
        SELECT
            c.id_federal_carga AS IdCarga,
            c.codigo_referencia AS CodigoReferencia,
            c.tipo_carga AS TipoCarga,
            CAST(NULL AS int) AS IdEntidadFederativa,
            N'Federal' AS EntidadFederativa,
            c.mes_corte AS MesCorte,
            c.anio_corte AS AnioCorte,
            c.fecha_validacion AS FechaValidacion,
            c.id_usuario_carga AS IdUsuarioCarga,
            u.usuario AS UsuarioCarga,
            LTRIM(RTRIM(CONCAT(
                u.nombre,
                N' ',
                u.primer_apellido,
                CASE
                    WHEN NULLIF(u.segundo_apellido, N'') IS NULL THEN N''
                    ELSE CONCAT(N' ', u.segundo_apellido)
                END
            ))) AS NombreUsuarioCarga,
            c.total_carpetas_investigacion AS TotalCarpetas,
            c.total_delitos AS TotalDelitos,
            c.total_victimas AS TotalVictimas,
            (
                SELECT COUNT(1)
                FROM dbo.federal_carga_advertencia ca
                WHERE ca.id_federal_carga = c.id_federal_carga
                  AND ca.activo = 1
            ) AS TotalAdvertencias
        FROM dbo.federal_carga c
        INNER JOIN dbo.usuario u
            ON u.id_usuario = c.id_usuario_carga
        WHERE c.tipo_carga = N'CARGA_INICIAL'
          AND c.estado = N'PENDIENTE_APROBACION'
          AND c.activo = 1
        ORDER BY
            c.fecha_validacion ASC,
            c.id_federal_carga ASC;
        """;

        using var connection = _dbConnectionFactory.CrearConexion();
        return (await connection.QueryAsync<CargaPendienteAdministracionItem>(sql)).ToList();
    }

    public async Task<CargaPendienteAdministracionDetalle?> ObtenerDetalleAdministracionAsync(string codigoReferencia)
    {
        const string sqlCarga = """
        SELECT TOP (1)
            c.id_federal_carga AS IdCarga,
            c.codigo_referencia AS CodigoReferencia,
            c.tipo_carga AS TipoCarga,
            CAST(NULL AS int) AS IdEntidadFederativa,
            N'Federal' AS EntidadFederativa,
            c.mes_corte AS MesCorte,
            c.anio_corte AS AnioCorte,
            c.fecha_validacion AS FechaValidacion,
            c.id_usuario_carga AS IdUsuarioCarga,
            u.usuario AS UsuarioCarga,
            LTRIM(RTRIM(CONCAT(
                u.nombre,
                N' ',
                u.primer_apellido,
                CASE
                    WHEN NULLIF(u.segundo_apellido, N'') IS NULL THEN N''
                    ELSE CONCAT(N' ', u.segundo_apellido)
                END
            ))) AS NombreUsuarioCarga,
            c.total_carpetas_investigacion AS TotalCarpetas,
            c.total_delitos AS TotalDelitos,
            c.total_victimas AS TotalVictimas,
            (
                SELECT COUNT(1)
                FROM dbo.federal_carga_advertencia ca
                WHERE ca.id_federal_carga = c.id_federal_carga
                  AND ca.activo = 1
            ) AS TotalAdvertencias
        FROM dbo.federal_carga c
        INNER JOIN dbo.usuario u
            ON u.id_usuario = c.id_usuario_carga
        WHERE c.codigo_referencia = @CodigoReferencia
          AND c.tipo_carga = N'CARGA_INICIAL'
          AND c.estado = N'PENDIENTE_APROBACION'
          AND c.activo = 1;
        """;

        const string sqlAdvertencias = """
        SELECT
            ca.id_federal_carga_advertencia AS IdCargaAdvertencia,
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
        FROM dbo.federal_carga_advertencia ca
        WHERE ca.id_federal_carga = @IdCarga
          AND ca.activo = 1
        ORDER BY
            ca.archivo,
            ca.numero_fila,
            ca.id_federal_carga_advertencia;
        """;

        using var connection = _dbConnectionFactory.CrearConexion();

        var carga = await connection.QueryFirstOrDefaultAsync<CargaPendienteAdministracionDetalle>(sqlCarga, new { CodigoReferencia = codigoReferencia });

        if (carga == null) return null;

        carga.Advertencias = (await connection.QueryAsync<CargaAdvertenciaAdministracionItem>(sqlAdvertencias, new { IdCarga = carga.IdCarga })).ToList();

        return carga;
    }

    public async Task<ConfirmarCargaResponse> AprobarCargaPendienteAsync(string codigoReferencia, int idUsuarioAprobacion)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerCargaConfirmacionAsync(connection, transaction, codigoReferencia, idUsuarioAprobacion);

            if (carga == null)
            {
                await transaction.RollbackAsync();
                return Respuesta(false, codigoReferencia, "NO_ENCONTRADA", "No se encontró la carga federal pendiente de aprobación.");
            }

            if (!carga.EsSuperUsuario)
            {
                await transaction.RollbackAsync();
                return Respuesta(false, codigoReferencia, carga.Estado, "Solo un superusuario puede aprobar cargas federales.");
            }

            if (!string.Equals(carga.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();
                return Respuesta(false, codigoReferencia, carga.Estado, "La carga federal ya no se encuentra pendiente de aprobación.");
            }

            await InsertarCarpetasFinalesAsync(connection, transaction, carga.IdFederalCarga, carga.IdUsuarioCarga);
            await InsertarDelitosFinalesAsync(connection, transaction, carga.IdFederalCarga, carga.IdUsuarioCarga);
            await InsertarVictimasFinalesAsync(connection, transaction, carga.IdFederalCarga, carga.IdUsuarioCarga);
            await ConfirmarCargaFinalAsync(connection, transaction, carga.IdFederalCarga, idUsuarioAprobacion);

            await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(
                connection,
                transaction,
                carga.IdFederalCarga,
                "PENDIENTE_APROBACION",
                "CONFIRMADO",
                idUsuarioAprobacion,
                "La carga federal fue aprobada por el superusuario.");

            await transaction.CommitAsync();

            return Respuesta(true, codigoReferencia, "CONFIRMADO", "La carga federal fue aprobada y confirmada correctamente.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ConfirmarCargaResponse> RechazarCargaPendienteAsync(string codigoReferencia, int idUsuarioRechazo, string motivo)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerCargaConfirmacionAsync(connection, transaction, codigoReferencia, idUsuarioRechazo);

            if (carga == null)
            {
                await transaction.RollbackAsync();
                return Respuesta(false, codigoReferencia, "NO_ENCONTRADA", "No se encontró la carga federal pendiente de aprobación.");
            }

            if (!carga.EsSuperUsuario)
            {
                await transaction.RollbackAsync();
                return Respuesta(false, codigoReferencia, carga.Estado, "Solo un superusuario puede rechazar cargas federales.");
            }

            if (!string.Equals(carga.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();
                return Respuesta(false, codigoReferencia, carga.Estado, "La carga federal ya no se encuentra pendiente de aprobación.");
            }

            var motivoLimpio = motivo?.Trim() ?? string.Empty;

            if (motivoLimpio.Length < 5)
            {
                await transaction.RollbackAsync();
                return Respuesta(false, codigoReferencia, "MOTIVO_INVALIDO", "Debe capturar un motivo de rechazo de al menos 5 caracteres.");
            }

            await RechazarCargaAdministracionAsync(connection, transaction, carga.IdFederalCarga, idUsuarioRechazo, motivoLimpio);

            await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(
                connection,
                transaction,
                carga.IdFederalCarga,
                "PENDIENTE_APROBACION",
                "RECHAZADO_ADMIN",
                idUsuarioRechazo,
                motivoLimpio);

            await transaction.CommitAsync();

            return Respuesta(true, codigoReferencia, "RECHAZADO_ADMIN", "La carga federal fue rechazada por el administrador.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static ConfirmarCargaResponse Respuesta(bool esValido, string codigoReferencia, string estado, string mensaje)
    {
        return new ConfirmarCargaResponse
        {
            EsValido = esValido,
            CodigoReferencia = codigoReferencia,
            Estado = estado,
            Mensaje = mensaje
        };
    }

    private static async Task<long> CrearCargaAsync(SqlConnection connection, SqlTransaction transaction, int idUsuarioCarga, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError)
    {
        const string sql = """
            INSERT INTO dbo.federal_carga
            (
                id_usuario_carga,
                id_entidad_federativa,
                codigo_referencia,
                tipo_carga,
                mes_corte,
                anio_corte,
                total_carpetas_investigacion,
                total_delitos,
                total_victimas,
                estado,
                fecha_validacion,
                fecha_expiracion,
                mensaje_error,
                activo
            )
            OUTPUT INSERTED.id_federal_carga
            VALUES
            (
                @IdUsuarioCarga,
                NULL,
                @CodigoReferencia,
                N'CARGA_INICIAL',
                @MesCorte,
                @AnioCorte,
                @TotalCarpetas,
                @TotalDelitos,
                @TotalVictimas,
                @Estado,
                SYSDATETIME(),
                DATEADD(HOUR, 48, SYSDATETIME()),
                @MensajeError,
                1
            );
            """;

        return await connection.ExecuteScalarAsync<long>(sql, new
        {
            IdUsuarioCarga = idUsuarioCarga,
            CodigoReferencia = codigoReferencia,
            MesCorte = mesCorte,
            AnioCorte = anioCorte,
            TotalCarpetas = totalCarpetas,
            TotalDelitos = totalDelitos,
            TotalVictimas = totalVictimas,
            Estado = estado,
            MensajeError = mensajeError
        }, transaction);
    }

    private static async Task GuardarTmpCarpetasAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, List<ArchivoFila> filasCarpetas)
    {
        var tabla = new DataTable();

        tabla.Columns.Add("id_federal_carga", typeof(long));
        tabla.Columns.Add("numero_fila", typeof(int));
        tabla.Columns.Add("id_ci", typeof(string));
        tabla.Columns.Add("ntra_ci", typeof(string));
        tabla.Columns.Add("fha_de_ini", typeof(string));
        tabla.Columns.Add("hra_de_ini", typeof(string));
        tabla.Columns.Add("rmen_de_hchos", typeof(string));
        tabla.Columns.Add("estado", typeof(string));
        tabla.Columns.Add("activo", typeof(bool));

        foreach (var fila in filasCarpetas)
        {
            tabla.Rows.Add(idFederalCarga, fila.NumeroFila, ValorTextoStaging(ObtenerValor(fila, "id_ci")), ValorTextoStaging(ObtenerValor(fila, "ntra_ci")), ValorTextoStaging(ObtenerValor(fila, "fha_de_ini")), ValorTextoStaging(ObtenerValor(fila, "hra_de_ini")), ValorTextoStaging(ObtenerValor(fila, "rmen_de_hchos")), "PENDIENTE", true);
        }

        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction) { DestinationTableName = "dbo.federal_carga_tmp_carpeta" };

        foreach (DataColumn columna in tabla.Columns) bulkCopy.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);

        await bulkCopy.WriteToServerAsync(tabla);
    }

    private static async Task GuardarTmpDelitosAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, List<ArchivoFila> filasDelitos)
    {
        var tabla = new DataTable();

        tabla.Columns.Add("id_federal_carga", typeof(long));
        tabla.Columns.Add("numero_fila", typeof(int));
        tabla.Columns.Add("id_ci", typeof(string));
        tabla.Columns.Add("id_delito", typeof(string));
        tabla.Columns.Add("dto", typeof(string));
        tabla.Columns.Add("moda_dto", typeof(string));
        tabla.Columns.Add("forma_acc", typeof(string));
        tabla.Columns.Add("fha_de_hchos", typeof(string));
        tabla.Columns.Add("hra_de_hchos", typeof(string));
        tabla.Columns.Add("emto_com_dto", typeof(string));
        tabla.Columns.Add("grdo_cons", typeof(string));
        tabla.Columns.Add("clasf_de_dto", typeof(string));
        tabla.Columns.Add("id_ent_hchos", typeof(string));
        tabla.Columns.Add("id_mun_hchos", typeof(string));
        tabla.Columns.Add("id_loc_hchos", typeof(string));
        tabla.Columns.Add("nom_loc_hchos", typeof(string));
        tabla.Columns.Add("id_col_hchos", typeof(string));
        tabla.Columns.Add("nom_col_hchos", typeof(string));
        tabla.Columns.Add("cp", typeof(string));
        tabla.Columns.Add("coord_x", typeof(string));
        tabla.Columns.Add("coord_y", typeof(string));
        tabla.Columns.Add("dom_hchos", typeof(string));
        tabla.Columns.Add("estado", typeof(string));
        tabla.Columns.Add("activo", typeof(bool));

        foreach (var fila in filasDelitos)
        {
            tabla.Rows.Add(idFederalCarga, fila.NumeroFila, ValorTextoStaging(ObtenerValor(fila, "id_ci")), ValorTextoStaging(ObtenerValor(fila, "id_delito")), ValorTextoStaging(ObtenerValor(fila, "dto")), ValorTextoStaging(ObtenerValor(fila, "moda_dto")), ValorTextoStaging(ObtenerValor(fila, "forma_acc")), ValorTextoStaging(ObtenerValor(fila, "fha_de_hchos")), ValorTextoStaging(ObtenerValor(fila, "hra_de_hchos")), ValorTextoStaging(ObtenerValor(fila, "emto_com_dto")), ValorTextoStaging(ObtenerValor(fila, "grdo_cons")), ValorTextoStaging(ObtenerValor(fila, "clasf_de_dto")), ValorTextoStaging(ObtenerValor(fila, "id_ent_hchos")), ValorTextoStaging(ObtenerValor(fila, "id_mun_hchos")), ValorTextoStaging(ObtenerValor(fila, "id_loc_hchos")), ValorTextoStaging(ObtenerValor(fila, "nom_loc_hchos")), ValorTextoStaging(ObtenerValor(fila, "id_col_hchos")), ValorTextoStaging(ObtenerValor(fila, "nom_col_hchos")), ValorTextoStaging(ObtenerValor(fila, "cp")), ValorTextoStaging(ObtenerValor(fila, "coord_x")), ValorTextoStaging(ObtenerValor(fila, "coord_y")), ValorTextoStaging(ObtenerValor(fila, "dom_hchos")), "PENDIENTE", true);
        }

        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction) { DestinationTableName = "dbo.federal_carga_tmp_delito" };

        foreach (DataColumn columna in tabla.Columns) bulkCopy.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);

        await bulkCopy.WriteToServerAsync(tabla);
    }

    private static async Task GuardarTmpVictimasAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, List<ArchivoFila> filasVictimas)
    {
        var tabla = new DataTable();

        tabla.Columns.Add("id_federal_carga", typeof(long));
        tabla.Columns.Add("numero_fila", typeof(int));
        tabla.Columns.Add("id_ci", typeof(string));
        tabla.Columns.Add("id_delito", typeof(string));
        tabla.Columns.Add("id_vicf", typeof(string));
        tabla.Columns.Add("id_tv", typeof(string));
        tabla.Columns.Add("id_tpm", typeof(string));
        tabla.Columns.Add("sexo", typeof(string));
        tabla.Columns.Add("genero", typeof(string));
        tabla.Columns.Add("pob", typeof(string));
        tabla.Columns.Add("disc", typeof(string));
        tabla.Columns.Add("fha_nac", typeof(string));
        tabla.Columns.Add("edad", typeof(string));
        tabla.Columns.Add("nacional", typeof(string));
        tabla.Columns.Add("estado", typeof(string));
        tabla.Columns.Add("activo", typeof(bool));

        foreach (var fila in filasVictimas)
        {
            tabla.Rows.Add(idFederalCarga, fila.NumeroFila, ValorTextoStaging(ObtenerValor(fila, "id_ci")), ValorTextoStaging(ObtenerValor(fila, "id_delito")), ValorTextoStaging(ObtenerValor(fila, "id_vicf")), ValorTextoStaging(ObtenerValor(fila, "id_tv")), ValorTextoStaging(ObtenerValor(fila, "id_tpm")), ValorTextoStaging(ObtenerValor(fila, "sexo")), ValorTextoStaging(ObtenerValor(fila, "genero")), ValorTextoStaging(ObtenerValor(fila, "pob")), ValorTextoStaging(ObtenerValor(fila, "disc")), ValorTextoStaging(ObtenerValor(fila, "fha_nac")), ValorTextoStaging(ObtenerValor(fila, "edad")), ValorTextoStaging(ObtenerValor(fila, "nacional")), "PENDIENTE", true);
        }

        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction) { DestinationTableName = "dbo.federal_carga_tmp_victima" };

        foreach (DataColumn columna in tabla.Columns) bulkCopy.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);

        await bulkCopy.WriteToServerAsync(tabla);
    }

    private static async Task<FederalCargaConfirmacionInfo?> ObtenerCargaConfirmacionAsync(SqlConnection connection, SqlTransaction transaction, string codigoReferencia, int idUsuarioConfirmacion)
    {
        const string sql = """
            SELECT
                c.id_federal_carga AS IdFederalCarga,
                c.id_usuario_carga AS IdUsuarioCarga,
                c.codigo_referencia AS CodigoReferencia,
                c.estado AS Estado,
                c.fecha_expiracion AS FechaExpiracion,
                CONVERT(bit, CASE WHEN r.rol = N'SUPER_USUARIO' THEN 1 ELSE 0 END) AS EsSuperUsuario,
                CONVERT(bit, ISNULL(um.habilita_carga, 0)) AS HabilitaCarga
            FROM dbo.federal_carga c WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN dbo.usuario u
                ON u.id_usuario = @IdUsuarioConfirmacion
               AND u.activo = 1
            INNER JOIN dbo.roles r
                ON r.id_rol = u.id_rol
               AND r.activo = 1
            INNER JOIN dbo.catalogo_modulo m
                ON m.clave = N'FEDERAL'
               AND m.activo = 1
            INNER JOIN dbo.usuario_modulo um
                ON um.id_usuario = u.id_usuario
               AND um.id_modulo = m.id_modulo
               AND um.habilitado = 1
               AND um.activo = 1
            WHERE c.codigo_referencia = @CodigoReferencia
              AND c.tipo_carga = N'CARGA_INICIAL'
              AND c.activo = 1;
            """;

        return await connection.QueryFirstOrDefaultAsync<FederalCargaConfirmacionInfo>(sql, new
        {
            CodigoReferencia = codigoReferencia,
            IdUsuarioConfirmacion = idUsuarioConfirmacion
        }, transaction);
    }

    private static async Task ActualizarCargaExpiradaAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga)
    {
        const string sql = """
            UPDATE dbo.federal_carga
            SET estado = N'EXPIRADO',
                mensaje_error = N'La carga federal expiró antes de ser confirmada.'
            WHERE id_federal_carga = @IdFederalCarga;

            UPDATE dbo.federal_carga_tmp_carpeta SET estado = N'EXPIRADO', fecha_procesamiento = SYSDATETIME() WHERE id_federal_carga = @IdFederalCarga;
            UPDATE dbo.federal_carga_tmp_delito SET estado = N'EXPIRADO', fecha_procesamiento = SYSDATETIME() WHERE id_federal_carga = @IdFederalCarga;
            UPDATE dbo.federal_carga_tmp_victima SET estado = N'EXPIRADO', fecha_procesamiento = SYSDATETIME() WHERE id_federal_carga = @IdFederalCarga;
            """;

        await connection.ExecuteAsync(sql, new { IdFederalCarga = idFederalCarga }, transaction);
    }

    private static async Task RechazarCargaAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, int idUsuarioConfirmacion)
    {
        const string sql = """
            UPDATE dbo.federal_carga
            SET estado = N'RECHAZADO_VALIDACION',
                fecha_confirmacion = SYSDATETIME(),
                id_usuario_confirmacion = @IdUsuarioConfirmacion,
                mensaje_error = N'La carga federal fue rechazada por el usuario después de revisar las validaciones.'
            WHERE id_federal_carga = @IdFederalCarga;

            UPDATE dbo.federal_carga_tmp_carpeta SET estado = N'RECHAZADO_VALIDACION', fecha_procesamiento = SYSDATETIME() WHERE id_federal_carga = @IdFederalCarga;
            UPDATE dbo.federal_carga_tmp_delito SET estado = N'RECHAZADO_VALIDACION', fecha_procesamiento = SYSDATETIME() WHERE id_federal_carga = @IdFederalCarga;
            UPDATE dbo.federal_carga_tmp_victima SET estado = N'RECHAZADO_VALIDACION', fecha_procesamiento = SYSDATETIME() WHERE id_federal_carga = @IdFederalCarga;
            """;

        await connection.ExecuteAsync(sql, new { IdFederalCarga = idFederalCarga, IdUsuarioConfirmacion = idUsuarioConfirmacion }, transaction);
    }

    private static async Task RechazarCargaAdministracionAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, int idUsuarioRechazo, string motivo)
    {
        const string sql = """
        UPDATE dbo.federal_carga
        SET estado = N'RECHAZADO_ADMIN',
            fecha_confirmacion = SYSDATETIME(),
            id_usuario_confirmacion = @IdUsuarioRechazo,
            mensaje_error = @Motivo,
            rechazo_visto = 0,
            fecha_rechazo_visto = NULL
        WHERE id_federal_carga = @IdFederalCarga;

        UPDATE dbo.federal_carga_tmp_carpeta
        SET estado = N'RECHAZADO_ADMIN',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_federal_carga = @IdFederalCarga;

        UPDATE dbo.federal_carga_tmp_delito
        SET estado = N'RECHAZADO_ADMIN',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_federal_carga = @IdFederalCarga;

        UPDATE dbo.federal_carga_tmp_victima
        SET estado = N'RECHAZADO_ADMIN',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_federal_carga = @IdFederalCarga;
        """;

        await connection.ExecuteAsync(sql, new
        {
            IdFederalCarga = idFederalCarga,
            IdUsuarioRechazo = idUsuarioRechazo,
            Motivo = motivo
        }, transaction);
    }

    private static async Task EnviarCargaAprobacionAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga)
    {
        const string sql = """
            UPDATE dbo.federal_carga SET estado = N'PENDIENTE_APROBACION', fecha_expiracion = NULL, mensaje_error = NULL WHERE id_federal_carga = @IdFederalCarga;
            UPDATE dbo.federal_carga_tmp_carpeta SET estado = N'PENDIENTE_APROBACION', fecha_procesamiento = NULL WHERE id_federal_carga = @IdFederalCarga;
            UPDATE dbo.federal_carga_tmp_delito SET estado = N'PENDIENTE_APROBACION', fecha_procesamiento = NULL WHERE id_federal_carga = @IdFederalCarga;
            UPDATE dbo.federal_carga_tmp_victima SET estado = N'PENDIENTE_APROBACION', fecha_procesamiento = NULL WHERE id_federal_carga = @IdFederalCarga;
            """;

        await connection.ExecuteAsync(sql, new { IdFederalCarga = idFederalCarga }, transaction);
    }

    private static async Task InsertarCarpetasFinalesAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, int idUsuarioRegistro)
    {
        const string sql = """
            INSERT INTO dbo.federal_carpeta_investigacion
            (
                identificador_carpeta_fiscalia,
                nomenclatura_carpeta_fiscalia,
                fecha_inicio,
                resumen_hechos,
                id_usuario_registro,
                fecha_registro,
                id_federal_carga,
                activo
            )
            SELECT
                c.id_ci,
                c.ntra_ci,
                COALESCE(
                    TRY_CONVERT(datetime2, CONCAT(c.fha_de_ini, N' ', NULLIF(LTRIM(RTRIM(c.hra_de_ini)), N'')), 103),
                    TRY_CONVERT(datetime2, CONCAT(c.fha_de_ini, N' ', NULLIF(LTRIM(RTRIM(c.hra_de_ini)), N''))),
                    CASE
                        WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), N''), N',', N'.')) >= 0
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), N''), N',', N'.')) < 1
                        THEN DATEADD(SECOND, CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), N''), N',', N'.')) * 86400, 0)), COALESCE(TRY_CONVERT(datetime2, c.fha_de_ini, 103), TRY_CONVERT(datetime2, c.fha_de_ini)))
                    END,
                    TRY_CONVERT(datetime2, c.fha_de_ini, 103),
                    TRY_CONVERT(datetime2, c.fha_de_ini)
                ),
                c.rmen_de_hchos,
                @IdUsuarioRegistro,
                SYSDATETIME(),
                @IdFederalCarga,
                1
            FROM dbo.federal_carga_tmp_carpeta c
            WHERE c.id_federal_carga = @IdFederalCarga
              AND c.activo = 1;
            """;

        await connection.ExecuteAsync(sql, new { IdFederalCarga = idFederalCarga, IdUsuarioRegistro = idUsuarioRegistro }, transaction);
    }

    private static async Task InsertarDelitosFinalesAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, int idUsuarioRegistro)
    {
        const string sql = """
            INSERT INTO dbo.federal_delito
            (
                id_federal_carpeta_investigacion,
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
                codigo_postal_fiscalia,
                coordenada_x,
                coordenada_y,
                domicilio_hechos,
                id_usuario_registro,
                fecha_registro,
                id_federal_carga,
                activo
            )
            SELECT
                ci.id_federal_carpeta_investigacion,
                d.id_delito,
                d.dto,
                d.moda_dto,
                fa.id_forma_accion,
                COALESCE(
                    TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, N' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), N'')), 103),
                    TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, N' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), N''))),
                    CASE
                        WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), N''), N',', N'.')) >= 0
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), N''), N',', N'.')) < 1
                        THEN DATEADD(SECOND, CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), N''), N',', N'.')) * 86400, 0)), COALESCE(TRY_CONVERT(datetime2, d.fha_de_hchos, 103), TRY_CONVERT(datetime2, d.fha_de_hchos)))
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
                NULLIF(LTRIM(RTRIM(d.cp)), N''),
                TRY_CONVERT(decimal(10,6), NULLIF(REPLACE(d.coord_x, N',', N'.'), N'')),
                TRY_CONVERT(decimal(10,6), NULLIF(REPLACE(d.coord_y, N',', N'.'), N'')),
                d.dom_hchos,
                @IdUsuarioRegistro,
                SYSDATETIME(),
                @IdFederalCarga,
                1
            FROM dbo.federal_carga_tmp_delito d
            INNER JOIN dbo.federal_carpeta_investigacion ci
                ON ci.id_federal_carga = d.id_federal_carga
               AND ci.identificador_carpeta_fiscalia = d.id_ci
               AND ci.activo = 1
            INNER JOIN dbo.federal_catalogo_modalidad_delito md
                ON md.clave4 = d.clasf_de_dto
               AND md.activo = 1
            INNER JOIN dbo.catalogo_forma_accion fa ON fa.clave = TRY_CONVERT(tinyint, d.forma_acc) AND fa.activo = 1
            INNER JOIN dbo.catalogo_instrumento_comision ic ON ic.clave = TRY_CONVERT(tinyint, d.emto_com_dto) AND ic.activo = 1
            INNER JOIN dbo.catalogo_grado_consumacion gc ON gc.clave = TRY_CONVERT(tinyint, d.grdo_cons) AND gc.activo = 1
            INNER JOIN dbo.catalogo_entidad_federativa ef ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos) AND ef.activo = 1
            INNER JOIN dbo.catalogo_municipio mun ON mun.id_entidad_federativa = ef.id_entidad_federativa AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos) AND mun.activo = 1
            OUTER APPLY
            (
                SELECT TOP 1 ccp.id_codigo_postal
                FROM dbo.catalogo_codigo_postal ccp
                WHERE ccp.codigo_postal = RIGHT(N'00000' + LTRIM(RTRIM(d.cp)), 5)
                  AND ccp.id_municipio = mun.id_municipio
                  AND ccp.activo = 1
                ORDER BY ccp.id_codigo_postal
            ) cp
            WHERE d.id_federal_carga = @IdFederalCarga
              AND d.activo = 1;
            """;

        await connection.ExecuteAsync(sql, new { IdFederalCarga = idFederalCarga, IdUsuarioRegistro = idUsuarioRegistro }, transaction);
    }

    private static async Task InsertarVictimasFinalesAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, int idUsuarioRegistro)
    {
        const string sql = """
            INSERT INTO dbo.federal_victima
            (
                id_federal_delito,
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
                id_federal_carga,
                activo
            )
            SELECT
                de.id_federal_delito,
                v.id_vicf,
                tv.id_tipo_victima,
                tvm.id_tipo_victima_moral,
                sx.id_sexo,
                gen.id_genero,
                nac.id_nacionalidad,
                pob.id_pertenece_poblacion_indigena,
                disc.id_presenta_discapacidad,
                COALESCE(TRY_CONVERT(date, NULLIF(v.fha_nac, N''), 103), TRY_CONVERT(date, NULLIF(v.fha_nac, N''))),
                TRY_CONVERT(smallint, NULLIF(v.edad, N'')),
                @IdUsuarioRegistro,
                SYSDATETIME(),
                @IdFederalCarga,
                1
            FROM dbo.federal_carga_tmp_victima v
            INNER JOIN dbo.federal_carpeta_investigacion ci
                ON ci.id_federal_carga = v.id_federal_carga
               AND ci.identificador_carpeta_fiscalia = v.id_ci
               AND ci.activo = 1
            INNER JOIN dbo.federal_delito de
                ON de.id_federal_carga = v.id_federal_carga
               AND de.id_federal_carpeta_investigacion = ci.id_federal_carpeta_investigacion
               AND de.identificador_delito_fiscalia = v.id_delito
               AND de.activo = 1
            INNER JOIN dbo.catalogo_tipo_victima tv ON tv.clave = TRY_CONVERT(tinyint, v.id_tv) AND tv.activo = 1
            LEFT JOIN dbo.catalogo_tipo_victima_moral tvm ON tvm.clave = TRY_CONVERT(tinyint, NULLIF(v.id_tpm, N'')) AND tvm.activo = 1
            LEFT JOIN dbo.catalogo_sexo sx ON sx.clave = TRY_CONVERT(tinyint, NULLIF(v.sexo, N'')) AND sx.activo = 1
            LEFT JOIN dbo.catalogo_genero gen ON gen.clave = TRY_CONVERT(tinyint, NULLIF(v.genero, N'')) AND gen.activo = 1
            LEFT JOIN dbo.catalogo_nacionalidad nac ON TRY_CONVERT(int, nac.clave) = TRY_CONVERT(int, NULLIF(v.nacional, N'')) AND nac.activo = 1
            LEFT JOIN dbo.catalogo_pertenece_poblacion_indigena pob ON pob.clave = TRY_CONVERT(tinyint, NULLIF(v.pob, N'')) AND pob.activo = 1
            LEFT JOIN dbo.catalogo_presenta_discapacidad disc ON disc.clave = TRY_CONVERT(tinyint, NULLIF(v.disc, N'')) AND disc.activo = 1
            WHERE v.id_federal_carga = @IdFederalCarga
              AND v.activo = 1;
            """;

        await connection.ExecuteAsync(sql, new { IdFederalCarga = idFederalCarga, IdUsuarioRegistro = idUsuarioRegistro }, transaction);
    }

    private static async Task ConfirmarCargaFinalAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, int idUsuarioConfirmacion)
    {
        const string sql = """
            UPDATE dbo.federal_carga
            SET estado = N'CONFIRMADO',
                fecha_confirmacion = SYSDATETIME(),
                id_usuario_confirmacion = @IdUsuarioConfirmacion,
                mensaje_error = NULL
            WHERE id_federal_carga = @IdFederalCarga;

            UPDATE dbo.federal_carga_tmp_carpeta SET estado = N'PROCESADO', fecha_procesamiento = SYSDATETIME() WHERE id_federal_carga = @IdFederalCarga;
            UPDATE dbo.federal_carga_tmp_delito SET estado = N'PROCESADO', fecha_procesamiento = SYSDATETIME() WHERE id_federal_carga = @IdFederalCarga;
            UPDATE dbo.federal_carga_tmp_victima SET estado = N'PROCESADO', fecha_procesamiento = SYSDATETIME() WHERE id_federal_carga = @IdFederalCarga;
            """;

        await connection.ExecuteAsync(sql, new { IdFederalCarga = idFederalCarga, IdUsuarioConfirmacion = idUsuarioConfirmacion }, transaction);
    }

    private static string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }

    private static string ValorTextoStaging(string? valor) => string.IsNullOrWhiteSpace(valor) ? string.Empty : valor.Trim();
}
