using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class CargaRepository : ICargaRepository
{
    private class CargaConfirmacionInfo
    {
        public long IdCarga { get; set; }
        public string CodigoReferencia { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public DateTime? FechaExpiracion { get; set; }
        public int? IdEntidadFederativaCarga { get; set; }
        public int? IdEntidadFederativaUsuario { get; set; }
        public bool EsSuperUsuario { get; set; }
        public bool HabilitaCarga { get; set; }
    }

    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CargaRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    private static string ValorTextoStaging(object? valor)
    {
        if (valor == null || valor == DBNull.Value)
        {
            return string.Empty;
        }

        return valor.ToString()?.Trim() ?? string.Empty;
    }

    public async Task<long> GuardarIntentoCargaAsync(int idUsuarioCarga, int? idEntidadFederativa, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError, List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas)
    {
        // Este método guarda todo el intento de carga en una sola transacción.
        // Si falla cualquier parte, se revierte carga y staging.
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var idCarga = await CrearCargaAsync(
                connection,
                transaction,
                idUsuarioCarga,
                idEntidadFederativa,
                codigoReferencia,
                tipoCarga: "CARGA_INICIAL",
                mesCorte,
                anioCorte,
                totalCarpetas,
                totalDelitos,
                totalVictimas,
                estado,
                mensajeError);

            await GuardarTmpCarpetasAsync(
                connection,
                transaction,
                idCarga,
                filasCarpetas);

            await GuardarTmpDelitosAsync(
                connection,
                transaction,
                idCarga,
                filasDelitos);

            await GuardarTmpVictimasAsync(
                connection,
                transaction,
                idCarga,
                filasVictimas);

            await transaction.CommitAsync();

            return idCarga;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<long> CrearCargaAsync(SqlConnection connection, SqlTransaction transaction, int idUsuarioCarga, int? idEntidadFederativa, string codigoReferencia, string tipoCarga, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError)
    {
        // Crea el intento de carga.
        // OUTPUT INSERTED.id_carga devuelve el ID generado por SQL Server.
        var sql = @"
            INSERT INTO carga (
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
            OUTPUT INSERTED.id_carga
            VALUES (
                @IdUsuarioCarga,
                @IdEntidadFederativa,
                @CodigoReferencia,
                @TipoCarga,
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
        ";

        var idCarga = await connection.ExecuteScalarAsync<long>(
            sql,
            new
            {
                IdUsuarioCarga = idUsuarioCarga,
                IdEntidadFederativa = idEntidadFederativa,
                CodigoReferencia = codigoReferencia,
                TipoCarga = tipoCarga,
                MesCorte = mesCorte,
                AnioCorte = anioCorte,
                TotalCarpetas = totalCarpetas,
                TotalDelitos = totalDelitos,
                TotalVictimas = totalVictimas,
                Estado = estado,
                MensajeError = mensajeError
            },
            transaction);

        return idCarga;
    }

    public async Task<bool> ExisteCargaConfirmadaAsync(int idEntidadFederativa, int mesCorte, int anioCorte)
    {
        // Revisa si ya existe una carga inicial confirmada para la entidad y periodo.
        // Esto se usa para saber si una actualización tiene una base confirmada previa.
        var sql = @"
        SELECT COUNT(1)
        FROM carga
        WHERE id_entidad_federativa = @IdEntidadFederativa
          AND mes_corte = @MesCorte
          AND anio_corte = @AnioCorte
          AND tipo_carga = 'CARGA_INICIAL'
          AND estado = 'CONFIRMADO'
          AND activo = 1;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var total = await connection.ExecuteScalarAsync<int>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa,
            MesCorte = mesCorte,
            AnioCorte = anioCorte
        });

        return total > 0;
    }

    private async Task GuardarTmpCarpetasAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, List<ArchivoFila> filasCarpetas)
    {
        // Guarda las carpetas leídas en staging usando carga masiva.
        // SqlBulkCopy evita insertar fila por fila.
        var tabla = new DataTable();

        tabla.Columns.Add("id_carga", typeof(long));
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
            tabla.Rows.Add(
                idCarga,
                fila.NumeroFila,
                ValorTextoStaging(ObtenerValor(fila, "id_ci")),
                ValorTextoStaging(ObtenerValor(fila, "ntra_ci")),
                ValorTextoStaging(ObtenerValor(fila, "fha_de_ini")),
                ValorTextoStaging(ObtenerValor(fila, "hra_de_ini")),
                ValorTextoStaging(ObtenerValor(fila, "rmen_de_hchos")),
                "PENDIENTE",
                true);
        }

        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
        {
            DestinationTableName = "carga_tmp_carpeta"
        };

        // El primer valor es el índice de la columna en el DataTable.
        // El segundo valor es el nombre de la columna destino en SQL Server.
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(0, "id_carga"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(1, "numero_fila"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(2, "id_ci"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(3, "ntra_ci"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(4, "fha_de_ini"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(5, "hra_de_ini"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(6, "rmen_de_hchos"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(7, "estado"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(8, "activo"));

        await bulkCopy.WriteToServerAsync(tabla);
    }

    private async Task GuardarTmpDelitosAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, List<ArchivoFila> filasDelitos)
    {
        // Guarda los delitos leídos en staging usando carga masiva.
        // Esta parte era la más pesada cuando se insertaba registro por registro.
        var tabla = new DataTable();

        tabla.Columns.Add("id_carga", typeof(long));
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
            tabla.Rows.Add(
                idCarga,
                fila.NumeroFila,
                ValorTextoStaging(ObtenerValor(fila, "id_ci")),
                ValorTextoStaging(ObtenerValor(fila, "id_delito")),
                ValorTextoStaging(ObtenerValor(fila, "dto")),
                ValorTextoStaging(ObtenerValor(fila, "moda_dto")),
                ValorTextoStaging(ObtenerValor(fila, "forma_acc")),
                ValorTextoStaging(ObtenerValor(fila, "fha_de_hchos")),
                ValorTextoStaging(ObtenerValor(fila, "hra_de_hchos")),
                ValorTextoStaging(ObtenerValor(fila, "emto_com_dto")),
                ValorTextoStaging(ObtenerValor(fila, "grdo_cons")),
                ValorTextoStaging(ObtenerValor(fila, "clasf_de_dto")),
                ValorTextoStaging(ObtenerValor(fila, "id_ent_hchos")),
                ValorTextoStaging(ObtenerValor(fila, "id_mun_hchos")),
                ValorTextoStaging(ObtenerValor(fila, "id_loc_hchos")),
                ValorTextoStaging(ObtenerValor(fila, "nom_loc_hchos")),
                ValorTextoStaging(ObtenerValor(fila, "id_col_hchos")),
                ValorTextoStaging(ObtenerValor(fila, "nom_col_hchos")),
                ValorTextoStaging(ObtenerValor(fila, "cp")),
                ValorTextoStaging(ObtenerValor(fila, "coord_x")),
                ValorTextoStaging(ObtenerValor(fila, "coord_y")),
                ValorTextoStaging(ObtenerValor(fila, "dom_hchos")),
                "PENDIENTE",
                true);
        }

        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
        {
            DestinationTableName = "carga_tmp_delito"
        };

        // El primer valor es el índice de la columna en el DataTable.
        // El segundo valor es el nombre de la columna destino en SQL Server.
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(0, "id_carga"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(1, "numero_fila"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(2, "id_ci"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(3, "id_delito"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(4, "dto"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(5, "moda_dto"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(6, "forma_acc"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(7, "fha_de_hchos"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(8, "hra_de_hchos"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(9, "emto_com_dto"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(10, "grdo_cons"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(11, "clasf_de_dto"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(12, "id_ent_hchos"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(13, "id_mun_hchos"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(14, "id_loc_hchos"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(15, "nom_loc_hchos"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(16, "id_col_hchos"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(17, "nom_col_hchos"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(18, "cp"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(19, "coord_x"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(20, "coord_y"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(21, "dom_hchos"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(22, "estado"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(23, "activo"));

        await bulkCopy.WriteToServerAsync(tabla);
    }

    private async Task GuardarTmpVictimasAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, List<ArchivoFila> filasVictimas)
    {
        // Guarda las víctimas leídas en staging usando carga masiva.
        // Se conserva el valor crudo del Excel para confirmar o auditar después.
        var tabla = new DataTable();

        tabla.Columns.Add("id_carga", typeof(long));
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
            tabla.Rows.Add(
                idCarga,
                fila.NumeroFila,
                ValorTextoStaging(ObtenerValor(fila, "id_ci")),
                ValorTextoStaging(ObtenerValor(fila, "id_delito")),
                ValorTextoStaging(ObtenerValor(fila, "id_vicf")),
                ValorTextoStaging(ObtenerValor(fila, "id_tv")),
                ValorTextoStaging(ObtenerValor(fila, "id_tpm")),
                ValorTextoStaging(ObtenerValor(fila, "sexo")),
                ValorTextoStaging(ObtenerValor(fila, "genero")),
                ValorTextoStaging(ObtenerValor(fila, "pob")),
                ValorTextoStaging(ObtenerValor(fila, "disc")),
                ValorTextoStaging(ObtenerValor(fila, "fha_nac")),
                ValorTextoStaging(ObtenerValor(fila, "edad")),
                ValorTextoStaging(ObtenerValor(fila, "nacional")),
                "PENDIENTE",
                true);
        }

        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
        {
            DestinationTableName = "carga_tmp_victima"
        };

        // El primer valor es el índice de la columna en el DataTable.
        // El segundo valor es el nombre de la columna destino en SQL Server.
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(0, "id_carga"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(1, "numero_fila"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(2, "id_ci"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(3, "id_delito"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(4, "id_vicf"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(5, "id_tv"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(6, "id_tpm"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(7, "sexo"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(8, "genero"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(9, "pob"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(10, "disc"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(11, "fha_nac"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(12, "edad"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(13, "nacional"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(14, "estado"));
        bulkCopy.ColumnMappings.Add(new SqlBulkCopyColumnMapping(15, "activo"));

        await bulkCopy.WriteToServerAsync(tabla);
    }

    public async Task<ConfirmarCargaResponse> ConfirmarCargaAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion)
    {
        // Confirma o rechaza una carga validada.
        // Todo se ejecuta en transacción para evitar cargas parciales.
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerCargaConfirmacionAsync(
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
                    Mensaje = "No se encontró una carga válida para confirmar."
                };
            }

            if (!string.Equals(carga.Estado, "VALIDADO_PENDIENTE", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = carga.Estado,
                    Mensaje = "La carga no se encuentra en estado VALIDADO_PENDIENTE."
                };
            }

            if (carga.FechaExpiracion.HasValue && carga.FechaExpiracion.Value < DateTime.Now)
            {
                await ActualizarCargaExpiradaAsync(
                    connection,
                    transaction,
                    carga.IdCarga);

                await transaction.CommitAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = "EXPIRADO",
                    Mensaje = "La carga ya expiró. Debe validar nuevamente los archivos."
                };
            }

            if (!carga.HabilitaCarga)
            {
                await transaction.RollbackAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = false,
                    CodigoReferencia = codigoReferencia,
                    Estado = carga.Estado,
                    Mensaje = "El usuario no tiene habilitada la carga de información."
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
                    Mensaje = "El usuario no puede confirmar cargas de otra entidad federativa."
                };
            }

            if (!aceptar)
            {
                await RechazarCargaAsync(
                    connection,
                    transaction,
                    carga.IdCarga,
                    idUsuarioConfirmacion);

                await transaction.CommitAsync();

                return new ConfirmarCargaResponse
                {
                    EsValido = true,
                    CodigoReferencia = codigoReferencia,
                    Estado = "RECHAZADO_USUARIO",
                    Mensaje = "La carga fue rechazada por el usuario."
                };
            }

            await InsertarCarpetasFinalesAsync(
                connection,
                transaction,
                carga.IdCarga,
                idUsuarioConfirmacion);

            await InsertarDelitosFinalesAsync(
                connection,
                transaction,
                carga.IdCarga,
                idUsuarioConfirmacion);

            await InsertarVictimasFinalesAsync(
                connection,
                transaction,
                carga.IdCarga,
                idUsuarioConfirmacion);

            await ConfirmarCargaFinalAsync(
                connection,
                transaction,
                carga.IdCarga,
                idUsuarioConfirmacion);

            await transaction.CommitAsync();

            return new ConfirmarCargaResponse
            {
                EsValido = true,
                CodigoReferencia = codigoReferencia,
                Estado = "CONFIRMADO",
                Mensaje = "La carga fue confirmada correctamente."
            };
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task<CargaConfirmacionInfo?> ObtenerCargaConfirmacionAsync(SqlConnection connection, SqlTransaction transaction, string codigoReferencia, int idUsuarioConfirmacion)
    {
        // Obtiene datos de carga y permisos del usuario que confirma.
        var sql = @"
        SELECT
            c.id_carga AS IdCarga,
            c.codigo_referencia AS CodigoReferencia,
            c.estado AS Estado,
            c.fecha_expiracion AS FechaExpiracion,
            c.id_entidad_federativa AS IdEntidadFederativaCarga,
            u.id_entidad_federativa AS IdEntidadFederativaUsuario,
            CASE WHEN r.rol = 'SUPER_USUARIO' THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS EsSuperUsuario,
            ISNULL(h.habilita_carga, 0) AS HabilitaCarga
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
          AND c.activo = 1;
    ";

        return await connection.QueryFirstOrDefaultAsync<CargaConfirmacionInfo>(
            sql,
            new
            {
                CodigoReferencia = codigoReferencia,
                IdUsuarioConfirmacion = idUsuarioConfirmacion
            },
            transaction);
    }

    private async Task ActualizarCargaExpiradaAsync(SqlConnection connection, SqlTransaction transaction, long idCarga)
    {
        var sql = @"
        UPDATE carga
        SET estado = 'EXPIRADO',
            mensaje_error = 'La carga expiró antes de ser confirmada.'
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_carpeta
        SET estado = 'EXPIRADO',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_delito
        SET estado = 'EXPIRADO',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_victima
        SET estado = 'EXPIRADO',
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

    private async Task RechazarCargaAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, int idUsuarioConfirmacion)
    {
        var sql = @"
        UPDATE carga
        SET estado = 'RECHAZADO_USUARIO',
            fecha_confirmacion = SYSDATETIME(),
            id_usuario_confirmacion = @IdUsuarioConfirmacion
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_carpeta
        SET estado = 'RECHAZADO_USUARIO',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_delito
        SET estado = 'RECHAZADO_USUARIO',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_carga = @IdCarga;

        UPDATE carga_tmp_victima
        SET estado = 'RECHAZADO_USUARIO',
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

    public async Task ActualizarEstadoCargaAsync(long idCarga, string estado, string? mensajeError)
    {
        // Actualiza el estado del intento de carga.
        var sql = @"
            UPDATE carga
            SET estado = @Estado,
                mensaje_error = @MensajeError
            WHERE id_carga = @IdCarga;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        await connection.ExecuteAsync(sql, new
        {
            IdCarga = idCarga,
            Estado = estado,
            MensajeError = mensajeError
        });
    }

    private static string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }

    private async Task InsertarCarpetasFinalesAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, int idUsuarioRegistro)
    {
        // Inserta carpetas desde staging a tabla final.
        var sql = @"
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
            c.id_ci,
            c.ntra_ci,
            COALESCE(
                TRY_CONVERT(datetime2, CONCAT(c.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(c.hra_de_ini)), '')), 103),
                TRY_CONVERT(datetime2, CONCAT(c.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(c.hra_de_ini)), ''))),
                CASE
                    WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), ''), ',', '.')) IS NOT NULL
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), ''), ',', '.')) >= 0
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), ''), ',', '.')) < 1
                    THEN DATEADD(
                        SECOND,
                        CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), ''), ',', '.')) * 86400, 0)),
                        COALESCE(
                            TRY_CONVERT(datetime2, c.fha_de_ini, 103),
                            TRY_CONVERT(datetime2, c.fha_de_ini)
                        )
                    )
                END,
                TRY_CONVERT(datetime2, c.fha_de_ini, 103),
                TRY_CONVERT(datetime2, c.fha_de_ini)
            ),
            c.rmen_de_hchos,
            @IdUsuarioRegistro,
            SYSDATETIME(),
            @IdCarga,
            1
        FROM carga_tmp_carpeta c
        WHERE c.id_carga = @IdCarga
          AND c.activo = 1;
    ";

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdCarga = idCarga,
                IdUsuarioRegistro = idUsuarioRegistro
            },
            transaction);
    }

    private async Task InsertarDelitosFinalesAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, int idUsuarioRegistro)
    {
        // Inserta delitos desde staging a tabla final.
        // Los ids de catálogos se resuelven aquí desde las claves recibidas en Excel.
        var sql = @"
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
            ci.id_carpeta_investigacion,
            d.id_delito,
            d.dto,
            d.moda_dto,
            fa.id_forma_accion,
            COALESCE(
                TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(d.hra_de_hchos, '')), 103),
                TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''))),
                CASE
                    WHEN TRY_CONVERT(float, REPLACE(NULLIF(d.hra_de_hchos, ''), ',', '.')) IS NOT NULL
                         AND TRY_CONVERT(float, REPLACE(NULLIF(d.hra_de_hchos, ''), ',', '.')) >= 0
                         AND TRY_CONVERT(float, REPLACE(NULLIF(d.hra_de_hchos, ''), ',', '.')) < 1
                    THEN DATEADD(
                        SECOND,
                        CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(d.hra_de_hchos, ''), ',', '.')) * 86400, 0)),
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
            @IdUsuarioRegistro,
            SYSDATETIME(),
            @IdCarga,
            1
        FROM carga_tmp_delito d
        INNER JOIN carpeta_investigacion ci
            ON ci.id_carga = d.id_carga
           AND ci.identificador_carpeta_fiscalia = d.id_ci
           AND ci.activo = 1
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
        WHERE d.id_carga = @IdCarga
          AND d.activo = 1;
    ";

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdCarga = idCarga,
                IdUsuarioRegistro = idUsuarioRegistro
            },
            transaction);
    }

    private async Task InsertarVictimasFinalesAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, int idUsuarioRegistro)
    {
        // Inserta víctimas desde staging a tabla final.
        // Los datos opcionales se insertan como NULL cuando vienen vacíos.
        var sql = @"
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
            de.id_delito,
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
            @IdUsuarioRegistro,
            SYSDATETIME(),
            @IdCarga,
            1
        FROM carga_tmp_victima v
        INNER JOIN carpeta_investigacion ci
            ON ci.id_carga = v.id_carga
           AND ci.identificador_carpeta_fiscalia = v.id_ci
           AND ci.activo = 1
        INNER JOIN delito de
            ON de.id_carga = v.id_carga
           AND de.id_carpeta_investigacion = ci.id_carpeta_investigacion
           AND de.identificador_delito_fiscalia = v.id_delito
           AND de.activo = 1
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
        WHERE v.id_carga = @IdCarga
          AND v.activo = 1;
    ";

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdCarga = idCarga,
                IdUsuarioRegistro = idUsuarioRegistro
            },
            transaction);
    }

    private async Task ConfirmarCargaFinalAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, int idUsuarioConfirmacion)
    {
        var sql = @"
        UPDATE carga
        SET estado = 'CONFIRMADO',
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

    public async Task<string?> ObtenerCodigoCargaPendienteAsync(int idEntidadFederativa, int mesCorte, int anioCorte)
    {
        // Revisa si ya existe una carga validada pendiente de confirmar
        // para la misma entidad y periodo.
        // Esto evita generar múltiples cargas pendientes del mismo corte.
        var sql = @"
        SELECT TOP 1 codigo_referencia
        FROM carga
        WHERE id_entidad_federativa = @IdEntidadFederativa
          AND mes_corte = @MesCorte
          AND anio_corte = @AnioCorte
          AND estado = 'VALIDADO_PENDIENTE'
          AND activo = 1
        ORDER BY fecha_validacion DESC;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<string?>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa,
            MesCorte = mesCorte,
            AnioCorte = anioCorte
        });
    }

    public async Task<string?> ObtenerCodigoActualizacionPendienteAsync(int idEntidadFederativa, int mesCorte, int anioCorte)
    {
        // Revisa si ya existe una actualización validada pendiente de confirmar
        // para la misma entidad y periodo.
        var sql = @"
        SELECT TOP 1 codigo_referencia
        FROM carga
        WHERE id_entidad_federativa = @IdEntidadFederativa
          AND mes_corte = @MesCorte
          AND anio_corte = @AnioCorte
          AND tipo_carga = 'ACTUALIZACION'
          AND estado = 'VALIDADO_PENDIENTE_ACTUALIZACION'
          AND activo = 1
        ORDER BY fecha_validacion DESC;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<string?>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa,
            MesCorte = mesCorte,
            AnioCorte = anioCorte
        });
    }

    public async Task<List<ActualizacionAnioDisponibleItem>> ObtenerPeriodosDisponiblesActualizacionAsync(int idEntidadFederativa)
    {
        // Obtiene los periodos realmente disponibles para actualización.
        // Disponible significa:
        // - existe carga inicial confirmada
        // - no existe actualización pendiente para ese periodo
        var sql = @"
        SELECT
            c.anio_corte AS AnioCorte,
            c.mes_corte AS MesCorte
        FROM carga c
        WHERE c.id_entidad_federativa = @IdEntidadFederativa
          AND c.tipo_carga = 'CARGA_INICIAL'
          AND c.estado = 'CONFIRMADO'
          AND c.activo = 1
          AND NOT EXISTS (
              SELECT 1
              FROM carga ca
              WHERE ca.id_entidad_federativa = c.id_entidad_federativa
                AND ca.mes_corte = c.mes_corte
                AND ca.anio_corte = c.anio_corte
                AND ca.tipo_carga = 'ACTUALIZACION'
                AND ca.estado = 'VALIDADO_PENDIENTE_ACTUALIZACION'
                AND ca.activo = 1
          )
        GROUP BY
            c.anio_corte,
            c.mes_corte
        ORDER BY
            c.anio_corte DESC,
            c.mes_corte DESC;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var periodos = await connection.QueryAsync<(int AnioCorte, int MesCorte)>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa
        });

        return periodos
            .GroupBy(x => x.AnioCorte)
            .Select(g => new ActualizacionAnioDisponibleItem
            {
                AnioCorte = g.Key,
                Meses = g
                    .OrderByDescending(x => x.MesCorte)
                    .Select(x => new ActualizacionMesDisponibleItem
                    {
                        MesCorte = x.MesCorte,
                        NombreMes = ObtenerNombreMes(x.MesCorte),
                        Periodo = $"{x.MesCorte:00}/{x.AnioCorte}"
                    })
                    .ToList()
            })
            .OrderByDescending(x => x.AnioCorte)
            .ToList();
    }

    private static string ObtenerNombreMes(int mes)
    {
        return mes switch
        {
            1 => "Enero",
            2 => "Febrero",
            3 => "Marzo",
            4 => "Abril",
            5 => "Mayo",
            6 => "Junio",
            7 => "Julio",
            8 => "Agosto",
            9 => "Septiembre",
            10 => "Octubre",
            11 => "Noviembre",
            12 => "Diciembre",
            _ => string.Empty
        };
    }
}