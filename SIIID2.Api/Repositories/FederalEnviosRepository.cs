using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class FederalEnviosRepository : IFederalEnviosRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public FederalEnviosRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<List<InformePeriodoItem>> ObtenerPeriodosAsync()
    {
        const string sql = """
            SELECT DISTINCT
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte
            FROM dbo.federal_carga c
            WHERE c.activo = 1
              AND (c.estado NOT LIKE N'RECHAZADO%' OR c.estado = N'RECHAZADO_ADMIN')
              AND c.mes_corte BETWEEN 1 AND 12
              AND c.anio_corte BETWEEN 2000 AND 2100
            ORDER BY
                c.anio_corte DESC,
                c.mes_corte DESC;
            """;

        using var connection = _dbConnectionFactory.CrearConexion();

        var periodos = (await connection.QueryAsync<InformePeriodoItem>(sql)).ToList();

        foreach (var periodo in periodos)
            periodo.Corte = $"{ObtenerNombreMes(periodo.MesCorte)} {periodo.AnioCorte}";

        return periodos;
    }

    public async Task<List<InformeEnvioItem>> ObtenerEnviosAsync(int? mesCorte, int? anioCorte)
    {
        const string sql = """
            WITH ultimo_visible AS
            (
                SELECT
                    c.id_federal_carga,
                    c.codigo_referencia,
                    c.tipo_carga,
                    c.estado,
                    c.id_usuario_carga,
                    c.mes_corte,
                    c.anio_corte,
                    c.fecha_validacion,
                    c.fecha_confirmacion,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY c.mes_corte, c.anio_corte
                        ORDER BY COALESCE(c.fecha_confirmacion, c.fecha_validacion) DESC, c.id_federal_carga DESC
                    ) AS rn
                FROM dbo.federal_carga c
                WHERE c.activo = 1
                  AND c.estado NOT LIKE N'RECHAZADO%'
                  AND (@MesCorte IS NULL OR c.mes_corte = @MesCorte)
                  AND (@AnioCorte IS NULL OR c.anio_corte = @AnioCorte)
            ),
            ultimo_rechazo AS
            (
                SELECT
                    c.id_federal_carga,
                    c.codigo_referencia,
                    c.tipo_carga,
                    c.estado,
                    c.id_usuario_carga,
                    c.id_usuario_confirmacion,
                    c.mes_corte,
                    c.anio_corte,
                    c.fecha_validacion,
                    c.fecha_confirmacion,
                    c.mensaje_error,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY c.mes_corte, c.anio_corte
                        ORDER BY COALESCE(c.fecha_confirmacion, c.fecha_validacion) DESC, c.id_federal_carga DESC
                    ) AS rn
                FROM dbo.federal_carga c
                WHERE c.activo = 1
                  AND c.estado = N'RECHAZADO_ADMIN'
                  AND (@MesCorte IS NULL OR c.mes_corte = @MesCorte)
                  AND (@AnioCorte IS NULL OR c.anio_corte = @AnioCorte)
            )
            SELECT
                c.id_federal_carga AS IdCarga,
                c.codigo_referencia AS CodigoReferencia,
                c.tipo_carga AS TipoCarga,
                c.estado AS Estado,
                0 AS IdEntidadFederativa,
                N'Federal' AS EntidadFederativa,
                N'FGR' AS ClaveEntidad,
                c.fecha_validacion AS FechaEnvio,
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte,
                u.usuario AS UsuarioEnvio,
                CAST(0 AS bit) AS EsRechazadoAdministrador,
                CAST(NULL AS nvarchar(max)) AS MotivoRechazo,
                CAST(NULL AS datetime2(0)) AS FechaRechazo,
                CAST(NULL AS nvarchar(150)) AS UsuarioRechazo,
                CONVERT(bit,
                    CASE WHEN
                        EXISTS
                        (
                            SELECT 1
                            FROM dbo.federal_carga_tmp_carpeta tc
                            WHERE tc.id_federal_carga = c.id_federal_carga
                              AND tc.activo = 1
                        )
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.federal_carga_tmp_delito td
                            WHERE td.id_federal_carga = c.id_federal_carga
                              AND td.activo = 1
                        )
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.federal_carga_tmp_victima tv
                            WHERE tv.id_federal_carga = c.id_federal_carga
                              AND tv.activo = 1
                        )
                        THEN 1
                        ELSE 0
                    END) AS TieneStagingDisponible
            FROM ultimo_visible c
            INNER JOIN dbo.usuario u
                ON u.id_usuario = c.id_usuario_carga
            WHERE c.rn = 1

            UNION ALL

            SELECT
                c.id_federal_carga AS IdCarga,
                c.codigo_referencia AS CodigoReferencia,
                c.tipo_carga AS TipoCarga,
                c.estado AS Estado,
                0 AS IdEntidadFederativa,
                N'Federal' AS EntidadFederativa,
                N'FGR' AS ClaveEntidad,
                c.fecha_validacion AS FechaEnvio,
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte,
                u.usuario AS UsuarioEnvio,
                CAST(1 AS bit) AS EsRechazadoAdministrador,
                c.mensaje_error AS MotivoRechazo,
                c.fecha_confirmacion AS FechaRechazo,
                ur.usuario AS UsuarioRechazo,
                CONVERT(bit,
                    CASE WHEN
                        EXISTS
                        (
                            SELECT 1
                            FROM dbo.federal_carga_tmp_carpeta tc
                            WHERE tc.id_federal_carga = c.id_federal_carga
                              AND tc.activo = 1
                        )
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.federal_carga_tmp_delito td
                            WHERE td.id_federal_carga = c.id_federal_carga
                              AND td.activo = 1
                        )
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.federal_carga_tmp_victima tv
                            WHERE tv.id_federal_carga = c.id_federal_carga
                              AND tv.activo = 1
                        )
                        THEN 1
                        ELSE 0
                    END) AS TieneStagingDisponible
            FROM ultimo_rechazo c
            INNER JOIN dbo.usuario u
                ON u.id_usuario = c.id_usuario_carga
            LEFT JOIN dbo.usuario ur
                ON ur.id_usuario = c.id_usuario_confirmacion
            WHERE c.rn = 1

            ORDER BY
                AnioCorte DESC,
                MesCorte DESC,
                FechaEnvio DESC
            OPTION (RECOMPILE);
            """;

        using var connection = _dbConnectionFactory.CrearConexion();

        var envios = (await connection.QueryAsync<InformeEnvioItem>(sql, new
        {
            MesCorte = mesCorte,
            AnioCorte = anioCorte
        })).ToList();

        foreach (var envio in envios)
        {
            envio.FechaEnvioTexto = envio.FechaEnvio.ToString("dd-MM-yyyy");
            envio.FechaRechazoTexto = envio.FechaRechazo?.ToString("dd-MM-yyyy") ?? string.Empty;
            envio.Corte = $"{ObtenerNombreMes(envio.MesCorte)} {envio.AnioCorte}";

            envio.EsConfirmado =
                string.Equals(envio.Estado, "CONFIRMADO", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(envio.Estado, "CONFIRMADO_ACTUALIZACION", StringComparison.OrdinalIgnoreCase);

            envio.EstadoTexto = ObtenerEstadoTexto(envio.Estado, envio.TipoCarga);

            if (envio.EsRechazadoAdministrador)
            {
                envio.EndpointAcuse = envio.TieneStagingDisponible && string.Equals(envio.TipoCarga, "CARGA_INICIAL", StringComparison.OrdinalIgnoreCase)
                    ? $"/api/federal/cargas/{envio.CodigoReferencia}/acuse"
                    : string.Empty;

                envio.EndpointExcel = envio.TieneStagingDisponible ? $"/api/federal/informes/envios/{envio.CodigoReferencia}/archivos" : string.Empty;
                continue;
            }

            if (string.Equals(envio.TipoCarga, "CARGA_INICIAL", StringComparison.OrdinalIgnoreCase))
            {
                envio.EndpointAcuse = envio.EsConfirmado
                    ? $"/api/federal/cargas/{envio.CodigoReferencia}/acuse-confirmado"
                    : $"/api/federal/cargas/{envio.CodigoReferencia}/acuse";
            }
            else
            {
                envio.EndpointAcuse = string.Empty;
            }

            envio.EndpointExcel = $"/api/federal/informes/envios/{envio.CodigoReferencia}/archivos";
        }

        return envios;
    }

    public async Task<InformeArchivoCargaInfo?> ObtenerCargaParaArchivosAsync(string codigoReferencia)
    {
        const string sql = """
        SELECT TOP (1)
            c.id_federal_carga AS IdCarga,
            c.codigo_referencia AS CodigoReferencia,
            c.tipo_carga AS TipoCarga,
            c.estado AS Estado,
            CAST(0 AS int) AS IdEntidadFederativa,
            c.mes_corte AS MesCorte,
            c.anio_corte AS AnioCorte,
            N'Federal' AS EntidadFederativa,
            CONVERT(bit,
                CASE WHEN
                    EXISTS
                    (
                        SELECT 1
                        FROM dbo.federal_carga_tmp_carpeta tc
                        WHERE tc.id_federal_carga = c.id_federal_carga
                          AND tc.activo = 1
                    )
                    OR EXISTS
                    (
                        SELECT 1
                        FROM dbo.federal_carga_tmp_delito td
                        WHERE td.id_federal_carga = c.id_federal_carga
                          AND td.activo = 1
                    )
                    OR EXISTS
                    (
                        SELECT 1
                        FROM dbo.federal_carga_tmp_victima tv
                        WHERE tv.id_federal_carga = c.id_federal_carga
                          AND tv.activo = 1
                    )
                    THEN 1
                    ELSE 0
                END) AS TieneStagingDisponible
        FROM dbo.federal_carga c
        WHERE c.codigo_referencia = @CodigoReferencia
          AND c.activo = 1
          AND c.estado IN
          (
              N'VALIDADO_PENDIENTE',
              N'VALIDADO_PENDIENTE_ACTUALIZACION',
              N'PENDIENTE_APROBACION',
              N'CONFIRMADO',
              N'CONFIRMADO_ACTUALIZACION',
              N'RECHAZADO_ADMIN'
          )
        ORDER BY c.id_federal_carga DESC;
        """;

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QueryFirstOrDefaultAsync<InformeArchivoCargaInfo>(sql, new { CodigoReferencia = codigoReferencia });
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerCarpetasConfirmadasPeriodoAsync(int mesCorte, int anioCorte)
    {
        const string sql = """
        WITH cargas_periodo AS
        (
            SELECT id_federal_carga
            FROM dbo.federal_carga
            WHERE mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND activo = 1
              AND
              (
                    (tipo_carga = N'CARGA_INICIAL' AND estado = N'CONFIRMADO')
                 OR (tipo_carga = N'ACTUALIZACION' AND estado = N'CONFIRMADO_ACTUALIZACION')
              )
        )
        SELECT
            ci.identificador_carpeta_fiscalia AS id_ci,
            ci.nomenclatura_carpeta_fiscalia AS ntra_ci,
            ISNULL(CONVERT(varchar(10), ci.fecha_inicio, 103), '') AS fha_de_ini,
            CASE
                WHEN ci.fecha_inicio IS NULL THEN ''
                WHEN CONVERT(time, ci.fecha_inicio) = '00:00:00' THEN ''
                ELSE CONVERT(varchar(8), ci.fecha_inicio, 108)
            END AS hra_de_ini,
            ISNULL(ci.resumen_hechos, '') AS rmen_de_hchos
        FROM dbo.federal_carpeta_investigacion ci
        INNER JOIN cargas_periodo cp
            ON cp.id_federal_carga = ci.id_federal_carga
        WHERE ci.activo = 1
        ORDER BY ci.identificador_carpeta_fiscalia;
        """;

        return await QueryDictionaryAsync(sql, new { MesCorte = mesCorte, AnioCorte = anioCorte });
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerDelitosConfirmadosPeriodoAsync(int mesCorte, int anioCorte)
    {
        const string sql = """
        WITH cargas_periodo AS
        (
            SELECT id_federal_carga
            FROM dbo.federal_carga
            WHERE mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND activo = 1
              AND
              (
                    (tipo_carga = N'CARGA_INICIAL' AND estado = N'CONFIRMADO')
                 OR (tipo_carga = N'ACTUALIZACION' AND estado = N'CONFIRMADO_ACTUALIZACION')
              )
        )
        SELECT
            ci.identificador_carpeta_fiscalia AS id_ci,
            d.identificador_delito_fiscalia AS id_delito,
            ISNULL(d.delito_fiscalia, '') AS dto,
            ISNULL(d.modalidad_delito_fiscalia, '') AS moda_dto,
            CONVERT(varchar(10), fa.clave) AS forma_acc,
            ISNULL(CONVERT(varchar(10), d.fecha_hechos, 103), '') AS fha_de_hchos,
            CASE WHEN d.fecha_hechos IS NULL THEN '' ELSE CONVERT(varchar(8), d.fecha_hechos, 108) END AS hra_de_hchos,
            CONVERT(varchar(10), ic.clave) AS emto_com_dto,
            CONVERT(varchar(10), gc.clave) AS grdo_cons,
            md.clave4 AS clasf_de_dto,
            CONVERT(varchar(10), d.id_entidad_federativa) AS id_ent_hchos,
            mun.clave AS id_mun_hchos,
            ISNULL(d.id_localidad_fiscalia, '') AS id_loc_hchos,
            ISNULL(d.localidad_fiscalia_nombre, '') AS nom_loc_hchos,
            ISNULL(d.id_colonia_fiscalia, '') AS id_col_hchos,
            ISNULL(d.colonia_fiscalia_nombre, '') AS nom_col_hchos,
            ISNULL(d.codigo_postal_fiscalia, '') AS cp,
            ISNULL(CONVERT(varchar(50), d.coordenada_x), '') AS coord_x,
            ISNULL(CONVERT(varchar(50), d.coordenada_y), '') AS coord_y,
            ISNULL(d.domicilio_hechos, '') AS dom_hchos
        FROM dbo.federal_delito d
        INNER JOIN cargas_periodo cp
            ON cp.id_federal_carga = d.id_federal_carga
        INNER JOIN dbo.federal_carpeta_investigacion ci
            ON ci.id_federal_carpeta_investigacion = d.id_federal_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN dbo.federal_catalogo_modalidad_delito md
            ON md.id_modalidad_delito = d.id_modalidad_delito
        INNER JOIN dbo.catalogo_forma_accion fa
            ON fa.id_forma_accion = d.id_forma_accion
        INNER JOIN dbo.catalogo_instrumento_comision ic
            ON ic.id_instrumento_comision = d.id_instrumento_comision
        INNER JOIN dbo.catalogo_grado_consumacion gc
            ON gc.id_grado_consumacion = d.id_grado_consumacion
        INNER JOIN dbo.catalogo_municipio mun
            ON mun.id_municipio = d.id_municipio
        WHERE d.activo = 1
        ORDER BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia;
        """;

        return await QueryDictionaryAsync(sql, new { MesCorte = mesCorte, AnioCorte = anioCorte });
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerVictimasConfirmadasPeriodoAsync(int mesCorte, int anioCorte)
    {
        const string sql = """
        WITH cargas_periodo AS
        (
            SELECT id_federal_carga
            FROM dbo.federal_carga
            WHERE mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND activo = 1
              AND
              (
                    (tipo_carga = N'CARGA_INICIAL' AND estado = N'CONFIRMADO')
                 OR (tipo_carga = N'ACTUALIZACION' AND estado = N'CONFIRMADO_ACTUALIZACION')
              )
        )
        SELECT
            ci.identificador_carpeta_fiscalia AS id_ci,
            d.identificador_delito_fiscalia AS id_delito,
            v.identificador_victima_fiscalia AS id_vicf,
            CONVERT(varchar(10), tv.clave) AS id_tv,
            CONVERT(varchar(10), tvm.clave) AS id_tpm,
            CONVERT(varchar(10), sx.clave) AS sexo,
            CONVERT(varchar(10), gen.clave) AS genero,
            CONVERT(varchar(10), pob.clave) AS pob,
            CONVERT(varchar(10), disc.clave) AS disc,
            ISNULL(CONVERT(varchar(10), v.fecha_nacimiento, 103), '') AS fha_nac,
            ISNULL(CONVERT(varchar(10), v.edad), '') AS edad,
            ISNULL(CONVERT(varchar(20), nac.clave), '') AS nacional
        FROM dbo.federal_victima v
        INNER JOIN cargas_periodo cp
            ON cp.id_federal_carga = v.id_federal_carga
        INNER JOIN dbo.federal_delito d
            ON d.id_federal_delito = v.id_federal_delito
           AND d.activo = 1
        INNER JOIN dbo.federal_carpeta_investigacion ci
            ON ci.id_federal_carpeta_investigacion = d.id_federal_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN dbo.catalogo_tipo_victima tv
            ON tv.id_tipo_victima = v.id_tipo_victima
        LEFT JOIN dbo.catalogo_tipo_victima_moral tvm
            ON tvm.id_tipo_victima_moral = v.id_tipo_victima_moral
        LEFT JOIN dbo.catalogo_sexo sx
            ON sx.id_sexo = v.id_sexo
        LEFT JOIN dbo.catalogo_genero gen
            ON gen.id_genero = v.id_genero
        LEFT JOIN dbo.catalogo_pertenece_poblacion_indigena pob
            ON pob.id_pertenece_poblacion_indigena = v.id_pertenece_poblacion_indigena
        LEFT JOIN dbo.catalogo_presenta_discapacidad disc
            ON disc.id_presenta_discapacidad = v.id_presenta_discapacidad
        LEFT JOIN dbo.catalogo_nacionalidad nac
            ON nac.id_nacionalidad = v.id_nacionalidad
        WHERE v.activo = 1
        ORDER BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia, v.identificador_victima_fiscalia;
        """;

        return await QueryDictionaryAsync(sql, new { MesCorte = mesCorte, AnioCorte = anioCorte });
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerCarpetasStagingAsync(long idFederalCarga)
    {
        const string sql = """
        SELECT
            id_ci,
            ntra_ci,
            fha_de_ini,
            hra_de_ini,
            rmen_de_hchos
        FROM dbo.federal_carga_tmp_carpeta
        WHERE id_federal_carga = @IdFederalCarga
          AND activo = 1
        ORDER BY numero_fila;
        """;

        return await QueryDictionaryAsync(sql, new { IdFederalCarga = idFederalCarga });
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerDelitosStagingAsync(long idFederalCarga)
    {
        const string sql = """
        SELECT
            id_ci,
            id_delito,
            dto,
            moda_dto,
            forma_acc,
            fha_de_hchos,
            hra_de_hchos,
            emto_com_dto,
            grdo_cons,
            clasf_de_dto,
            id_ent_hchos,
            id_mun_hchos,
            id_loc_hchos,
            nom_loc_hchos,
            id_col_hchos,
            nom_col_hchos,
            cp,
            coord_x,
            coord_y,
            dom_hchos
        FROM dbo.federal_carga_tmp_delito
        WHERE id_federal_carga = @IdFederalCarga
          AND activo = 1
        ORDER BY numero_fila;
        """;

        return await QueryDictionaryAsync(sql, new { IdFederalCarga = idFederalCarga });
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerVictimasStagingAsync(long idFederalCarga)
    {
        const string sql = """
        SELECT
            id_ci,
            id_delito,
            id_vicf,
            id_tv,
            id_tpm,
            sexo,
            genero,
            pob,
            disc,
            fha_nac,
            edad,
            nacional
        FROM dbo.federal_carga_tmp_victima
        WHERE id_federal_carga = @IdFederalCarga
          AND activo = 1
        ORDER BY numero_fila;
        """;

        return await QueryDictionaryAsync(sql, new { IdFederalCarga = idFederalCarga });
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryAsync(string sql, object parametros)
    {
        using var connection = _dbConnectionFactory.CrearConexion();
        var filas = await connection.QueryAsync(sql, parametros);

        return filas
            .Select(fila => ((IDictionary<string, object?>)fila).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase))
            .Cast<IDictionary<string, object?>>()
            .ToList();
    }

    private static string ObtenerEstadoTexto(string estado, string tipoCarga)
    {
        var estadoNormalizado = (estado ?? string.Empty).Trim().ToUpperInvariant();
        var esActualizacion = string.Equals(tipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase);
        var sufijo = esActualizacion ? "actualización" : "carga";

        return estadoNormalizado switch
        {
            "CONFIRMADO" => $"Confirmado {sufijo}",
            "CONFIRMADO_ACTUALIZACION" => $"Confirmado {sufijo}",
            "PENDIENTE_APROBACION" => "Pendiente de aprobación",
            "VALIDADO_PENDIENTE" => $"Pendiente {sufijo}",
            "VALIDADO_PENDIENTE_ACTUALIZACION" => $"Pendiente {sufijo}",
            "RECHAZADO_ADMIN" => "Rechazado por administración",
            _ => estadoNormalizado.Replace("_", " ")
        };
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