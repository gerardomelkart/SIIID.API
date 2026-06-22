using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class ActualizacionCargaRepository : IActualizacionCargaRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public ActualizacionCargaRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<long> GuardarIntentoActualizacionAsync(int idUsuarioCarga, int? idEntidadFederativa, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError, List<CargaValidacionError> advertencias, List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas)
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

            await CargaAuditoriaSql.GuardarAdvertenciasAsync(
                                    connection,
                                    transaction,
                                    idCarga,
                                    advertencias);

            await CargaAuditoriaSql.RegistrarCambioEstadoAsync(
                                    connection,
                                    transaction,
                                    idCarga,
                                    estadoAnterior: null,
                                    estadoNuevo: estado,
                                    idUsuario: idUsuarioCarga,
                                    comentario: estado == "VALIDADO_PENDIENTE_ACTUALIZACION"
                                        ? "Actualización validada y pendiente de decisión del usuario."
                                        : "Intento de actualización registrado con errores de validación.");

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
        // Compara la actualización en staging contra los registros finales activos
        // del mismo periodo y entidad.
        //
        // Después de confirmar actualizaciones, las versiones anteriores quedan inactivas.
        // Por eso no se reconstruye la versión vigente con ROW_NUMBER; se usa activo = 1.

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

;WITH carpetas_actuales AS (
    SELECT
        ci.id_carpeta_investigacion,
        ci.identificador_carpeta_fiscalia,
        ci.nomenclatura_carpeta_fiscalia,
        ci.fecha_inicio,
        ci.resumen_hechos
    FROM carpeta_investigacion ci
    INNER JOIN carga c
        ON c.id_carga = ci.id_carga
    WHERE c.id_entidad_federativa = @IdEntidadFederativa
      AND c.mes_corte = @MesCorte
      AND c.anio_corte = @AnioCorte
      AND c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
      AND c.activo = 1
      AND ci.activo = 1
),
carpetas_tmp AS (
            SELECT
                id_ci,
                ntra_ci,
                COALESCE(
                    TRY_CONVERT(datetime2, CONCAT(fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(hra_de_ini)), '')), 103),
                    TRY_CONVERT(datetime2, CONCAT(fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(hra_de_ini)), ''))),
                    CASE
                        WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(hra_de_ini)), ''), ',', '.')) IS NOT NULL
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(hra_de_ini)), ''), ',', '.')) >= 0
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(hra_de_ini)), ''), ',', '.')) < 1
                        THEN DATEADD(
                            SECOND,
                            CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(hra_de_ini)), ''), ',', '.')) * 86400, 0)),
                            COALESCE(
                                TRY_CONVERT(datetime2, fha_de_ini, 103),
                                TRY_CONVERT(datetime2, fha_de_ini)
                            )
                        )
                    END,
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
delitos_actuales AS (
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
        d.domicilio_hechos
    FROM delito d
    INNER JOIN carpeta_investigacion ci
        ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
       AND ci.activo = 1
    INNER JOIN carga c
        ON c.id_carga = d.id_carga
    WHERE c.id_entidad_federativa = @IdEntidadFederativa
      AND c.mes_corte = @MesCorte
      AND c.anio_corte = @AnioCorte
      AND c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
      AND c.activo = 1
      AND d.activo = 1
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
victimas_actuales AS (
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
        v.edad
    FROM victima v
    INNER JOIN delito d
        ON d.id_delito = v.id_delito
       AND d.activo = 1
    INNER JOIN carpeta_investigacion ci
        ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
       AND ci.activo = 1
    INNER JOIN carga c
        ON c.id_carga = v.id_carga
    WHERE c.id_entidad_federativa = @IdEntidadFederativa
      AND c.mes_corte = @MesCorte
      AND c.anio_corte = @AnioCorte
      AND c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
      AND c.activo = 1
      AND v.activo = 1
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
GROUP BY tipo
OPTION (RECOMPILE);
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

    private static string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }
}