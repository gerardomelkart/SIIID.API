using System.Data;
using Dapper;
using MySqlConnector;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class CargaRepository : ICargaRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CargaRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<long> CrearCargaAsync(int idUsuarioCarga, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError)
    {
        // Crea el intento de carga.
        var sql = @"
            INSERT INTO carga (
                id_usuario_carga,
                codigo_referencia,
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
            VALUES (
                @IdUsuarioCarga,
                @CodigoReferencia,
                @MesCorte,
                @AnioCorte,
                @TotalCarpetas,
                @TotalDelitos,
                @TotalVictimas,
                @Estado,
                CURRENT_TIMESTAMP,
                DATE_ADD(CURRENT_TIMESTAMP, INTERVAL 48 HOUR),
                @MensajeError,
                1
            );

            SELECT LAST_INSERT_ID();
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var idCarga = await connection.ExecuteScalarAsync<long>(sql, new
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
        });

        return idCarga;
    }

    public async Task GuardarTmpCarpetasAsync(long idCarga, List<ArchivoFila> filasCarpetas)
    {
        // Guarda las carpetas leídas en staging usando carga masiva.
        // Esto evita insertar fila por fila.
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

        using var connection = (MySqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        var bulkCopy = new MySqlBulkCopy(connection)
        {
            DestinationTableName = "carga_tmp_carpeta"
        };

        // El primer valor es el índice de la columna en el DataTable.
        // El segundo valor es el nombre de la columna destino en MySQL.
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(0, "id_carga"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(1, "numero_fila"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(2, "id_ci"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(3, "ntra_ci"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(4, "fha_de_ini"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(5, "hra_de_ini"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(6, "rmen_de_hchos"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(7, "estado"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(8, "activo"));

        await bulkCopy.WriteToServerAsync(tabla);
    }

    public async Task GuardarTmpDelitosAsync(long idCarga, List<ArchivoFila> filasDelitos)
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

        using var connection = (MySqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        var bulkCopy = new MySqlBulkCopy(connection)
        {
            DestinationTableName = "carga_tmp_delito"
        };

        // El primer valor es el índice de la columna en el DataTable.
        // El segundo valor es el nombre de la columna destino en MySQL.
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(0, "id_carga"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(1, "numero_fila"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(2, "id_ci"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(3, "id_delito"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(4, "dto"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(5, "moda_dto"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(6, "forma_acc"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(7, "fha_de_hchos"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(8, "hra_de_hchos"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(9, "emto_com_dto"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(10, "grdo_cons"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(11, "clasf_de_dto"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(12, "id_ent_hchos"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(13, "id_mun_hchos"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(14, "id_loc_hchos"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(15, "nom_loc_hchos"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(16, "id_col_hchos"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(17, "nom_col_hchos"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(18, "cp"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(19, "coord_x"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(20, "coord_y"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(21, "dom_hchos"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(22, "estado"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(23, "activo"));

        await bulkCopy.WriteToServerAsync(tabla);
    }

    public async Task GuardarTmpVictimasAsync(long idCarga, List<ArchivoFila> filasVictimas)
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

        using var connection = (MySqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        var bulkCopy = new MySqlBulkCopy(connection)
        {
            DestinationTableName = "carga_tmp_victima"
        };

        // El primer valor es el índice de la columna en el DataTable.
        // El segundo valor es el nombre de la columna destino en MySQL.
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(0, "id_carga"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(1, "numero_fila"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(2, "id_ci"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(3, "id_delito"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(4, "id_vicf"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(5, "id_tv"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(6, "id_tpm"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(7, "sexo"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(8, "genero"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(9, "pob"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(10, "disc"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(11, "fha_nac"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(12, "edad"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(13, "nacional"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(14, "estado"));
        bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(15, "activo"));

        await bulkCopy.WriteToServerAsync(tabla);
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
}