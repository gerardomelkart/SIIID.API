using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public partial class FederalActualizacionRepository : IFederalActualizacionRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public FederalActualizacionRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<CargaPendienteInfo?> ObtenerPendienteAsync(int mesCorte, int anioCorte)
    {
        const string sql = """
            SELECT TOP (1) codigo_referencia AS CodigoReferencia, estado AS Estado
            FROM dbo.federal_carga
            WHERE id_entidad_federativa IS NULL
              AND mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND tipo_carga = N'ACTUALIZACION'
              AND estado IN (N'VALIDADO_PENDIENTE_ACTUALIZACION', N'PENDIENTE_APROBACION')
              AND activo = 1
            ORDER BY fecha_validacion DESC, id_federal_carga DESC;
            """;

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QueryFirstOrDefaultAsync<CargaPendienteInfo>(sql, new { MesCorte = mesCorte, AnioCorte = anioCorte });
    }

    public async Task<List<ActualizacionAnioDisponibleItem>> ObtenerPeriodosDisponiblesAsync()
    {
        const string sql = """
            SELECT DISTINCT anio_corte AS AnioCorte, mes_corte AS MesCorte
            FROM dbo.federal_carga
            WHERE id_entidad_federativa IS NULL
              AND tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
              AND activo = 1
            ORDER BY anio_corte DESC, mes_corte DESC;
            """;

        using var connection = _dbConnectionFactory.CrearConexion();
        var periodos = await connection.QueryAsync<(int AnioCorte, int MesCorte)>(sql);

        return periodos.GroupBy(x => x.AnioCorte).Select(anio => new ActualizacionAnioDisponibleItem
        {
            AnioCorte = anio.Key,
            Meses = anio.Select(x => new ActualizacionMesDisponibleItem { MesCorte = x.MesCorte, NombreMes = ObtenerNombreMes(x.MesCorte), Periodo = $"{x.MesCorte:00}/{x.AnioCorte}" }).ToList()
        }).ToList();
    }

    public async Task<long> GuardarIntentoAsync(int idUsuarioCarga, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError, List<CargaValidacionError> advertencias, List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas)
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
            await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(connection, transaction, idFederalCarga, null, estado, idUsuarioCarga, estado == "VALIDADO_PENDIENTE_ACTUALIZACION" ? "Actualización federal validada y pendiente de decisión del usuario." : "Intento de actualización federal registrado con errores de validación.");
            await transaction.CommitAsync();
            return idFederalCarga;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<List<CargaValidacionResumenItem>> ObtenerResumenDiferenciasAsync(long idFederalCarga)
    {
        var resultados = await ObtenerConteosDiferenciasAsync(idFederalCarga);

        return resultados.Select(x => new CargaValidacionResumenItem
        {
            Archivo = x.Archivo,
            Codigo = $"FEDERAL_ACTUALIZACION_{x.Archivo.ToUpperInvariant()}_{x.Tipo}",
            Descripcion = x.Tipo switch { "NUEVO" => $"Registros nuevos en {x.Archivo}.", "MODIFICADO" => $"Registros modificados en {x.Archivo}.", "ELIMINADO" => $"Registros eliminados en {x.Archivo}.", "SIN_CAMBIOS" => $"Registros sin cambios en {x.Archivo}.", _ => $"Registros {x.Tipo} en {x.Archivo}." },
            TotalRegistros = x.Total,
            EsError = false
        }).ToList();
    }

    private async Task<IEnumerable<(string Archivo, string Tipo, int Total)>> ObtenerConteosDiferenciasAsync(long idFederalCarga)
    {
        const string sql = """
        DECLARE @MesCorte TINYINT, @AnioCorte SMALLINT;
        SELECT @MesCorte = mes_corte, @AnioCorte = anio_corte FROM dbo.federal_carga WHERE id_federal_carga = @IdFederalCarga;

        ;WITH carpetas_actuales AS
        (
            SELECT ci.identificador_carpeta_fiscalia, ci.nomenclatura_carpeta_fiscalia, ci.fecha_inicio, ci.resumen_hechos
            FROM dbo.federal_carpeta_investigacion ci
            INNER JOIN dbo.federal_carga c ON c.id_federal_carga = ci.id_federal_carga
            WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND ci.activo = 1
        ),
        carpetas_tmp AS
        (
            SELECT id_ci, ntra_ci, COALESCE(TRY_CONVERT(datetime2, CONCAT(fha_de_ini, N' ', NULLIF(LTRIM(RTRIM(hra_de_ini)), N'')), 103), TRY_CONVERT(datetime2, CONCAT(fha_de_ini, N' ', NULLIF(LTRIM(RTRIM(hra_de_ini)), N''))), CASE WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(hra_de_ini)), N''), N',', N'.')) >= 0 AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(hra_de_ini)), N''), N',', N'.')) < 1 THEN DATEADD(SECOND, CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(hra_de_ini)), N''), N',', N'.')) * 86400, 0)), COALESCE(TRY_CONVERT(datetime2, fha_de_ini, 103), TRY_CONVERT(datetime2, fha_de_ini))) END, TRY_CONVERT(datetime2, fha_de_ini, 103), TRY_CONVERT(datetime2, fha_de_ini)) AS fecha_inicio, rmen_de_hchos
            FROM dbo.federal_carga_tmp_carpeta WHERE id_federal_carga = @IdFederalCarga AND activo = 1
        ),
        carpetas_clasificadas AS
        (
            SELECT CASE WHEN ca.identificador_carpeta_fiscalia IS NULL THEN N'NUEVO' WHEN ISNULL(ca.nomenclatura_carpeta_fiscalia, N'') <> ISNULL(ct.ntra_ci, N'') OR ISNULL(CONVERT(varchar(19), ca.fecha_inicio, 120), N'') <> ISNULL(CONVERT(varchar(19), ct.fecha_inicio, 120), N'') OR ISNULL(ca.resumen_hechos, N'') <> ISNULL(ct.rmen_de_hchos, N'') THEN N'MODIFICADO' ELSE N'SIN_CAMBIOS' END AS tipo
            FROM carpetas_tmp ct LEFT JOIN carpetas_actuales ca ON ca.identificador_carpeta_fiscalia = ct.id_ci
            UNION ALL
            SELECT N'ELIMINADO' FROM carpetas_actuales ca LEFT JOIN carpetas_tmp ct ON ct.id_ci = ca.identificador_carpeta_fiscalia WHERE ct.id_ci IS NULL
        ),
        delitos_actuales AS
        (
            SELECT ci.identificador_carpeta_fiscalia AS id_ci, d.identificador_delito_fiscalia, d.delito_fiscalia, d.modalidad_delito_fiscalia, d.id_forma_accion, d.fecha_hechos, d.id_instrumento_comision, d.id_grado_consumacion, d.id_modalidad_delito, d.id_entidad_federativa, d.id_municipio, d.id_localidad_fiscalia, d.localidad_fiscalia_nombre, d.id_colonia_fiscalia, d.colonia_fiscalia_nombre, d.id_codigo_postal, d.codigo_postal_fiscalia, d.coordenada_x, d.coordenada_y, d.domicilio_hechos
            FROM dbo.federal_delito d INNER JOIN dbo.federal_carpeta_investigacion ci ON ci.id_federal_carpeta_investigacion = d.id_federal_carpeta_investigacion AND ci.activo = 1 INNER JOIN dbo.federal_carga c ON c.id_federal_carga = d.id_federal_carga
            WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND d.activo = 1
        ),
        delitos_tmp AS
        (
            SELECT d.id_ci, d.id_delito, d.dto, d.moda_dto, fa.id_forma_accion, COALESCE(TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, N' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), N'')), 103), TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, N' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), N''))), CASE WHEN TRY_CONVERT(float, REPLACE(NULLIF(d.hra_de_hchos, N''), N',', N'.')) >= 0 AND TRY_CONVERT(float, REPLACE(NULLIF(d.hra_de_hchos, N''), N',', N'.')) < 1 THEN DATEADD(SECOND, CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(d.hra_de_hchos, N''), N',', N'.')) * 86400, 0)), COALESCE(TRY_CONVERT(datetime2, d.fha_de_hchos, 103), TRY_CONVERT(datetime2, d.fha_de_hchos))) END, TRY_CONVERT(datetime2, d.fha_de_hchos, 103), TRY_CONVERT(datetime2, d.fha_de_hchos)) AS fecha_hechos, ic.id_instrumento_comision, gc.id_grado_consumacion, md.id_modalidad_delito, ef.id_entidad_federativa, mun.id_municipio, d.id_loc_hchos, d.nom_loc_hchos, d.id_col_hchos, d.nom_col_hchos, cp.id_codigo_postal, NULLIF(LTRIM(RTRIM(d.cp)), N'') AS codigo_postal_fiscalia, TRY_CONVERT(decimal(10,6), NULLIF(REPLACE(d.coord_x, N',', N'.'), N'')) AS coordenada_x, TRY_CONVERT(decimal(10,6), NULLIF(REPLACE(d.coord_y, N',', N'.'), N'')) AS coordenada_y, d.dom_hchos
            FROM dbo.federal_carga_tmp_delito d INNER JOIN dbo.federal_catalogo_modalidad_delito md ON md.clave4 = d.clasf_de_dto AND md.activo = 1 INNER JOIN dbo.catalogo_forma_accion fa ON fa.clave = TRY_CONVERT(tinyint, d.forma_acc) AND fa.activo = 1 INNER JOIN dbo.catalogo_instrumento_comision ic ON ic.clave = TRY_CONVERT(tinyint, d.emto_com_dto) AND ic.activo = 1 INNER JOIN dbo.catalogo_grado_consumacion gc ON gc.clave = TRY_CONVERT(tinyint, d.grdo_cons) AND gc.activo = 1 INNER JOIN dbo.catalogo_entidad_federativa ef ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos) AND ef.activo = 1 INNER JOIN dbo.catalogo_municipio mun ON mun.id_entidad_federativa = ef.id_entidad_federativa AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos) AND mun.activo = 1 OUTER APPLY (SELECT TOP (1) ccp.id_codigo_postal FROM dbo.catalogo_codigo_postal ccp WHERE ccp.codigo_postal = RIGHT(N'00000' + LTRIM(RTRIM(d.cp)), 5) AND ccp.id_municipio = mun.id_municipio AND ccp.activo = 1 ORDER BY ccp.id_codigo_postal) cp
            WHERE d.id_federal_carga = @IdFederalCarga AND d.activo = 1
        ),
        delitos_clasificados AS
        (
            SELECT CASE WHEN da.identificador_delito_fiscalia IS NULL THEN N'NUEVO' WHEN ISNULL(da.delito_fiscalia, N'') <> ISNULL(dt.dto, N'') OR ISNULL(da.modalidad_delito_fiscalia, N'') <> ISNULL(dt.moda_dto, N'') OR ISNULL(da.id_forma_accion, 0) <> ISNULL(dt.id_forma_accion, 0) OR ISNULL(CONVERT(varchar(19), da.fecha_hechos, 120), N'') <> ISNULL(CONVERT(varchar(19), dt.fecha_hechos, 120), N'') OR ISNULL(da.id_instrumento_comision, 0) <> ISNULL(dt.id_instrumento_comision, 0) OR ISNULL(da.id_grado_consumacion, 0) <> ISNULL(dt.id_grado_consumacion, 0) OR ISNULL(da.id_modalidad_delito, 0) <> ISNULL(dt.id_modalidad_delito, 0) OR ISNULL(da.id_entidad_federativa, 0) <> ISNULL(dt.id_entidad_federativa, 0) OR ISNULL(da.id_municipio, 0) <> ISNULL(dt.id_municipio, 0) OR ISNULL(da.id_localidad_fiscalia, N'') <> ISNULL(dt.id_loc_hchos, N'') OR ISNULL(da.localidad_fiscalia_nombre, N'') <> ISNULL(dt.nom_loc_hchos, N'') OR ISNULL(da.id_colonia_fiscalia, N'') <> ISNULL(dt.id_col_hchos, N'') OR ISNULL(da.colonia_fiscalia_nombre, N'') <> ISNULL(dt.nom_col_hchos, N'') OR ISNULL(da.id_codigo_postal, 0) <> ISNULL(dt.id_codigo_postal, 0) OR ISNULL(da.codigo_postal_fiscalia, N'') <> ISNULL(dt.codigo_postal_fiscalia, N'') OR ISNULL(da.coordenada_x, 0) <> ISNULL(dt.coordenada_x, 0) OR ISNULL(da.coordenada_y, 0) <> ISNULL(dt.coordenada_y, 0) OR ISNULL(da.domicilio_hechos, N'') <> ISNULL(dt.dom_hchos, N'') THEN N'MODIFICADO' ELSE N'SIN_CAMBIOS' END AS tipo
            FROM delitos_tmp dt LEFT JOIN delitos_actuales da ON da.id_ci = dt.id_ci AND da.identificador_delito_fiscalia = dt.id_delito
            UNION ALL
            SELECT N'ELIMINADO' FROM delitos_actuales da LEFT JOIN delitos_tmp dt ON dt.id_ci = da.id_ci AND dt.id_delito = da.identificador_delito_fiscalia WHERE dt.id_delito IS NULL
        ),
        victimas_actuales AS
        (
            SELECT ci.identificador_carpeta_fiscalia AS id_ci, d.identificador_delito_fiscalia AS id_delito, v.identificador_victima_fiscalia, v.id_tipo_victima, v.id_tipo_victima_moral, v.id_sexo, v.id_genero, v.id_nacionalidad, v.id_pertenece_poblacion_indigena, v.id_presenta_discapacidad, v.fecha_nacimiento, v.edad
            FROM dbo.federal_victima v INNER JOIN dbo.federal_delito d ON d.id_federal_delito = v.id_federal_delito AND d.activo = 1 INNER JOIN dbo.federal_carpeta_investigacion ci ON ci.id_federal_carpeta_investigacion = d.id_federal_carpeta_investigacion AND ci.activo = 1 INNER JOIN dbo.federal_carga c ON c.id_federal_carga = v.id_federal_carga
            WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND v.activo = 1
        ),
        victimas_tmp AS
        (
            SELECT v.id_ci, v.id_delito, v.id_vicf, tv.id_tipo_victima, tvm.id_tipo_victima_moral, sx.id_sexo, gen.id_genero, nac.id_nacionalidad, pob.id_pertenece_poblacion_indigena, disc.id_presenta_discapacidad, COALESCE(TRY_CONVERT(date, NULLIF(v.fha_nac, N''), 103), TRY_CONVERT(date, NULLIF(v.fha_nac, N''))) AS fecha_nacimiento, TRY_CONVERT(smallint, NULLIF(v.edad, N'')) AS edad
            FROM dbo.federal_carga_tmp_victima v INNER JOIN dbo.catalogo_tipo_victima tv ON tv.clave = TRY_CONVERT(tinyint, v.id_tv) AND tv.activo = 1 LEFT JOIN dbo.catalogo_tipo_victima_moral tvm ON tvm.clave = TRY_CONVERT(tinyint, NULLIF(v.id_tpm, N'')) AND tvm.activo = 1 LEFT JOIN dbo.catalogo_sexo sx ON sx.clave = TRY_CONVERT(tinyint, NULLIF(v.sexo, N'')) AND sx.activo = 1 LEFT JOIN dbo.catalogo_genero gen ON gen.clave = TRY_CONVERT(tinyint, NULLIF(v.genero, N'')) AND gen.activo = 1 LEFT JOIN dbo.catalogo_nacionalidad nac ON TRY_CONVERT(int, nac.clave) = TRY_CONVERT(int, NULLIF(v.nacional, N'')) AND nac.activo = 1 LEFT JOIN dbo.catalogo_pertenece_poblacion_indigena pob ON pob.clave = TRY_CONVERT(tinyint, NULLIF(v.pob, N'')) AND pob.activo = 1 LEFT JOIN dbo.catalogo_presenta_discapacidad disc ON disc.clave = TRY_CONVERT(tinyint, NULLIF(v.disc, N'')) AND disc.activo = 1
            WHERE v.id_federal_carga = @IdFederalCarga AND v.activo = 1
        ),
        victimas_clasificadas AS
        (
            SELECT CASE WHEN va.identificador_victima_fiscalia IS NULL THEN N'NUEVO' WHEN ISNULL(va.id_tipo_victima, 0) <> ISNULL(vt.id_tipo_victima, 0) OR ISNULL(va.id_tipo_victima_moral, 0) <> ISNULL(vt.id_tipo_victima_moral, 0) OR ISNULL(va.id_sexo, 0) <> ISNULL(vt.id_sexo, 0) OR ISNULL(va.id_genero, 0) <> ISNULL(vt.id_genero, 0) OR ISNULL(va.id_nacionalidad, 0) <> ISNULL(vt.id_nacionalidad, 0) OR ISNULL(va.id_pertenece_poblacion_indigena, 0) <> ISNULL(vt.id_pertenece_poblacion_indigena, 0) OR ISNULL(va.id_presenta_discapacidad, 0) <> ISNULL(vt.id_presenta_discapacidad, 0) OR ISNULL(CONVERT(varchar(10), va.fecha_nacimiento, 120), N'') <> ISNULL(CONVERT(varchar(10), vt.fecha_nacimiento, 120), N'') OR ISNULL(va.edad, 0) <> ISNULL(vt.edad, 0) THEN N'MODIFICADO' ELSE N'SIN_CAMBIOS' END AS tipo
            FROM victimas_tmp vt LEFT JOIN victimas_actuales va ON va.id_ci = vt.id_ci AND va.id_delito = vt.id_delito AND va.identificador_victima_fiscalia = vt.id_vicf
            UNION ALL
            SELECT N'ELIMINADO' FROM victimas_actuales va LEFT JOIN victimas_tmp vt ON vt.id_ci = va.id_ci AND vt.id_delito = va.id_delito AND vt.id_vicf = va.identificador_victima_fiscalia WHERE vt.id_vicf IS NULL
        )
        SELECT N'carpetas' AS Archivo, tipo AS Tipo, COUNT(1) AS Total FROM carpetas_clasificadas GROUP BY tipo
        UNION ALL SELECT N'delitos', tipo, COUNT(1) FROM delitos_clasificados GROUP BY tipo
        UNION ALL SELECT N'victimas', tipo, COUNT(1) FROM victimas_clasificadas GROUP BY tipo
        OPTION (RECOMPILE);
        """;

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QueryAsync<(string Archivo, string Tipo, int Total)>(sql, new { IdFederalCarga = idFederalCarga }, commandTimeout: 180);
    }

    private static async Task<long> CrearCargaAsync(SqlConnection connection, SqlTransaction transaction, int idUsuarioCarga, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError)
    {
        const string sql = """
            INSERT INTO dbo.federal_carga (id_usuario_carga, id_entidad_federativa, codigo_referencia, tipo_carga, mes_corte, anio_corte, total_carpetas_investigacion, total_delitos, total_victimas, estado, fecha_validacion, fecha_expiracion, mensaje_error, activo)
            OUTPUT INSERTED.id_federal_carga
            VALUES (@IdUsuarioCarga, NULL, @CodigoReferencia, N'ACTUALIZACION', @MesCorte, @AnioCorte, @TotalCarpetas, @TotalDelitos, @TotalVictimas, @Estado, SYSDATETIME(), DATEADD(HOUR, 48, SYSDATETIME()), @MensajeError, 1);
            """;

        return await connection.ExecuteScalarAsync<long>(sql, new { IdUsuarioCarga = idUsuarioCarga, CodigoReferencia = codigoReferencia, MesCorte = mesCorte, AnioCorte = anioCorte, TotalCarpetas = totalCarpetas, TotalDelitos = totalDelitos, TotalVictimas = totalVictimas, Estado = estado, MensajeError = mensajeError }, transaction);
    }

    private static async Task GuardarTmpCarpetasAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, List<ArchivoFila> filas)
    {
        var tabla = new DataTable();
        foreach (var columna in new[] { "id_ci", "ntra_ci", "fha_de_ini", "hra_de_ini", "rmen_de_hchos" }) tabla.Columns.Add(columna, typeof(string));
        tabla.Columns.Add("id_federal_carga", typeof(long));
        tabla.Columns.Add("numero_fila", typeof(int));
        tabla.Columns.Add("estado", typeof(string));
        tabla.Columns.Add("activo", typeof(bool));
        tabla.Columns["id_federal_carga"]!.SetOrdinal(0);
        tabla.Columns["numero_fila"]!.SetOrdinal(1);
        foreach (var fila in filas) tabla.Rows.Add(idFederalCarga, fila.NumeroFila, Valor(fila, "id_ci"), Valor(fila, "ntra_ci"), Valor(fila, "fha_de_ini"), Valor(fila, "hra_de_ini"), Valor(fila, "rmen_de_hchos"), "PENDIENTE", true);
        await EjecutarBulkAsync(connection, transaction, tabla, "dbo.federal_carga_tmp_carpeta");
    }

    private static async Task GuardarTmpDelitosAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, List<ArchivoFila> filas)
    {
        var tabla = CrearTablaStaging(["id_ci", "id_delito", "dto", "moda_dto", "forma_acc", "fha_de_hchos", "hra_de_hchos", "emto_com_dto", "grdo_cons", "clasf_de_dto", "id_ent_hchos", "id_mun_hchos", "id_loc_hchos", "nom_loc_hchos", "id_col_hchos", "nom_col_hchos", "cp", "coord_x", "coord_y", "dom_hchos"]);
        foreach (var fila in filas) tabla.Rows.Add(new object[] { idFederalCarga, fila.NumeroFila }.Concat(new[] { "id_ci", "id_delito", "dto", "moda_dto", "forma_acc", "fha_de_hchos", "hra_de_hchos", "emto_com_dto", "grdo_cons", "clasf_de_dto", "id_ent_hchos", "id_mun_hchos", "id_loc_hchos", "nom_loc_hchos", "id_col_hchos", "nom_col_hchos", "cp", "coord_x", "coord_y", "dom_hchos" }.Select(x => (object)Valor(fila, x))).Concat(new object[] { "PENDIENTE", true }).ToArray());
        await EjecutarBulkAsync(connection, transaction, tabla, "dbo.federal_carga_tmp_delito");
    }

    private static async Task GuardarTmpVictimasAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, List<ArchivoFila> filas)
    {
        var columnas = new[] { "id_ci", "id_delito", "id_vicf", "id_tv", "id_tpm", "sexo", "genero", "pob", "disc", "fha_nac", "edad", "nacional" };
        var tabla = CrearTablaStaging(columnas);
        foreach (var fila in filas) tabla.Rows.Add(new object[] { idFederalCarga, fila.NumeroFila }.Concat(columnas.Select(x => (object)Valor(fila, x))).Concat(new object[] { "PENDIENTE", true }).ToArray());
        await EjecutarBulkAsync(connection, transaction, tabla, "dbo.federal_carga_tmp_victima");
    }

    private static DataTable CrearTablaStaging(IEnumerable<string> columnas)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("id_federal_carga", typeof(long));
        tabla.Columns.Add("numero_fila", typeof(int));
        foreach (var columna in columnas) tabla.Columns.Add(columna, typeof(string));
        tabla.Columns.Add("estado", typeof(string));
        tabla.Columns.Add("activo", typeof(bool));
        return tabla;
    }

    private static async Task EjecutarBulkAsync(SqlConnection connection, SqlTransaction transaction, DataTable tabla, string destino)
    {
        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction) { DestinationTableName = destino };
        foreach (DataColumn columna in tabla.Columns) bulk.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);
        await bulk.WriteToServerAsync(tabla);
    }

    private static string Valor(ArchivoFila fila, string columna) => fila.Columnas.TryGetValue(columna, out var valor) && !string.IsNullOrWhiteSpace(valor) ? valor.Trim() : string.Empty;

    private static string ObtenerNombreMes(int mes) => mes switch { 1 => "Enero", 2 => "Febrero", 3 => "Marzo", 4 => "Abril", 5 => "Mayo", 6 => "Junio", 7 => "Julio", 8 => "Agosto", 9 => "Septiembre", 10 => "Octubre", 11 => "Noviembre", 12 => "Diciembre", _ => mes.ToString("00") };

    private static ConfirmarCargaResponse Respuesta(bool esValido, string codigoReferencia, string estado, string mensaje) => new() { EsValido = esValido, CodigoReferencia = codigoReferencia, Estado = estado, Mensaje = mensaje };
}
