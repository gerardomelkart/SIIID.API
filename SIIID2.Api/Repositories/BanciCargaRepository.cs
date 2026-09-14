using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class BanciCargaRepository : IBanciCargaRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public BanciCargaRepository(
        IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory =
            dbConnectionFactory;
    }

    public async Task<BanciUsuarioCargaInfo?> ObtenerUsuarioCargaAsync(
        int idUsuario)
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
                ON m.clave = N'BANCI'
               AND m.activo = 1

            INNER JOIN dbo.usuario_modulo um
                ON um.id_usuario = u.id_usuario
               AND um.id_modulo = m.id_modulo
               AND um.habilitado = 1
               AND um.activo = 1

            WHERE u.id_usuario = @IdUsuario
              AND u.activo = 1;
            """;

        using var connection =
            _dbConnectionFactory.CrearConexion();

        return await connection
            .QueryFirstOrDefaultAsync<BanciUsuarioCargaInfo>(
                sql,
                new
                {
                    IdUsuario = idUsuario
                });
    }

    public async Task<int?> ResolverEntidadFederativaAsync(
        string valor)
    {
        const string sql = """
            DECLARE @Valor nvarchar(250) =
                UPPER(LTRIM(RTRIM(@ValorOriginal)));

            DECLARE @ValorSinPunto nvarchar(250) =
                REPLACE(@Valor, N'.', N' ');

            SELECT TOP (1)
                CONVERT(int, ef.id_entidad_federativa)
            FROM dbo.catalogo_entidad_federativa ef
            WHERE ef.activo = 1
              AND
              (
                    UPPER(LTRIM(RTRIM(ef.nombre))) = @Valor
                 OR UPPER(LTRIM(RTRIM(ef.siglas))) = @Valor
                 OR UPPER(LTRIM(RTRIM(ef.siglas_renapo))) = @Valor
                 OR UPPER(LTRIM(RTRIM(ef.clave))) = @Valor
                 OR ef.id_entidad_federativa = TRY_CONVERT(tinyint, @Valor)
                 OR @ValorSinPunto LIKE UPPER(LTRIM(RTRIM(ef.clave))) + N' %'
              )
            ORDER BY
                CASE
                    WHEN UPPER(LTRIM(RTRIM(ef.nombre))) = @Valor THEN 1
                    WHEN UPPER(LTRIM(RTRIM(ef.siglas))) = @Valor THEN 2
                    WHEN UPPER(LTRIM(RTRIM(ef.siglas_renapo))) = @Valor THEN 3
                    WHEN UPPER(LTRIM(RTRIM(ef.clave))) = @Valor THEN 4
                    WHEN ef.id_entidad_federativa = TRY_CONVERT(tinyint, @Valor) THEN 5
                    ELSE 6
                END;
            """;

        using var connection =
            _dbConnectionFactory.CrearConexion();

        return await connection
            .ExecuteScalarAsync<int?>(
                sql,
                new
                {
                    ValorOriginal = valor
                });
    }

    public async Task<long> GuardarCargaValidadaAsync(
        int idUsuarioCarga,
        int idEntidadFederativa,
        string codigoReferencia,
        string modalidadIngreso,
        BanciLecturaArchivosResultado lectura,
        BanciCargaValidacionResponse validacion)
    {
        using var connection =
            (SqlConnection)_dbConnectionFactory
                .CrearConexion();

        await connection.OpenAsync();

        using var transaction =
            (SqlTransaction)await connection
                .BeginTransactionAsync();

        try
        {
            var idBanciCarga =
                await CrearCargaAsync(
                    connection,
                    transaction,
                    idUsuarioCarga,
                    idEntidadFederativa,
                    codigoReferencia,
                    modalidadIngreso,
                    lectura,
                    validacion);

            await GuardarCarpetasAsync(
                connection,
                transaction,
                idBanciCarga,
                lectura.Carpetas);

            await GuardarDelitosAsync(
                connection,
                transaction,
                idBanciCarga,
                lectura.Delitos);

            await GuardarVictimasAsync(
                connection,
                transaction,
                idBanciCarga,
                lectura.Victimas);

            await GuardarObservacionesAsync(
                connection,
                transaction,
                idBanciCarga,
                validacion.Advertencias);

            await FinalizarCargaAsync(
                connection,
                transaction,
                idBanciCarga);

            await transaction.CommitAsync();

            return idBanciCarga;
        }
        catch
        {
            if (transaction.Connection != null)
            {
                await transaction.RollbackAsync();
            }

            throw;
        }
    }

    private static async Task<long> CrearCargaAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int idUsuarioCarga,
        int idEntidadFederativa,
        string codigoReferencia,
        string modalidadIngreso,
        BanciLecturaArchivosResultado lectura,
        BanciCargaValidacionResponse validacion)
    {
        const string sql = """
            INSERT INTO dbo.banci_carga
            (
                id_usuario_carga,
                id_entidad_federativa,
                codigo_referencia,
                modalidad_ingesta,
                fecha_inicio_procesamiento,
                total_carpetas,
                total_delitos,
                total_victimas,
                total_altas,
                total_actualizaciones,
                total_sin_cambio,
                total_errores,
                total_advertencias,
                estado,
                mensaje_error,
                activo
            )
            OUTPUT INSERTED.id_banci_carga
            VALUES
            (
                @IdUsuarioCarga,
                @IdEntidadFederativa,
                @CodigoReferencia,
                @ModalidadIngreso,
                SYSDATETIME(),
                @TotalCarpetas,
                @TotalDelitos,
                @TotalVictimas,
                0,
                0,
                0,
                0,
                @TotalAdvertencias,
                N'PROCESANDO',
                NULL,
                1
            );
            """;

        return await connection
            .ExecuteScalarAsync<long>(
                sql,
                new
                {
                    IdUsuarioCarga = idUsuarioCarga,
                    IdEntidadFederativa = idEntidadFederativa,
                    CodigoReferencia = codigoReferencia,
                    ModalidadIngreso = modalidadIngreso,
                    TotalCarpetas = lectura.Carpetas.Count,
                    TotalDelitos = lectura.Delitos.Count,
                    TotalVictimas = lectura.Victimas.Count,
                    TotalAdvertencias = validacion.Advertencias.Count
                },
                transaction);
    }

    private static async Task GuardarCarpetasAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long idBanciCarga,
        IReadOnlyCollection<ArchivoFila> filas)
    {
        if (filas.Count == 0)
        {
            return;
        }

        var tabla = new DataTable();

        tabla.Columns.Add(
            "id_banci_carga",
            typeof(long));

        tabla.Columns.Add(
            "id_banci_carga_archivo",
            typeof(long));

        tabla.Columns.Add(
            "nombre_hoja",
            typeof(string));

        tabla.Columns.Add(
            "numero_fila",
            typeof(int));

        tabla.Columns.Add("entidad", typeof(string));
        tabla.Columns.Add("id_ci", typeof(string));
        tabla.Columns.Add("ntra_ci", typeof(string));
        tabla.Columns.Add("fha_de_ini", typeof(string));
        tabla.Columns.Add("hra_de_ini", typeof(string));
        tabla.Columns.Add("rmen_de_hchos", typeof(string));
        tabla.Columns.Add("ord_apreh", typeof(string));
        tabla.Columns.Add("fgran", typeof(string));
        tabla.Columns.Add("ctaon", typeof(string));
        tabla.Columns.Add("td_v_ap", typeof(string));
        tabla.Columns.Add("proc_abrev", typeof(string));
        tabla.Columns.Add("juc_oral", typeof(string));
        tabla.Columns.Add("td_sen_con", typeof(string));
        tabla.Columns.Add("no_ejer_acc_pnal", typeof(string));
        tabla.Columns.Add("otra", typeof(string));
        tabla.Columns.Add("dic", typeof(string));

        tabla.Columns.Add(
            "estado",
            typeof(string));

        tabla.Columns.Add(
            "fecha_procesamiento",
            typeof(DateTime));

        tabla.Columns.Add(
            "activo",
            typeof(bool));

        foreach (var fila in filas)
        {
            tabla.Rows.Add(
                idBanciCarga,
                DBNull.Value,
                DBNull.Value,
                fila.NumeroFila ?? 0,
                Db(Valor(fila, "entidad")),
                Db(Valor(fila, "id_ci")),
                Db(Valor(fila, "ntra_ci")),
                Db(Valor(fila, "fha_de_ini")),
                Db(Valor(fila, "hra_de_ini")),
                Db(Valor(fila, "rmen_de_hchos")),
                Db(Valor(fila, "ord_apreh")),
                Db(Valor(fila, "fgran")),
                Db(Valor(fila, "ctaon")),
                Db(Valor(fila, "td_v_ap")),
                Db(Valor(fila, "proc_abrev")),
                Db(Valor(fila, "juc_oral")),
                Db(Valor(fila, "td_sen_con")),
                Db(Valor(fila, "no_ejer_acc_pnal")),
                Db(Valor(fila, "otra")),
                Db(Valor(fila, "dic")),
                "PENDIENTE",
                DBNull.Value,
                true);
        }

        await BulkCopyAsync(
            connection,
            transaction,
            tabla,
            "dbo.banci_carga_tmp_carpeta");
    }

    private static async Task GuardarDelitosAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long idBanciCarga,
        IReadOnlyCollection<ArchivoFila> filas)
    {
        if (filas.Count == 0)
        {
            return;
        }

        var tabla = new DataTable();

        tabla.Columns.Add(
            "id_banci_carga",
            typeof(long));

        tabla.Columns.Add(
            "id_banci_carga_archivo",
            typeof(long));

        tabla.Columns.Add(
            "nombre_hoja",
            typeof(string));

        tabla.Columns.Add(
            "numero_fila",
            typeof(int));

        tabla.Columns.Add("entidad", typeof(string));
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
        tabla.Columns.Add("nom_ent_hchos", typeof(string));
        tabla.Columns.Add("id_ent_hchos", typeof(string));
        tabla.Columns.Add("nom_mun_hchos", typeof(string));
        tabla.Columns.Add("id_mun_hchos", typeof(string));
        tabla.Columns.Add("nom_loc_hchos", typeof(string));
        tabla.Columns.Add("id_loc_hchos", typeof(string));
        tabla.Columns.Add("nom_col_hchos", typeof(string));
        tabla.Columns.Add("id_col_hchos", typeof(string));
        tabla.Columns.Add("cp", typeof(string));
        tabla.Columns.Add("coord_x", typeof(string));
        tabla.Columns.Add("coord_y", typeof(string));
        tabla.Columns.Add("dom_hchos", typeof(string));

        tabla.Columns.Add(
            "estado",
            typeof(string));

        tabla.Columns.Add(
            "fecha_procesamiento",
            typeof(DateTime));

        tabla.Columns.Add(
            "activo",
            typeof(bool));

        foreach (var fila in filas)
        {
            tabla.Rows.Add(
                idBanciCarga,
                DBNull.Value,
                DBNull.Value,
                fila.NumeroFila ?? 0,
                Db(Valor(fila, "entidad")),
                Db(Valor(fila, "id_ci")),
                Db(Valor(fila, "id_delito")),
                Db(Valor(fila, "dto")),
                Db(Valor(fila, "moda_dto")),
                Db(Valor(fila, "forma_acc")),
                Db(Valor(fila, "fha_de_hchos")),
                Db(Valor(fila, "hra_de_hchos")),
                Db(Valor(fila, "emto_com_dto")),
                Db(Valor(fila, "grdo_cons")),
                Db(Valor(fila, "clasf_de_dto")),
                Db(Valor(fila, "nom_ent_hchos")),
                Db(Valor(fila, "id_ent_hchos")),
                Db(Valor(fila, "nom_mun_hchos")),
                Db(Valor(fila, "id_mun_hchos")),
                Db(Valor(fila, "nom_loc_hchos")),
                Db(Valor(fila, "id_loc_hchos")),
                Db(Valor(fila, "nom_col_hchos")),
                Db(Valor(fila, "id_col_hchos")),
                Db(Valor(fila, "cp")),
                Db(Valor(fila, "coord_x")),
                Db(Valor(fila, "coord_y")),
                Db(Valor(fila, "dom_hchos")),
                "PENDIENTE",
                DBNull.Value,
                true);
        }

        await BulkCopyAsync(
            connection,
            transaction,
            tabla,
            "dbo.banci_carga_tmp_delito");
    }

    private static async Task GuardarVictimasAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long idBanciCarga,
        IReadOnlyCollection<ArchivoFila> filas)
    {
        if (filas.Count == 0)
        {
            return;
        }

        var tabla = new DataTable();

        tabla.Columns.Add(
            "id_banci_carga",
            typeof(long));

        tabla.Columns.Add(
            "id_banci_carga_archivo",
            typeof(long));

        tabla.Columns.Add(
            "nombre_hoja",
            typeof(string));

        tabla.Columns.Add(
            "numero_fila",
            typeof(int));

        tabla.Columns.Add("entidad", typeof(string));
        tabla.Columns.Add("id_ci", typeof(string));
        tabla.Columns.Add("id_delito", typeof(string));
        tabla.Columns.Add("id_vicf", typeof(string));
        tabla.Columns.Add("sexo", typeof(string));
        tabla.Columns.Add("genero", typeof(string));
        tabla.Columns.Add("pob", typeof(string));
        tabla.Columns.Add("disc", typeof(string));
        tabla.Columns.Add("fha_nac", typeof(string));
        tabla.Columns.Add("edad", typeof(string));
        tabla.Columns.Add("nacional", typeof(string));
        tabla.Columns.Add("no_banci", typeof(string));
        tabla.Columns.Add("folio_fotovolante", typeof(string));
        tabla.Columns.Add("folio_rnpdno", typeof(string));
        tabla.Columns.Add("pro_apellido", typeof(string));
        tabla.Columns.Add("sdo_apellido", typeof(string));
        tabla.Columns.Add("nomb", typeof(string));
        tabla.Columns.Add("entidad_nacimiento", typeof(string));
        tabla.Columns.Add("estado_migratorio", typeof(string));
        tabla.Columns.Add("curp", typeof(string));
        tabla.Columns.Add("rfc", typeof(string));
        tabla.Columns.Add("fecha_ultimo_contacto", typeof(string));
        tabla.Columns.Add("hora_ultimo_contacto", typeof(string));
        tabla.Columns.Add("entidad_visto", typeof(string));
        tabla.Columns.Add("municipio_visto", typeof(string));
        tabla.Columns.Add("lugar_ultimo_contacto", typeof(string));
        tabla.Columns.Add("senas_tatuaje_datos_identificacion", typeof(string));
        tabla.Columns.Add("localizado_o_no_localizado", typeof(string));
        tabla.Columns.Add("con_o_sin_vida", typeof(string));
        tabla.Columns.Add("fecha_localizacion", typeof(string));
        tabla.Columns.Add("voluntaria", typeof(string));
        tabla.Columns.Add("fue_delito", typeof(string));
        tabla.Columns.Add("delito", typeof(string));
        tabla.Columns.Add("obs", typeof(string));

        tabla.Columns.Add(
            "estado",
            typeof(string));

        tabla.Columns.Add(
            "fecha_procesamiento",
            typeof(DateTime));

        tabla.Columns.Add(
            "activo",
            typeof(bool));

        foreach (var fila in filas)
        {
            tabla.Rows.Add(
                idBanciCarga,
                DBNull.Value,
                DBNull.Value,
                fila.NumeroFila ?? 0,
                Db(Valor(fila, "entidad")),
                Db(Valor(fila, "id_ci")),
                Db(Valor(fila, "id_delito")),
                Db(Valor(fila, "id_vicf")),
                Db(Valor(fila, "sexo")),
                Db(Valor(fila, "genero")),
                Db(Valor(fila, "pob")),
                Db(Valor(fila, "disc")),
                Db(Valor(fila, "fha_nac")),
                Db(Valor(fila, "edad")),
                Db(Valor(fila, "nacional")),
                Db(Valor(fila, "no_banci")),
                Db(Valor(fila, "folio_fotovolante")),
                Db(Valor(fila, "folio_rnpdno")),
                Db(Valor(fila, "pro_apellido")),
                Db(Valor(fila, "sdo_apellido")),
                Db(Valor(fila, "nomb")),
                Db(Valor(fila, "entidad_nacimiento")),
                Db(Valor(fila, "estado_migratorio")),
                Db(Valor(fila, "curp")),
                Db(Valor(fila, "rfc")),
                Db(Valor(fila, "fecha_ultimo_contacto")),
                Db(Valor(fila, "hora_ultimo_contacto")),
                Db(Valor(fila, "entidad_visto")),
                Db(Valor(fila, "municipio_visto")),
                Db(Valor(fila, "lugar_ultimo_contacto")),
                Db(Valor(fila, "senas_tatuaje_datos_identificacion")),
                Db(Valor(fila, "localizado_o_no_localizado")),
                Db(Valor(fila, "con_o_sin_vida")),
                Db(Valor(fila, "fecha_localizacion")),
                Db(Valor(fila, "voluntaria")),
                Db(Valor(fila, "fue_delito")),
                Db(Valor(fila, "delito")),
                Db(Valor(fila, "obs")),
                "PENDIENTE",
                DBNull.Value,
                true);
        }

        await BulkCopyAsync(
            connection,
            transaction,
            tabla,
            "dbo.banci_carga_tmp_victima");
    }

    private static async Task GuardarObservacionesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long idBanciCarga,
        IReadOnlyCollection<BanciCargaValidacionError> observaciones)
    {
        if (observaciones.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO dbo.banci_carga_observacion
            (
                id_banci_carga,
                severidad,
                tipo_registro,
                numero_fila,
                campo,
                valor,
                codigo,
                mensaje,
                activo
            )
            VALUES
            (
                @IdBanciCarga,
                N'ADVERTENCIA',
                @TipoRegistro,
                @NumeroFila,
                @Campo,
                @Valor,
                @Codigo,
                @Mensaje,
                1
            );
            """;

        foreach (var observacion in observaciones)
        {
            await connection.ExecuteAsync(
                sql,
                new
                {
                    IdBanciCarga = idBanciCarga,
                    TipoRegistro = ObtenerTipoRegistro(
                        observacion.Archivo),
                    observacion.NumeroFila,
                    observacion.Campo,
                    observacion.Valor,
                    observacion.Codigo,
                    observacion.Mensaje
                },
                transaction);
        }
    }

    private static async Task FinalizarCargaAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long idBanciCarga)
    {
        const string sql = """
            UPDATE dbo.banci_carga
            SET estado = N'VALIDADO',
                fecha_fin_procesamiento = SYSDATETIME()
            WHERE id_banci_carga = @IdBanciCarga;
            """;

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdBanciCarga = idBanciCarga
            },
            transaction);
    }

    private static async Task BulkCopyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DataTable tabla,
        string tablaDestino)
    {
        using var bulk =
            new SqlBulkCopy(
                connection,
                SqlBulkCopyOptions.CheckConstraints |
                SqlBulkCopyOptions.KeepNulls |
                SqlBulkCopyOptions.TableLock,
                transaction)
            {
                DestinationTableName = tablaDestino,
                BatchSize = 5000,
                BulkCopyTimeout = 300
            };

        foreach (DataColumn columna in tabla.Columns)
        {
            bulk.ColumnMappings.Add(
                columna.ColumnName,
                columna.ColumnName);
        }

        await bulk.WriteToServerAsync(tabla);
    }

    private static string Valor(
        ArchivoFila fila,
        string columna)
    {
        return fila.Columnas.TryGetValue(
                columna,
                out var valor)
            ? valor?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static object Db(
        string valor)
    {
        return string.IsNullOrWhiteSpace(valor)
            ? DBNull.Value
            : valor;
    }

    private static string ObtenerTipoRegistro(
        string archivo)
    {
        return archivo.Trim().ToLowerInvariant() switch
        {
            "carpetas" or "carpeta" => "CARPETA",
            "delitos" or "delito" => "DELITO",
            "victimas" or "victima" => "VICTIMA",
            _ => "GENERAL"
        };
    }
}