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

    private class ActualizacionDiferenciaRow
    {
        public string Seccion { get; set; } = string.Empty;
        public string TipoMovimiento { get; set; } = string.Empty;
        public string CampoIdentificador { get; set; } = string.Empty;
        public string IdentificadorFiscalia { get; set; } = string.Empty;
        public string? Campo { get; set; }
        public string? ValorAnterior { get; set; }
        public string? ValorNuevo { get; set; }
    }

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

    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CargaRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
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
                ObtenerValor(fila, "id_ci"),
                ObtenerValor(fila, "ntra_ci"),
                ObtenerValor(fila, "fha_de_ini"),
                ObtenerValor(fila, "hra_de_ini"),
                ObtenerValor(fila, "rmen_de_hchos"),
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
                ObtenerValor(fila, "id_ci"),
                ObtenerValor(fila, "id_delito"),
                ObtenerValor(fila, "dto"),
                ObtenerValor(fila, "moda_dto"),
                ObtenerValor(fila, "forma_acc"),
                ObtenerValor(fila, "fha_de_hchos"),
                ObtenerValor(fila, "hra_de_hchos"),
                ObtenerValor(fila, "emto_com_dto"),
                ObtenerValor(fila, "grdo_cons"),
                ObtenerValor(fila, "clasf_de_dto"),
                ObtenerValor(fila, "id_ent_hchos"),
                ObtenerValor(fila, "id_mun_hchos"),
                ObtenerValor(fila, "id_loc_hchos"),
                ObtenerValor(fila, "nom_loc_hchos"),
                ObtenerValor(fila, "id_col_hchos"),
                ObtenerValor(fila, "nom_col_hchos"),
                ObtenerValor(fila, "cp"),
                ObtenerValor(fila, "coord_x"),
                ObtenerValor(fila, "coord_y"),
                ObtenerValor(fila, "dom_hchos"),
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
                ObtenerValor(fila, "id_ci"),
                ObtenerValor(fila, "id_delito"),
                ObtenerValor(fila, "id_vicf"),
                ObtenerValor(fila, "id_tv"),
                ObtenerValor(fila, "id_tpm"),
                ObtenerValor(fila, "sexo"),
                ObtenerValor(fila, "genero"),
                ObtenerValor(fila, "pob"),
                ObtenerValor(fila, "disc"),
                ObtenerValor(fila, "fha_nac"),
                ObtenerValor(fila, "edad"),
                ObtenerValor(fila, "nacional"),
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

    public async Task<long> GuardarIntentoActualizacionAsync(int idUsuarioCarga, int? idEntidadFederativa, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError,  List<ArchivoFila> filasCarpetas,  List<ArchivoFila> filasDelitos,  List<ArchivoFila> filasVictimas)
    {
        // Guarda la actualización igual que una carga:
        // registro en carga + staging de carpetas/delitos/víctimas.
        // La diferencia es tipo_carga = ACTUALIZACION.
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
                tipoCarga: "ACTUALIZACION",
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

    public async Task<List<CargaValidacionResumenItem>> ObtenerResumenDiferenciasActualizacionAsync(long idCargaActualizacion)
    {
        // Compara la actualización en staging contra la versión final activa vigente
        // del mismo periodo y entidad.
        //
        // Se usa ROW_NUMBER para tomar la versión activa más reciente por identificador fiscalía.
        // Esto evita mezclar información cuando ya existen actualizaciones confirmadas previas.

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

        ;WITH cargas_periodo AS (
            SELECT
                id_carga,
                fecha_confirmacion
            FROM carga
            WHERE id_entidad_federativa = @IdEntidadFederativa
              AND mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND activo = 1
        ),
        carpetas_actuales_base AS (
            SELECT
                ci.id_carpeta_investigacion,
                ci.identificador_carpeta_fiscalia,
                ci.nomenclatura_carpeta_fiscalia,
                ci.fecha_inicio,
                ci.resumen_hechos,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, ci.id_carga DESC, ci.id_carpeta_investigacion DESC
                ) AS rn
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
        ),
        carpetas_actuales AS (
            SELECT
                id_carpeta_investigacion,
                identificador_carpeta_fiscalia,
                nomenclatura_carpeta_fiscalia,
                fecha_inicio,
                resumen_hechos
            FROM carpetas_actuales_base
            WHERE rn = 1
        ),
        carpetas_tmp AS (
            SELECT
                id_ci,
                ntra_ci,
                COALESCE(
                    TRY_CONVERT(datetime2, fha_de_ini, 103),
                    TRY_CONVERT(datetime2, fha_de_ini)
                ) AS fecha_inicio,
                rmen_de_hchos
            FROM carga_tmp_carpeta
            WHERE id_carga = @IdCargaActualizacion
              AND activo = 1
        ),
        carpetas_clasificadas AS (
            SELECT
                CASE
                    WHEN ca.identificador_carpeta_fiscalia IS NULL THEN 'NUEVO'
                    WHEN
                        ISNULL(ca.nomenclatura_carpeta_fiscalia, '') <> ISNULL(ct.ntra_ci, '')
                        OR ISNULL(CONVERT(varchar(19), ca.fecha_inicio, 120), '') <> ISNULL(CONVERT(varchar(19), ct.fecha_inicio, 120), '')
                        OR ISNULL(ca.resumen_hechos, '') <> ISNULL(ct.rmen_de_hchos, '')
                        THEN 'MODIFICADO'
                    ELSE 'SIN_CAMBIOS'
                END AS tipo
            FROM carpetas_tmp ct
            LEFT JOIN carpetas_actuales ca
                ON ca.identificador_carpeta_fiscalia = ct.id_ci

            UNION ALL

            SELECT 'ELIMINADO'
            FROM carpetas_actuales ca
            LEFT JOIN carpetas_tmp ct
                ON ct.id_ci = ca.identificador_carpeta_fiscalia
            WHERE ct.id_ci IS NULL
        ),
        delitos_actuales_base AS (
            SELECT
                d.id_delito,
                ci.identificador_carpeta_fiscalia AS id_ci,
                d.identificador_delito_fiscalia,
                d.delito_fiscalia,
                d.modalidad_delito_fiscalia,
                d.id_forma_accion,
                d.fecha_hechos,
                d.id_instrumento_comision,
                d.id_grado_consumacion,
                d.id_modalidad_delito,
                d.id_entidad_federativa,
                d.id_municipio,
                d.id_localidad_fiscalia,
                d.localidad_fiscalia_nombre,
                d.id_colonia_fiscalia,
                d.colonia_fiscalia_nombre,
                d.id_codigo_postal,
                d.coordenada_x,
                d.coordenada_y,
                d.domicilio_hechos,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, d.id_carga DESC, d.id_delito DESC
                ) AS rn
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            WHERE d.activo = 1
        ),
        delitos_actuales AS (
            SELECT
                id_delito,
                id_ci,
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
                domicilio_hechos
            FROM delitos_actuales_base
            WHERE rn = 1
        ),
        delitos_tmp AS (
            SELECT
                d.id_ci,
                d.id_delito,
                d.dto,
                d.moda_dto,
                fa.id_forma_accion,
                COALESCE(
                    TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(d.hra_de_hchos, '')), 103),
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
        delitos_clasificados AS (
            SELECT
                CASE
                    WHEN da.identificador_delito_fiscalia IS NULL THEN 'NUEVO'
                    WHEN
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
                        THEN 'MODIFICADO'
                    ELSE 'SIN_CAMBIOS'
                END AS tipo
            FROM delitos_tmp dt
            LEFT JOIN delitos_actuales da
                ON da.id_ci = dt.id_ci
               AND da.identificador_delito_fiscalia = dt.id_delito

            UNION ALL

            SELECT 'ELIMINADO'
            FROM delitos_actuales da
            LEFT JOIN delitos_tmp dt
                ON dt.id_ci = da.id_ci
               AND dt.id_delito = da.identificador_delito_fiscalia
            WHERE dt.id_delito IS NULL
        ),
        victimas_actuales_base AS (
            SELECT
                v.id_victima,
                ci.identificador_carpeta_fiscalia AS id_ci,
                d.identificador_delito_fiscalia AS id_delito_fiscalia,
                v.identificador_victima_fiscalia,
                v.id_tipo_victima,
                v.id_tipo_victima_moral,
                v.id_sexo,
                v.id_genero,
                v.id_nacionalidad,
                v.id_pertenece_poblacion_indigena,
                v.id_presenta_discapacidad,
                v.fecha_nacimiento,
                v.edad,
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
        victimas_actuales AS (
            SELECT
                id_victima,
                id_ci,
                id_delito_fiscalia,
                identificador_victima_fiscalia,
                id_tipo_victima,
                id_tipo_victima_moral,
                id_sexo,
                id_genero,
                id_nacionalidad,
                id_pertenece_poblacion_indigena,
                id_presenta_discapacidad,
                fecha_nacimiento,
                edad
            FROM victimas_actuales_base
            WHERE rn = 1
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
        victimas_clasificadas AS (
            SELECT
                CASE
                    WHEN va.identificador_victima_fiscalia IS NULL THEN 'NUEVO'
                    WHEN
                        ISNULL(va.id_tipo_victima, 0) <> ISNULL(vt.id_tipo_victima, 0)
                        OR ISNULL(va.id_tipo_victima_moral, 0) <> ISNULL(vt.id_tipo_victima_moral, 0)
                        OR ISNULL(va.id_sexo, 0) <> ISNULL(vt.id_sexo, 0)
                        OR ISNULL(va.id_genero, 0) <> ISNULL(vt.id_genero, 0)
                        OR ISNULL(va.id_nacionalidad, 0) <> ISNULL(vt.id_nacionalidad, 0)
                        OR ISNULL(va.id_pertenece_poblacion_indigena, 0) <> ISNULL(vt.id_pertenece_poblacion_indigena, 0)
                        OR ISNULL(va.id_presenta_discapacidad, 0) <> ISNULL(vt.id_presenta_discapacidad, 0)
                        OR ISNULL(CONVERT(varchar(10), va.fecha_nacimiento, 120), '') <> ISNULL(CONVERT(varchar(10), vt.fecha_nacimiento, 120), '')
                        OR ISNULL(va.edad, 0) <> ISNULL(vt.edad, 0)
                        THEN 'MODIFICADO'
                    ELSE 'SIN_CAMBIOS'
                END AS tipo
            FROM victimas_tmp vt
            LEFT JOIN victimas_actuales va
                ON va.id_ci = vt.id_ci
               AND va.id_delito_fiscalia = vt.id_delito
               AND va.identificador_victima_fiscalia = vt.id_vicf

            UNION ALL

            SELECT 'ELIMINADO'
            FROM victimas_actuales va
            LEFT JOIN victimas_tmp vt
                ON vt.id_ci = va.id_ci
               AND vt.id_delito = va.id_delito_fiscalia
               AND vt.id_vicf = va.identificador_victima_fiscalia
            WHERE vt.id_vicf IS NULL
        )
        SELECT 'carpetas' AS Archivo, tipo AS Tipo, COUNT(1) AS Total
        FROM carpetas_clasificadas
        GROUP BY tipo

        UNION ALL

        SELECT 'delitos' AS Archivo, tipo AS Tipo, COUNT(1) AS Total
        FROM delitos_clasificados
        GROUP BY tipo

        UNION ALL

        SELECT 'victimas' AS Archivo, tipo AS Tipo, COUNT(1) AS Total
        FROM victimas_clasificadas
        GROUP BY tipo;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var resultados = await connection.QueryAsync<(string Archivo, string Tipo, int Total)>(
            sql,
            new
            {
                IdCargaActualizacion = idCargaActualizacion
            });

        var resumen = new List<CargaValidacionResumenItem>();

        foreach (var item in resultados)
        {
            resumen.Add(new CargaValidacionResumenItem
            {
                Archivo = item.Archivo,
                Codigo = $"ACTUALIZACION_{item.Archivo.ToUpperInvariant()}_{item.Tipo}",
                Descripcion = item.Tipo switch
                {
                    "NUEVO" => $"Registros nuevos en {item.Archivo}.",
                    "MODIFICADO" => $"Registros modificados en {item.Archivo}.",
                    "ELIMINADO" => $"Registros eliminados en {item.Archivo}.",
                    "SIN_CAMBIOS" => $"Registros sin cambios en {item.Archivo}.",
                    _ => $"Registros {item.Tipo} en {item.Archivo}."
                },
                TotalRegistros = item.Total,
                EsError = false
            });
        }

        return resumen;
    }

    public async Task<ActualizacionDiferenciasResponse?> ObtenerDetalleDiferenciasActualizacionAsync(string codigoReferencia, int? idEntidadFederativaUsuario, bool esSuperUsuario)
    {
        // Devuelve el detalle de diferencias de una actualización pendiente.
        // Incluye carpetas, delitos y víctimas.
        //
        // Para cada registro modificado regresa los campos que cambiaron.
        // Para nuevos/eliminados regresa el identificador de fiscalía.

        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT TOP 1
                id_carga,
                codigo_referencia,
                id_entidad_federativa,
                mes_corte,
                anio_corte
            FROM carga
            WHERE codigo_referencia = @CodigoReferencia
              AND tipo_carga = 'ACTUALIZACION'
              AND estado = 'VALIDADO_PENDIENTE_ACTUALIZACION'
              AND activo = 1
              AND (
                    @EsSuperUsuario = 1
                    OR id_entidad_federativa = @IdEntidadFederativaUsuario
                  )
        ),
        cargas_periodo AS (
            SELECT
                c.id_carga,
                c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        carpetas_actuales_base AS (
            SELECT
                ci.identificador_carpeta_fiscalia,
                ci.nomenclatura_carpeta_fiscalia,
                ci.fecha_inicio,
                ci.resumen_hechos,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, ci.id_carga DESC, ci.id_carpeta_investigacion DESC
                ) AS rn
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
        ),
        carpetas_actuales AS (
            SELECT
                identificador_carpeta_fiscalia,
                nomenclatura_carpeta_fiscalia,
                fecha_inicio,
                resumen_hechos
            FROM carpetas_actuales_base
            WHERE rn = 1
        ),
        carpetas_tmp AS (
            SELECT
                c.id_ci,
                c.ntra_ci,
                COALESCE(
                    TRY_CONVERT(datetime2, c.fha_de_ini, 103),
                    TRY_CONVERT(datetime2, c.fha_de_ini)
                ) AS fecha_inicio,
                c.rmen_de_hchos
            FROM carga_tmp_carpeta c
            INNER JOIN carga_actualizacion ca
                ON ca.id_carga = c.id_carga
            WHERE c.activo = 1
        ),
        delitos_actuales_base AS (
            SELECT
                ci.identificador_carpeta_fiscalia AS id_ci,
                d.identificador_delito_fiscalia,
                d.delito_fiscalia,
                d.modalidad_delito_fiscalia,
                d.id_forma_accion,
                d.fecha_hechos,
                d.id_instrumento_comision,
                d.id_grado_consumacion,
                d.id_modalidad_delito,
                d.id_entidad_federativa,
                d.id_municipio,
                d.id_localidad_fiscalia,
                d.localidad_fiscalia_nombre,
                d.id_colonia_fiscalia,
                d.colonia_fiscalia_nombre,
                d.id_codigo_postal,
                d.coordenada_x,
                d.coordenada_y,
                d.domicilio_hechos,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, d.id_carga DESC, d.id_delito DESC
                ) AS rn
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            WHERE d.activo = 1
        ),
        delitos_actuales AS (
            SELECT
                id_ci,
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
                domicilio_hechos
            FROM delitos_actuales_base
            WHERE rn = 1
        ),
        delitos_tmp AS (
            SELECT
                d.id_ci,
                d.id_delito,
                d.dto,
                d.moda_dto,
                fa.id_forma_accion,
                COALESCE(
                    TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(d.hra_de_hchos, '')), 103),
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
            INNER JOIN carga_actualizacion ca
                ON ca.id_carga = d.id_carga
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
            WHERE d.activo = 1
        ),
        victimas_actuales_base AS (
            SELECT
                ci.identificador_carpeta_fiscalia AS id_ci,
                d.identificador_delito_fiscalia AS id_delito_fiscalia,
                v.identificador_victima_fiscalia,
                v.id_tipo_victima,
                v.id_tipo_victima_moral,
                v.id_sexo,
                v.id_genero,
                v.id_nacionalidad,
                v.id_pertenece_poblacion_indigena,
                v.id_presenta_discapacidad,
                v.fecha_nacimiento,
                v.edad,
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
        victimas_actuales AS (
            SELECT
                id_ci,
                id_delito_fiscalia,
                identificador_victima_fiscalia,
                id_tipo_victima,
                id_tipo_victima_moral,
                id_sexo,
                id_genero,
                id_nacionalidad,
                id_pertenece_poblacion_indigena,
                id_presenta_discapacidad,
                fecha_nacimiento,
                edad
            FROM victimas_actuales_base
            WHERE rn = 1
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
            INNER JOIN carga_actualizacion ca
                ON ca.id_carga = v.id_carga
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
            WHERE v.activo = 1
        )
        SELECT
            'carpetas' AS Seccion,
            'NUEVO' AS TipoMovimiento,
            'id_ci' AS CampoIdentificador,
            ct.id_ci AS IdentificadorFiscalia,
            NULL AS Campo,
            NULL AS ValorAnterior,
            NULL AS ValorNuevo
        FROM carpetas_tmp ct
        LEFT JOIN carpetas_actuales ca
            ON ca.identificador_carpeta_fiscalia = ct.id_ci
        WHERE ca.identificador_carpeta_fiscalia IS NULL

        UNION ALL

        SELECT
            'carpetas',
            'ELIMINADO',
            'id_ci',
            ca.identificador_carpeta_fiscalia,
            NULL,
            NULL,
            NULL
        FROM carpetas_actuales ca
        LEFT JOIN carpetas_tmp ct
            ON ct.id_ci = ca.identificador_carpeta_fiscalia
        WHERE ct.id_ci IS NULL

        UNION ALL

        SELECT
            'carpetas',
            'MODIFICADO',
            'id_ci',
            ct.id_ci,
            dif.Campo,
            dif.ValorAnterior,
            dif.ValorNuevo
        FROM carpetas_tmp ct
        INNER JOIN carpetas_actuales ca
            ON ca.identificador_carpeta_fiscalia = ct.id_ci
        CROSS APPLY (
            VALUES
                ('nomenclatura_carpeta_fiscalia', ca.nomenclatura_carpeta_fiscalia, ct.ntra_ci),
                ('fecha_inicio', CONVERT(varchar(19), ca.fecha_inicio, 120), CONVERT(varchar(19), ct.fecha_inicio, 120)),
                ('resumen_hechos', ca.resumen_hechos, ct.rmen_de_hchos)
        ) dif(Campo, ValorAnterior, ValorNuevo)
        WHERE ISNULL(dif.ValorAnterior, '') <> ISNULL(dif.ValorNuevo, '')

        UNION ALL

        SELECT
            'delitos',
            'NUEVO',
            'id_ci + id_delito',
            CONCAT(dt.id_ci, ' | ', dt.id_delito),
            NULL,
            NULL,
            NULL
        FROM delitos_tmp dt
        LEFT JOIN delitos_actuales da
            ON da.id_ci = dt.id_ci
           AND da.identificador_delito_fiscalia = dt.id_delito
        WHERE da.identificador_delito_fiscalia IS NULL

        UNION ALL

        SELECT
            'delitos',
            'ELIMINADO',
            'id_ci + id_delito',
            CONCAT(da.id_ci, ' | ', da.identificador_delito_fiscalia),
            NULL,
            NULL,
            NULL
        FROM delitos_actuales da
        LEFT JOIN delitos_tmp dt
            ON dt.id_ci = da.id_ci
           AND dt.id_delito = da.identificador_delito_fiscalia
        WHERE dt.id_delito IS NULL

        UNION ALL

        SELECT
            'delitos',
            'MODIFICADO',
            'id_ci + id_delito',
            CONCAT(dt.id_ci, ' | ', dt.id_delito),
            dif.Campo,
            dif.ValorAnterior,
            dif.ValorNuevo
        FROM delitos_tmp dt
        INNER JOIN delitos_actuales da
            ON da.id_ci = dt.id_ci
           AND da.identificador_delito_fiscalia = dt.id_delito
        CROSS APPLY (
            VALUES
                ('delito_fiscalia', da.delito_fiscalia, dt.dto),
                ('modalidad_delito_fiscalia', da.modalidad_delito_fiscalia, dt.moda_dto),
                ('id_forma_accion', CONVERT(varchar(50), da.id_forma_accion), CONVERT(varchar(50), dt.id_forma_accion)),
                ('fecha_hechos', CONVERT(varchar(19), da.fecha_hechos, 120), CONVERT(varchar(19), dt.fecha_hechos, 120)),
                ('id_instrumento_comision', CONVERT(varchar(50), da.id_instrumento_comision), CONVERT(varchar(50), dt.id_instrumento_comision)),
                ('id_grado_consumacion', CONVERT(varchar(50), da.id_grado_consumacion), CONVERT(varchar(50), dt.id_grado_consumacion)),
                ('id_modalidad_delito', CONVERT(varchar(50), da.id_modalidad_delito), CONVERT(varchar(50), dt.id_modalidad_delito)),
                ('id_entidad_federativa', CONVERT(varchar(50), da.id_entidad_federativa), CONVERT(varchar(50), dt.id_entidad_federativa)),
                ('id_municipio', CONVERT(varchar(50), da.id_municipio), CONVERT(varchar(50), dt.id_municipio)),
                ('id_localidad_fiscalia', da.id_localidad_fiscalia, dt.id_loc_hchos),
                ('localidad_fiscalia_nombre', da.localidad_fiscalia_nombre, dt.nom_loc_hchos),
                ('id_colonia_fiscalia', da.id_colonia_fiscalia, dt.id_col_hchos),
                ('colonia_fiscalia_nombre', da.colonia_fiscalia_nombre, dt.nom_col_hchos),
                ('id_codigo_postal', CONVERT(varchar(50), da.id_codigo_postal), CONVERT(varchar(50), dt.id_codigo_postal)),
                ('coordenada_x', CONVERT(varchar(50), da.coordenada_x), CONVERT(varchar(50), dt.coordenada_x)),
                ('coordenada_y', CONVERT(varchar(50), da.coordenada_y), CONVERT(varchar(50), dt.coordenada_y)),
                ('domicilio_hechos', da.domicilio_hechos, dt.dom_hchos)
        ) dif(Campo, ValorAnterior, ValorNuevo)
        WHERE ISNULL(dif.ValorAnterior, '') <> ISNULL(dif.ValorNuevo, '')

        UNION ALL

        SELECT
            'victimas',
            'NUEVO',
            'id_ci + id_delito + id_vicf',
            CONCAT(vt.id_ci, ' | ', vt.id_delito, ' | ', vt.id_vicf),
            NULL,
            NULL,
            NULL
        FROM victimas_tmp vt
        LEFT JOIN victimas_actuales va
            ON va.id_ci = vt.id_ci
           AND va.id_delito_fiscalia = vt.id_delito
           AND va.identificador_victima_fiscalia = vt.id_vicf
        WHERE va.identificador_victima_fiscalia IS NULL

        UNION ALL

        SELECT
            'victimas',
            'ELIMINADO',
            'id_ci + id_delito + id_vicf',
            CONCAT(va.id_ci, ' | ', va.id_delito_fiscalia, ' | ', va.identificador_victima_fiscalia),
            NULL,
            NULL,
            NULL
        FROM victimas_actuales va
        LEFT JOIN victimas_tmp vt
            ON vt.id_ci = va.id_ci
           AND vt.id_delito = va.id_delito_fiscalia
           AND vt.id_vicf = va.identificador_victima_fiscalia
        WHERE vt.id_vicf IS NULL

        UNION ALL

        SELECT
            'victimas',
            'MODIFICADO',
            'id_ci + id_delito + id_vicf',
            CONCAT(vt.id_ci, ' | ', vt.id_delito, ' | ', vt.id_vicf),
            dif.Campo,
            dif.ValorAnterior,
            dif.ValorNuevo
        FROM victimas_tmp vt
        INNER JOIN victimas_actuales va
            ON va.id_ci = vt.id_ci
           AND va.id_delito_fiscalia = vt.id_delito
           AND va.identificador_victima_fiscalia = vt.id_vicf
        CROSS APPLY (
            VALUES
                ('id_tipo_victima', CONVERT(varchar(50), va.id_tipo_victima), CONVERT(varchar(50), vt.id_tipo_victima)),
                ('id_tipo_victima_moral', CONVERT(varchar(50), va.id_tipo_victima_moral), CONVERT(varchar(50), vt.id_tipo_victima_moral)),
                ('id_sexo', CONVERT(varchar(50), va.id_sexo), CONVERT(varchar(50), vt.id_sexo)),
                ('id_genero', CONVERT(varchar(50), va.id_genero), CONVERT(varchar(50), vt.id_genero)),
                ('id_nacionalidad', CONVERT(varchar(50), va.id_nacionalidad), CONVERT(varchar(50), vt.id_nacionalidad)),
                ('id_pertenece_poblacion_indigena', CONVERT(varchar(50), va.id_pertenece_poblacion_indigena), CONVERT(varchar(50), vt.id_pertenece_poblacion_indigena)),
                ('id_presenta_discapacidad', CONVERT(varchar(50), va.id_presenta_discapacidad), CONVERT(varchar(50), vt.id_presenta_discapacidad)),
                ('fecha_nacimiento', CONVERT(varchar(10), va.fecha_nacimiento, 120), CONVERT(varchar(10), vt.fecha_nacimiento, 120)),
                ('edad', CONVERT(varchar(50), va.edad), CONVERT(varchar(50), vt.edad))
        ) dif(Campo, ValorAnterior, ValorNuevo)
        WHERE ISNULL(dif.ValorAnterior, '') <> ISNULL(dif.ValorNuevo, '');
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var filas = (await connection.QueryAsync<ActualizacionDiferenciaRow>(
            sql,
            new
            {
                CodigoReferencia = codigoReferencia,
                IdEntidadFederativaUsuario = idEntidadFederativaUsuario,
                EsSuperUsuario = esSuperUsuario
            })).ToList();

        var response = new ActualizacionDiferenciasResponse
        {
            EsValido = true,
            CodigoReferencia = codigoReferencia,
            Mensaje = filas.Count == 0
                ? "No se encontraron diferencias detalladas para la actualización."
                : "Detalle de diferencias obtenido correctamente."
        };

        AgregarDiferenciasAlResponse(filas, "carpetas", response.Carpetas);
        AgregarDiferenciasAlResponse(filas, "delitos", response.Delitos);
        AgregarDiferenciasAlResponse(filas, "victimas", response.Victimas);

        return response;
    }

    private static void AgregarDiferenciasAlResponse(List<ActualizacionDiferenciaRow> filas, string seccion, List<ActualizacionDiferenciaRegistro> destino)
    {
        var grupos = filas
            .Where(x => x.Seccion == seccion)
            .GroupBy(x => new
            {
                x.TipoMovimiento,
                x.CampoIdentificador,
                x.IdentificadorFiscalia
            });

        foreach (var grupo in grupos)
        {
            var registro = new ActualizacionDiferenciaRegistro
            {
                TipoMovimiento = grupo.Key.TipoMovimiento,
                CampoIdentificador = grupo.Key.CampoIdentificador,
                IdentificadorFiscalia = grupo.Key.IdentificadorFiscalia
            };

            foreach (var campo in grupo.Where(x => !string.IsNullOrWhiteSpace(x.Campo)))
            {
                registro.CamposModificados.Add(new ActualizacionCampoDiferencia
                {
                    Campo = campo.Campo!,
                    ValorAnterior = campo.ValorAnterior,
                    ValorNuevo = campo.ValorNuevo
                });
            }

            destino.Add(registro);
        }
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

            await AplicarActualizacionCarpetasAsync(
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

    private async Task AplicarActualizacionCarpetasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
    {
        await InsertarHistoricoCarpetasModificadasAsync(
            connection,
            transaction,
            idCargaActualizacion,
            idUsuarioConfirmacion);

        await ActualizarCarpetasModificadasAsync(
            connection,
            transaction,
            idCargaActualizacion);

        await InsertarCarpetasNuevasActualizacionAsync(
            connection,
            transaction,
            idCargaActualizacion,
            idUsuarioConfirmacion);

        await InsertarHistoricoCarpetasEliminadasAsync(
            connection,
            transaction,
            idCargaActualizacion,
            idUsuarioConfirmacion);

        await DesactivarCarpetasEliminadasAsync(
            connection,
            transaction,
            idCargaActualizacion);
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
                    OR ISNULL(CONVERT(varchar(19), ca.fecha_inicio, 120), '') <> ISNULL(CONVERT(varchar(19), COALESCE(TRY_CONVERT(datetime2, ct.fha_de_ini, 103), TRY_CONVERT(datetime2, ct.fha_de_ini)), 120), '')
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
                OR ISNULL(CONVERT(varchar(19), ci.fecha_inicio, 120), '') <> ISNULL(CONVERT(varchar(19), COALESCE(TRY_CONVERT(datetime2, ct.fha_de_ini, 103), TRY_CONVERT(datetime2, ct.fha_de_ini)), 120), '')
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
                INNER JOIN cargas_periodo cp
                    ON cp.id_carga = ci.id_carga
                WHERE ci.identificador_carpeta_fiscalia = ct.id_ci
                  AND ci.activo = 1
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