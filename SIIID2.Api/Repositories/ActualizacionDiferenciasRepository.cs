using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Data;

namespace SIIID2.Api.Repositories;

public class ActualizacionDiferenciasRepository : IActualizacionDiferenciasRepository
{
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

    private class ActualizacionDiferenciasContexto
    {
        public long IdCarga { get; set; }

        public string CodigoReferencia { get; set; } = string.Empty;

        public int IdEntidadFederativa { get; set; }

        public int MesCorte { get; set; }

        public int AnioCorte { get; set; }
    }

    private readonly IDbConnectionFactory _dbConnectionFactory;

    public ActualizacionDiferenciasRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<ActualizacionDiferenciasResponse?> ObtenerDetalleDiferenciasActualizacionAsync(string codigoReferencia, int? idEntidadFederativaUsuario, bool esSuperUsuario, int limitePorSeccion)
    {
        using var connection = _dbConnectionFactory.CrearConexion();

        var contexto = await ObtenerContextoActualizacionAsync(
            connection,
            codigoReferencia,
            idEntidadFederativaUsuario,
            esSuperUsuario);

        if (contexto == null)
        {
            return null;
        }

        var filas = new List<ActualizacionDiferenciaRow>();

        filas.AddRange(await ObtenerDiferenciasCarpetasAsync(connection, contexto));
        filas.AddRange(await ObtenerDiferenciasDelitosAsync(connection, contexto));
        filas.AddRange(await ObtenerDiferenciasVictimasAsync(connection, contexto));

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

        AplicarLimiteDiferencias(response, limitePorSeccion);

        return response;
    }

    private static async Task<ActualizacionDiferenciasContexto?> ObtenerContextoActualizacionAsync(IDbConnection connection, string codigoReferencia, int? idEntidadFederativaUsuario, bool esSuperUsuario)
    {
        var sql = @"
        SELECT TOP 1
            id_carga AS IdCarga,
            codigo_referencia AS CodigoReferencia,
            id_entidad_federativa AS IdEntidadFederativa,
            mes_corte AS MesCorte,
            anio_corte AS AnioCorte
        FROM carga
        WHERE codigo_referencia = @CodigoReferencia
          AND tipo_carga = 'ACTUALIZACION'
          AND estado IN
            (
                'VALIDADO_PENDIENTE_ACTUALIZACION',
                'PENDIENTE_APROBACION'
            )
          AND activo = 1
          AND (
                @EsSuperUsuario = 1
                OR id_entidad_federativa = @IdEntidadFederativaUsuario
              );
        ";

        return await connection.QueryFirstOrDefaultAsync<ActualizacionDiferenciasContexto>(sql, new
        {
            CodigoReferencia = codigoReferencia,
            IdEntidadFederativaUsuario = idEntidadFederativaUsuario,
            EsSuperUsuario = esSuperUsuario
        });
    }

    private static async Task<List<ActualizacionDiferenciaRow>> ObtenerDiferenciasCarpetasAsync(IDbConnection connection, ActualizacionDiferenciasContexto contexto)
    {
        var sql = @"
        ;WITH carpetas_actuales AS (
            SELECT
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
                c.id_ci,
                c.ntra_ci,
                c.fha_de_ini AS fha_de_ini_excel,
                c.hra_de_ini AS hra_de_ini_excel,
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
                ) AS fecha_inicio,
                c.rmen_de_hchos
            FROM carga_tmp_carpeta c
            WHERE c.id_carga = @IdCarga
              AND c.activo = 1
        )
        SELECT
            'carpetas' AS Seccion,
            'NUEVO' AS TipoMovimiento,
            'id_ci' AS CampoIdentificador,
            ct.id_ci AS IdentificadorFiscalia,
            dif.Campo,
            dif.ValorAnterior,
            dif.ValorNuevo
        FROM carpetas_tmp ct
        LEFT JOIN carpetas_actuales ca
            ON ca.identificador_carpeta_fiscalia = ct.id_ci
        CROSS APPLY (
            VALUES
                ('id_ci', CAST(NULL AS varchar(max)), CONVERT(varchar(max), ct.id_ci)),
                ('ntra_ci', CAST(NULL AS varchar(max)), CONVERT(varchar(max), ct.ntra_ci)),
                ('fha_de_ini', CAST(NULL AS varchar(max)), CONVERT(varchar(max), ct.fha_de_ini_excel)),
                ('hra_de_ini', CAST(NULL AS varchar(max)), CONVERT(varchar(max), ct.hra_de_ini_excel)),
                ('rmen_de_hchos', CAST(NULL AS varchar(max)), CONVERT(varchar(max), ct.rmen_de_hchos))
        ) dif(Campo, ValorAnterior, ValorNuevo)
        WHERE ca.identificador_carpeta_fiscalia IS NULL

        UNION ALL

        SELECT
            'carpetas',
            'ELIMINADO',
            'id_ci',
            ca.identificador_carpeta_fiscalia,
            dif.Campo,
            dif.ValorAnterior,
            dif.ValorNuevo
        FROM carpetas_actuales ca
        LEFT JOIN carpetas_tmp ct
            ON ct.id_ci = ca.identificador_carpeta_fiscalia
        CROSS APPLY (
            VALUES
                ('id_ci', CONVERT(varchar(max), ca.identificador_carpeta_fiscalia), CAST(NULL AS varchar(max))),
                ('ntra_ci', CONVERT(varchar(max), ca.nomenclatura_carpeta_fiscalia), CAST(NULL AS varchar(max))),
                ('fha_de_ini', CONVERT(varchar(max), CONVERT(varchar(10), ca.fecha_inicio, 103)), CAST(NULL AS varchar(max))),
                ('hra_de_ini', CONVERT(varchar(max), CASE
                    WHEN ca.fecha_inicio IS NULL THEN ''
                    WHEN CONVERT(time, ca.fecha_inicio) = '00:00:00' THEN ''
                    ELSE CONVERT(varchar(8), ca.fecha_inicio, 108)
                END), CAST(NULL AS varchar(max))),
                ('rmen_de_hchos', CONVERT(varchar(max), ca.resumen_hechos), CAST(NULL AS varchar(max)))
        ) dif(Campo, ValorAnterior, ValorNuevo)
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
                ('ntra_ci', CONVERT(varchar(max), ca.nomenclatura_carpeta_fiscalia), CONVERT(varchar(max), ct.ntra_ci), CONVERT(varchar(max), ca.nomenclatura_carpeta_fiscalia), CONVERT(varchar(max), ct.ntra_ci)),
                ('fha_de_ini',
                    CONVERT(varchar(max), CONVERT(varchar(10), ca.fecha_inicio, 103)),
                    CONVERT(varchar(max), ct.fha_de_ini_excel),
                    CONVERT(varchar(max), CONVERT(varchar(10), ca.fecha_inicio, 120)),
                    CONVERT(varchar(max), CONVERT(varchar(10), ct.fecha_inicio, 120))
                ),
                ('hra_de_ini',
                    CONVERT(varchar(max), CASE
                        WHEN ca.fecha_inicio IS NULL THEN ''
                        WHEN CONVERT(time, ca.fecha_inicio) = '00:00:00' THEN ''
                        ELSE CONVERT(varchar(8), ca.fecha_inicio, 108)
                    END),
                    CONVERT(varchar(max), ct.hra_de_ini_excel),
                    CONVERT(varchar(max), CASE
                        WHEN ca.fecha_inicio IS NULL THEN ''
                        WHEN CONVERT(time, ca.fecha_inicio) = '00:00:00' THEN ''
                        ELSE CONVERT(varchar(8), ca.fecha_inicio, 108)
                    END),
                    CONVERT(varchar(max), CASE
                        WHEN ct.fecha_inicio IS NULL THEN ''
                        WHEN CONVERT(time, ct.fecha_inicio) = '00:00:00' THEN ''
                        ELSE CONVERT(varchar(8), ct.fecha_inicio, 108)
                    END)
                ),
                ('rmen_de_hchos',
                    CONVERT(varchar(max), ca.resumen_hechos),
                    CONVERT(varchar(max), ct.rmen_de_hchos),
                    CONVERT(varchar(max), ca.resumen_hechos),
                    CONVERT(varchar(max), ct.rmen_de_hchos)
                )
        ) dif(Campo, ValorAnterior, ValorNuevo, ComparacionAnterior, ComparacionNuevo)
        WHERE ISNULL(dif.ComparacionAnterior, '') <> ISNULL(dif.ComparacionNuevo, '') OPTION (RECOMPILE);
    ";

        var filas = await connection.QueryAsync<ActualizacionDiferenciaRow>(sql, contexto, commandTimeout: 180);

        return filas.ToList();
    }

    private static async Task<List<ActualizacionDiferenciaRow>> ObtenerDiferenciasDelitosAsync(IDbConnection connection, ActualizacionDiferenciasContexto contexto)
    {
        var sql = @"
        ;WITH delitos_actuales AS (
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
                CONVERT(varchar(50), fa.clave) AS forma_acc_valor,
                CONVERT(varchar(50), ic.clave) AS emto_com_dto_valor,
                CONVERT(varchar(50), gc.clave) AS grdo_cons_valor,
                md.clave4 AS clasf_de_dto_valor,
                ef.clave AS id_ent_hchos_valor,
                mun.clave AS id_mun_hchos_valor,
                ccp.codigo_postal AS cp_valor
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN carga c
                ON c.id_carga = d.id_carga
            LEFT JOIN catalogo_forma_accion fa
                ON fa.id_forma_accion = d.id_forma_accion
            LEFT JOIN catalogo_instrumento_comision ic
                ON ic.id_instrumento_comision = d.id_instrumento_comision
            LEFT JOIN catalogo_grado_consumacion gc
                ON gc.id_grado_consumacion = d.id_grado_consumacion
            LEFT JOIN catalogo_modalidad_delito md
                ON md.id_modalidad_delito = d.id_modalidad_delito
            LEFT JOIN catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = d.id_entidad_federativa
            LEFT JOIN catalogo_municipio mun
                ON mun.id_municipio = d.id_municipio
            LEFT JOIN catalogo_codigo_postal ccp
                ON ccp.id_codigo_postal = d.id_codigo_postal
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
                d.forma_acc AS forma_acc_excel,
                d.fha_de_hchos AS fha_de_hchos_excel,
                d.hra_de_hchos AS hra_de_hchos_excel,
                d.emto_com_dto AS emto_com_dto_excel,
                d.grdo_cons AS grdo_cons_excel,
                d.clasf_de_dto AS clasf_de_dto_excel,
                d.id_ent_hchos AS id_ent_hchos_excel,
                d.id_mun_hchos AS id_mun_hchos_excel,
                d.id_loc_hchos AS id_loc_hchos_excel,
                d.nom_loc_hchos AS nom_loc_hchos_excel,
                d.id_col_hchos AS id_col_hchos_excel,
                d.nom_col_hchos AS nom_col_hchos_excel,
                d.cp AS cp_excel,
                d.coord_x AS coord_x_excel,
                d.coord_y AS coord_y_excel,
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
            WHERE d.id_carga = @IdCarga
              AND d.activo = 1
        )
        SELECT
            'delitos' AS Seccion,
            'NUEVO' AS TipoMovimiento,
            'id_ci + id_delito' AS CampoIdentificador,
            CONCAT(dt.id_ci, ' | ', dt.id_delito) AS IdentificadorFiscalia,
            dif.Campo,
            dif.ValorAnterior,
            dif.ValorNuevo
        FROM delitos_tmp dt
        LEFT JOIN delitos_actuales da
            ON da.id_ci = dt.id_ci
           AND da.identificador_delito_fiscalia = dt.id_delito
        CROSS APPLY (
            VALUES
                ('id_ci', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.id_ci)),
                ('id_delito', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.id_delito)),
                ('dto', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.dto)),
                ('moda_dto', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.moda_dto)),
                ('forma_acc', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.forma_acc_excel)),
                ('fha_de_hchos', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.fha_de_hchos_excel)),
                ('hra_de_hchos', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.hra_de_hchos_excel)),
                ('emto_com_dto', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.emto_com_dto_excel)),
                ('grdo_cons', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.grdo_cons_excel)),
                ('clasf_de_dto', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.clasf_de_dto_excel)),
                ('id_ent_hchos', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.id_ent_hchos_excel)),
                ('id_mun_hchos', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.id_mun_hchos_excel)),
                ('id_loc_hchos', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.id_loc_hchos_excel)),
                ('nom_loc_hchos', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.nom_loc_hchos_excel)),
                ('id_col_hchos', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.id_col_hchos_excel)),
                ('nom_col_hchos', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.nom_col_hchos_excel)),
                ('cp', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.cp_excel)),
                ('coord_x', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.coord_x_excel)),
                ('coord_y', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.coord_y_excel)),
                ('dom_hchos', CAST(NULL AS varchar(max)), CONVERT(varchar(max), dt.dom_hchos))
        ) dif(Campo, ValorAnterior, ValorNuevo)
        WHERE da.identificador_delito_fiscalia IS NULL

        UNION ALL

        SELECT
            'delitos',
            'ELIMINADO',
            'id_ci + id_delito',
            CONCAT(da.id_ci, ' | ', da.identificador_delito_fiscalia),
            dif.Campo,
            dif.ValorAnterior,
            dif.ValorNuevo
        FROM delitos_actuales da
        LEFT JOIN delitos_tmp dt
            ON dt.id_ci = da.id_ci
           AND dt.id_delito = da.identificador_delito_fiscalia
        CROSS APPLY (
            VALUES
                ('id_ci', CONVERT(varchar(max), da.id_ci), CAST(NULL AS varchar(max))),
                ('id_delito', CONVERT(varchar(max), da.identificador_delito_fiscalia), CAST(NULL AS varchar(max))),
                ('dto', CONVERT(varchar(max), da.delito_fiscalia), CAST(NULL AS varchar(max))),
                ('moda_dto', CONVERT(varchar(max), da.modalidad_delito_fiscalia), CAST(NULL AS varchar(max))),
                ('forma_acc', CONVERT(varchar(max), da.forma_acc_valor), CAST(NULL AS varchar(max))),
                ('fha_de_hchos', CONVERT(varchar(max), CONVERT(varchar(10), da.fecha_hechos, 103)), CAST(NULL AS varchar(max))),
                ('hra_de_hchos', CONVERT(varchar(max), CASE
                    WHEN da.fecha_hechos IS NULL THEN ''
                    WHEN CONVERT(time, da.fecha_hechos) = '00:00:00' THEN ''
                    ELSE CONVERT(varchar(8), da.fecha_hechos, 108)
                END), CAST(NULL AS varchar(max))),
                ('emto_com_dto', CONVERT(varchar(max), da.emto_com_dto_valor), CAST(NULL AS varchar(max))),
                ('grdo_cons', CONVERT(varchar(max), da.grdo_cons_valor), CAST(NULL AS varchar(max))),
                ('clasf_de_dto', CONVERT(varchar(max), da.clasf_de_dto_valor), CAST(NULL AS varchar(max))),
                ('id_ent_hchos', CONVERT(varchar(max), da.id_ent_hchos_valor), CAST(NULL AS varchar(max))),
                ('id_mun_hchos', CONVERT(varchar(max), da.id_mun_hchos_valor), CAST(NULL AS varchar(max))),
                ('id_loc_hchos', CONVERT(varchar(max), da.id_localidad_fiscalia), CAST(NULL AS varchar(max))),
                ('nom_loc_hchos', CONVERT(varchar(max), da.localidad_fiscalia_nombre), CAST(NULL AS varchar(max))),
                ('id_col_hchos', CONVERT(varchar(max), da.id_colonia_fiscalia), CAST(NULL AS varchar(max))),
                ('nom_col_hchos', CONVERT(varchar(max), da.colonia_fiscalia_nombre), CAST(NULL AS varchar(max))),
                ('cp', CONVERT(varchar(max), da.cp_valor), CAST(NULL AS varchar(max))),
                ('coord_x', CONVERT(varchar(max), da.coordenada_x), CAST(NULL AS varchar(max))),
                ('coord_y', CONVERT(varchar(max), da.coordenada_y), CAST(NULL AS varchar(max))),
                ('dom_hchos', CONVERT(varchar(max), da.domicilio_hechos), CAST(NULL AS varchar(max)))
        ) dif(Campo, ValorAnterior, ValorNuevo)
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
                ('dto', CONVERT(varchar(max), da.delito_fiscalia), CONVERT(varchar(max), dt.dto), CONVERT(varchar(max), da.delito_fiscalia), CONVERT(varchar(max), dt.dto)),
                ('moda_dto', CONVERT(varchar(max), da.modalidad_delito_fiscalia), CONVERT(varchar(max), dt.moda_dto), CONVERT(varchar(max), da.modalidad_delito_fiscalia), CONVERT(varchar(max), dt.moda_dto)),
                ('forma_acc', CONVERT(varchar(max), da.forma_acc_valor), CONVERT(varchar(max), dt.forma_acc_excel), CONVERT(varchar(max), da.id_forma_accion), CONVERT(varchar(max), dt.id_forma_accion)),
                ('fha_de_hchos',
                    CONVERT(varchar(max), CONVERT(varchar(10), da.fecha_hechos, 103)),
                    CONVERT(varchar(max), dt.fha_de_hchos_excel),
                    CONVERT(varchar(max), CONVERT(varchar(10), da.fecha_hechos, 120)),
                    CONVERT(varchar(max), CONVERT(varchar(10), dt.fecha_hechos, 120))
                ),
                ('hra_de_hchos',
                    CONVERT(varchar(max), CASE
                        WHEN da.fecha_hechos IS NULL THEN ''
                        WHEN CONVERT(time, da.fecha_hechos) = '00:00:00' THEN ''
                        ELSE CONVERT(varchar(8), da.fecha_hechos, 108)
                    END),
                    CONVERT(varchar(max), dt.hra_de_hchos_excel),
                    CONVERT(varchar(max), CASE
                        WHEN da.fecha_hechos IS NULL THEN ''
                        WHEN CONVERT(time, da.fecha_hechos) = '00:00:00' THEN ''
                        ELSE CONVERT(varchar(8), da.fecha_hechos, 108)
                    END),
                    CONVERT(varchar(max), CASE
                        WHEN dt.fecha_hechos IS NULL THEN ''
                        WHEN CONVERT(time, dt.fecha_hechos) = '00:00:00' THEN ''
                        ELSE CONVERT(varchar(8), dt.fecha_hechos, 108)
                    END)
                ),
                ('emto_com_dto', CONVERT(varchar(max), da.emto_com_dto_valor), CONVERT(varchar(max), dt.emto_com_dto_excel), CONVERT(varchar(max), da.id_instrumento_comision), CONVERT(varchar(max), dt.id_instrumento_comision)),
                ('grdo_cons', CONVERT(varchar(max), da.grdo_cons_valor), CONVERT(varchar(max), dt.grdo_cons_excel), CONVERT(varchar(max), da.id_grado_consumacion), CONVERT(varchar(max), dt.id_grado_consumacion)),
                ('clasf_de_dto', CONVERT(varchar(max), da.clasf_de_dto_valor), CONVERT(varchar(max), dt.clasf_de_dto_excel), CONVERT(varchar(max), da.id_modalidad_delito), CONVERT(varchar(max), dt.id_modalidad_delito)),
                ('id_ent_hchos', CONVERT(varchar(max), da.id_ent_hchos_valor), CONVERT(varchar(max), dt.id_ent_hchos_excel), CONVERT(varchar(max), da.id_entidad_federativa), CONVERT(varchar(max), dt.id_entidad_federativa)),
                ('id_mun_hchos', CONVERT(varchar(max), da.id_mun_hchos_valor), CONVERT(varchar(max), dt.id_mun_hchos_excel), CONVERT(varchar(max), da.id_municipio), CONVERT(varchar(max), dt.id_municipio)),
                ('id_loc_hchos', CONVERT(varchar(max), da.id_localidad_fiscalia), CONVERT(varchar(max), dt.id_loc_hchos_excel), CONVERT(varchar(max), da.id_localidad_fiscalia), CONVERT(varchar(max), dt.id_loc_hchos_excel)),
                ('nom_loc_hchos', CONVERT(varchar(max), da.localidad_fiscalia_nombre), CONVERT(varchar(max), dt.nom_loc_hchos_excel), CONVERT(varchar(max), da.localidad_fiscalia_nombre), CONVERT(varchar(max), dt.nom_loc_hchos_excel)),
                ('id_col_hchos', CONVERT(varchar(max), da.id_colonia_fiscalia), CONVERT(varchar(max), dt.id_col_hchos_excel), CONVERT(varchar(max), da.id_colonia_fiscalia), CONVERT(varchar(max), dt.id_col_hchos_excel)),
                ('nom_col_hchos', CONVERT(varchar(max), da.colonia_fiscalia_nombre), CONVERT(varchar(max), dt.nom_col_hchos_excel), CONVERT(varchar(max), da.colonia_fiscalia_nombre), CONVERT(varchar(max), dt.nom_col_hchos_excel)),
                ('cp', CONVERT(varchar(max), da.cp_valor), CONVERT(varchar(max), dt.cp_excel), CONVERT(varchar(max), da.id_codigo_postal), CONVERT(varchar(max), dt.id_codigo_postal)),
                ('coord_x', CONVERT(varchar(max), da.coordenada_x), CONVERT(varchar(max), dt.coord_x_excel), CONVERT(varchar(max), da.coordenada_x), CONVERT(varchar(max), dt.coordenada_x)),
                ('coord_y', CONVERT(varchar(max), da.coordenada_y), CONVERT(varchar(max), dt.coord_y_excel), CONVERT(varchar(max), da.coordenada_y), CONVERT(varchar(max), dt.coordenada_y)),
                ('dom_hchos', CONVERT(varchar(max), da.domicilio_hechos), CONVERT(varchar(max), dt.dom_hchos), CONVERT(varchar(max), da.domicilio_hechos), CONVERT(varchar(max), dt.dom_hchos))
        ) dif(Campo, ValorAnterior, ValorNuevo, ComparacionAnterior, ComparacionNuevo)
        WHERE ISNULL(dif.ComparacionAnterior, '') <> ISNULL(dif.ComparacionNuevo, '') OPTION (RECOMPILE);
    ";

        var filas = await connection.QueryAsync<ActualizacionDiferenciaRow>(sql, contexto, commandTimeout: 180);

        return filas.ToList();
    }

    private static async Task<List<ActualizacionDiferenciaRow>> ObtenerDiferenciasVictimasAsync(IDbConnection connection, ActualizacionDiferenciasContexto contexto)
    {
        var sql = @"
        ;WITH victimas_actuales AS (
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
                CONVERT(varchar(50), tv.clave) AS id_tv_valor,
                CONVERT(varchar(50), tvm.clave) AS id_tpm_valor,
                CONVERT(varchar(50), sx.clave) AS sexo_valor,
                CONVERT(varchar(50), gen.clave) AS genero_valor,
                nac.clave AS nacional_valor,
                CONVERT(varchar(50), pob.clave) AS pob_valor,
                CONVERT(varchar(50), disc.clave) AS disc_valor
            FROM victima v
            INNER JOIN delito d
                ON d.id_delito = v.id_delito
               AND d.activo = 1
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN carga c
                ON c.id_carga = v.id_carga
            LEFT JOIN catalogo_tipo_victima tv
                ON tv.id_tipo_victima = v.id_tipo_victima
            LEFT JOIN catalogo_tipo_victima_moral tvm
                ON tvm.id_tipo_victima_moral = v.id_tipo_victima_moral
            LEFT JOIN catalogo_sexo sx
                ON sx.id_sexo = v.id_sexo
            LEFT JOIN catalogo_genero gen
                ON gen.id_genero = v.id_genero
            LEFT JOIN catalogo_nacionalidad nac
                ON nac.id_nacionalidad = v.id_nacionalidad
            LEFT JOIN catalogo_pertenece_poblacion_indigena pob
                ON pob.id_pertenece_poblacion_indigena = v.id_pertenece_poblacion_indigena
            LEFT JOIN catalogo_presenta_discapacidad disc
                ON disc.id_presenta_discapacidad = v.id_presenta_discapacidad
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
                v.id_tv AS id_tv_excel,
                v.id_tpm AS id_tpm_excel,
                v.sexo AS sexo_excel,
                v.genero AS genero_excel,
                v.pob AS pob_excel,
                v.disc AS disc_excel,
                v.fha_nac AS fha_nac_excel,
                v.edad AS edad_excel,
                v.nacional AS nacional_excel,
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
            WHERE v.id_carga = @IdCarga
              AND v.activo = 1
        )
        SELECT
            'victimas' AS Seccion,
            'NUEVO' AS TipoMovimiento,
            'id_ci + id_delito + id_vicf' AS CampoIdentificador,
            CONCAT(vt.id_ci, ' | ', vt.id_delito, ' | ', vt.id_vicf) AS IdentificadorFiscalia,
            dif.Campo,
            dif.ValorAnterior,
            dif.ValorNuevo
        FROM victimas_tmp vt
        LEFT JOIN victimas_actuales va
            ON va.id_ci = vt.id_ci
           AND va.id_delito_fiscalia = vt.id_delito
           AND va.identificador_victima_fiscalia = vt.id_vicf
        CROSS APPLY (
            VALUES
                ('id_ci', CAST(NULL AS varchar(max)), CONVERT(varchar(max), vt.id_ci)),
                ('id_delito', CAST(NULL AS varchar(max)), CONVERT(varchar(max), vt.id_delito)),
                ('id_vicf', CAST(NULL AS varchar(max)), CONVERT(varchar(max), vt.id_vicf)),
                ('id_tv', CAST(NULL AS varchar(max)), CONVERT(varchar(max), vt.id_tv_excel)),
                ('id_tpm', CAST(NULL AS varchar(max)), CONVERT(varchar(max), vt.id_tpm_excel)),
                ('sexo', CAST(NULL AS varchar(max)), CONVERT(varchar(max), vt.sexo_excel)),
                ('genero', CAST(NULL AS varchar(max)), CONVERT(varchar(max), vt.genero_excel)),
                ('pob', CAST(NULL AS varchar(max)), CONVERT(varchar(max), vt.pob_excel)),
                ('disc', CAST(NULL AS varchar(max)), CONVERT(varchar(max), vt.disc_excel)),
                ('fha_nac', CAST(NULL AS varchar(max)), CONVERT(varchar(max), vt.fha_nac_excel)),
                ('edad', CAST(NULL AS varchar(max)), CONVERT(varchar(max), vt.edad_excel)),
                ('nacional', CAST(NULL AS varchar(max)), CONVERT(varchar(max), vt.nacional_excel))
        ) dif(Campo, ValorAnterior, ValorNuevo)
        WHERE va.identificador_victima_fiscalia IS NULL

        UNION ALL

        SELECT
            'victimas',
            'ELIMINADO',
            'id_ci + id_delito + id_vicf',
            CONCAT(va.id_ci, ' | ', va.id_delito_fiscalia, ' | ', va.identificador_victima_fiscalia),
            dif.Campo,
            dif.ValorAnterior,
            dif.ValorNuevo
        FROM victimas_actuales va
        LEFT JOIN victimas_tmp vt
            ON vt.id_ci = va.id_ci
           AND vt.id_delito = va.id_delito_fiscalia
           AND vt.id_vicf = va.identificador_victima_fiscalia
        CROSS APPLY (
            VALUES
                ('id_ci', CONVERT(varchar(max), va.id_ci), CAST(NULL AS varchar(max))),
                ('id_delito', CONVERT(varchar(max), va.id_delito_fiscalia), CAST(NULL AS varchar(max))),
                ('id_vicf', CONVERT(varchar(max), va.identificador_victima_fiscalia), CAST(NULL AS varchar(max))),
                ('id_tv', CONVERT(varchar(max), va.id_tv_valor), CAST(NULL AS varchar(max))),
                ('id_tpm', CONVERT(varchar(max), va.id_tpm_valor), CAST(NULL AS varchar(max))),
                ('sexo', CONVERT(varchar(max), va.sexo_valor), CAST(NULL AS varchar(max))),
                ('genero', CONVERT(varchar(max), va.genero_valor), CAST(NULL AS varchar(max))),
                ('pob', CONVERT(varchar(max), va.pob_valor), CAST(NULL AS varchar(max))),
                ('disc', CONVERT(varchar(max), va.disc_valor), CAST(NULL AS varchar(max))),
                ('fha_nac', CONVERT(varchar(max), CONVERT(varchar(10), va.fecha_nacimiento, 103)), CAST(NULL AS varchar(max))),
                ('edad', CONVERT(varchar(max), va.edad), CAST(NULL AS varchar(max))),
                ('nacional', CONVERT(varchar(max), va.nacional_valor), CAST(NULL AS varchar(max)))
        ) dif(Campo, ValorAnterior, ValorNuevo)
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
                ('id_tv', CONVERT(varchar(max), va.id_tv_valor), CONVERT(varchar(max), vt.id_tv_excel), CONVERT(varchar(max), va.id_tipo_victima), CONVERT(varchar(max), vt.id_tipo_victima)),
                ('id_tpm', CONVERT(varchar(max), va.id_tpm_valor), CONVERT(varchar(max), vt.id_tpm_excel), CONVERT(varchar(max), va.id_tipo_victima_moral), CONVERT(varchar(max), vt.id_tipo_victima_moral)),
                ('sexo', CONVERT(varchar(max), va.sexo_valor), CONVERT(varchar(max), vt.sexo_excel), CONVERT(varchar(max), va.id_sexo), CONVERT(varchar(max), vt.id_sexo)),
                ('genero', CONVERT(varchar(max), va.genero_valor), CONVERT(varchar(max), vt.genero_excel), CONVERT(varchar(max), va.id_genero), CONVERT(varchar(max), vt.id_genero)),
                ('pob', CONVERT(varchar(max), va.pob_valor), CONVERT(varchar(max), vt.pob_excel), CONVERT(varchar(max), va.id_pertenece_poblacion_indigena), CONVERT(varchar(max), vt.id_pertenece_poblacion_indigena)),
                ('disc', CONVERT(varchar(max), va.disc_valor), CONVERT(varchar(max), vt.disc_excel), CONVERT(varchar(max), va.id_presenta_discapacidad), CONVERT(varchar(max), vt.id_presenta_discapacidad)),
                ('fha_nac', CONVERT(varchar(max), CONVERT(varchar(10), va.fecha_nacimiento, 103)), CONVERT(varchar(max), vt.fha_nac_excel), CONVERT(varchar(max), CONVERT(varchar(10), va.fecha_nacimiento, 120)), CONVERT(varchar(max), CONVERT(varchar(10), vt.fecha_nacimiento, 120))),
                ('edad', CONVERT(varchar(max), va.edad), CONVERT(varchar(max), vt.edad_excel), CONVERT(varchar(max), va.edad), CONVERT(varchar(max), vt.edad)),
                ('nacional', CONVERT(varchar(max), va.nacional_valor), CONVERT(varchar(max), vt.nacional_excel), CONVERT(varchar(max), va.id_nacionalidad), CONVERT(varchar(max), vt.id_nacionalidad))
        ) dif(Campo, ValorAnterior, ValorNuevo, ComparacionAnterior, ComparacionNuevo)
        WHERE ISNULL(dif.ComparacionAnterior, '') <> ISNULL(dif.ComparacionNuevo, '') OPTION (RECOMPILE);
    ";

        var filas = await connection.QueryAsync<ActualizacionDiferenciaRow>(sql, contexto, commandTimeout: 180);

        return filas.ToList();
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

    private static void AplicarLimiteDiferencias(ActualizacionDiferenciasResponse response, int limitePorSeccion)
    {
        response.TotalCarpetas = response.Carpetas.Count;
        response.TotalDelitos = response.Delitos.Count;
        response.TotalVictimas = response.Victimas.Count;
        response.TotalDiferencias =
            response.TotalCarpetas +
            response.TotalDelitos +
            response.TotalVictimas;

        response.LimitePorSeccion = limitePorSeccion;

        if (limitePorSeccion == 0)
        {
            response.DetalleLimitado = response.TotalDiferencias > 0;

            response.Carpetas = new List<ActualizacionDiferenciaRegistro>();
            response.Delitos = new List<ActualizacionDiferenciaRegistro>();
            response.Victimas = new List<ActualizacionDiferenciaRegistro>();

            return;
        }

        var carpetasLimitadas = response.Carpetas
            .Take(limitePorSeccion)
            .ToList();

        var delitosLimitados = response.Delitos
            .Take(limitePorSeccion)
            .ToList();

        var victimasLimitadas = response.Victimas
            .Take(limitePorSeccion)
            .ToList();

        response.DetalleLimitado =
            response.TotalCarpetas > carpetasLimitadas.Count ||
            response.TotalDelitos > delitosLimitados.Count ||
            response.TotalVictimas > victimasLimitadas.Count;

        response.Carpetas = carpetasLimitadas;
        response.Delitos = delitosLimitados;
        response.Victimas = victimasLimitadas;
    }
}