using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;
// Coordinación y dependencias compartidas; las operaciones están separadas en archivos parciales.
public partial class InformeRepository : IInformeRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    public InformeRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<List<InformePeriodoItem>> ObtenerPeriodosEnviosAsync(bool esSuperUsuario, int? idEntidadFederativaUsuario)
    {
        const string sql = @"
        SELECT DISTINCT
            c.mes_corte AS MesCorte,
            c.anio_corte AS AnioCorte
        FROM dbo.carga c
        WHERE c.activo = 1
          AND (c.estado NOT LIKE N'RECHAZADO%' OR c.estado = N'RECHAZADO_ADMIN')
          AND (@EsSuperUsuario = 1 OR c.id_entidad_federativa = @IdEntidadFederativaUsuario)
          AND c.mes_corte BETWEEN 1 AND 12
          AND c.anio_corte BETWEEN 2000 AND 2100
        ORDER BY c.anio_corte DESC, c.mes_corte DESC;
    ";
        using var connection = _dbConnectionFactory.CrearConexion();
        var periodos = (await connection.QueryAsync<InformePeriodoItem>(sql, new { EsSuperUsuario = esSuperUsuario, IdEntidadFederativaUsuario = idEntidadFederativaUsuario })).ToList();
        foreach (var periodo in periodos)
            periodo.Corte = $"{ObtenerNombreMes(periodo.MesCorte)} {periodo.AnioCorte}";
        return periodos;
    }

    public async Task<List<InformeEnvioItem>> ObtenerEnviosAsync(bool esSuperUsuario,
        int? idEntidadFederativaUsuario,
        int? idEntidadFederativa,
        int? mesCorte,
        int? anioCorte)
    {
        // Obtiene el último envío confirmado por entidad y periodo.
        // Considera tanto carga inicial confirmada como actualizaciones confirmadas.
        // Si hay varias actualizaciones confirmadas para el mismo corte,
        // se toma únicamente la más reciente.
        var sql = @"
    WITH ultimo_visible AS
    (
        SELECT
            c.id_carga,
            c.codigo_referencia,
            c.tipo_carga,
            c.estado,
            c.id_entidad_federativa,
            c.id_usuario_carga,
            c.mes_corte,
            c.anio_corte,
            c.fecha_validacion AS fecha_envio,
            ROW_NUMBER() OVER
            (
                PARTITION BY c.id_entidad_federativa, c.mes_corte, c.anio_corte
                ORDER BY COALESCE(c.fecha_confirmacion, c.fecha_validacion) DESC, c.id_carga DESC
            ) AS rn
        FROM carga c
        WHERE c.activo = 1
          AND c.estado NOT LIKE 'RECHAZADO%'
          AND (@EsSuperUsuario = 1 OR c.id_entidad_federativa = @IdEntidadFederativaUsuario)
          AND (@IdEntidadFederativa IS NULL OR c.id_entidad_federativa = @IdEntidadFederativa)
          AND (@MesCorte IS NULL OR c.mes_corte = @MesCorte)
          AND (@AnioCorte IS NULL OR c.anio_corte = @AnioCorte)
    ),
    envios AS
    (
        SELECT
            v.*,
            conf.codigo_referencia AS codigo_referencia_confirmada,
            conf.tipo_carga AS tipo_carga_confirmada
        FROM ultimo_visible v
        OUTER APPLY
        (
            SELECT TOP (1)
                c2.codigo_referencia,
                c2.tipo_carga
            FROM carga c2
            WHERE c2.activo = 1
              AND c2.id_entidad_federativa = v.id_entidad_federativa
              AND c2.mes_corte = v.mes_corte
              AND c2.anio_corte = v.anio_corte
              AND c2.id_carga <> v.id_carga
              AND
              (
                    (c2.tipo_carga = 'CARGA_INICIAL' AND c2.estado = 'CONFIRMADO')
                 OR (c2.tipo_carga = 'ACTUALIZACION' AND c2.estado = 'CONFIRMADO_ACTUALIZACION')
              )
            ORDER BY COALESCE(c2.fecha_confirmacion, c2.fecha_validacion) DESC, c2.id_carga DESC
        ) conf
        WHERE v.rn = 1
    )
    SELECT
        e.id_carga AS IdCarga,
        e.codigo_referencia AS CodigoReferencia,
        e.tipo_carga AS TipoCarga,
        e.estado AS Estado,
        e.codigo_referencia_confirmada AS CodigoReferenciaConfirmada,
        e.tipo_carga_confirmada AS TipoCargaConfirmada,
        e.id_entidad_federativa AS IdEntidadFederativa,
        ef.nombre AS EntidadFederativa,
        ef.clave AS ClaveEntidad,
        e.fecha_envio AS FechaEnvio,
        e.mes_corte AS MesCorte,
        e.anio_corte AS AnioCorte,
        ucarga.usuario AS UsuarioEnvio
    FROM envios e
    INNER JOIN usuario ucarga ON ucarga.id_usuario = e.id_usuario_carga
    INNER JOIN catalogo_entidad_federativa ef ON ef.id_entidad_federativa = e.id_entidad_federativa
    ORDER BY ef.nombre, e.anio_corte DESC, e.mes_corte DESC
    OPTION (RECOMPILE);
";
        using var connection = _dbConnectionFactory.CrearConexion();
        var envios = await connection.QueryAsync<InformeEnvioItem>(sql, new { EsSuperUsuario = esSuperUsuario, IdEntidadFederativaUsuario = idEntidadFederativaUsuario, IdEntidadFederativa = esSuperUsuario ? idEntidadFederativa : null, MesCorte = mesCorte, AnioCorte = anioCorte });
        return envios.Select(x =>
        {
            x.FechaEnvioTexto = x.FechaEnvio.ToString("dd-MM-yyyy");
            x.Corte = $"{ObtenerNombreMes(x.MesCorte)} {x.AnioCorte}";
            x.EsConfirmado = string.Equals(x.Estado, "CONFIRMADO", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Estado, "CONFIRMADO_ACTUALIZACION", StringComparison.OrdinalIgnoreCase);
            x.EstadoTexto = ObtenerEstadoEnvioTexto(x.Estado, x.TipoCarga);
            if(x.EsConfirmado)
            {
                x.EndpointAcuse = ObtenerEndpointAcuse(x.TipoCarga, x.CodigoReferencia);
                x.EndpointExcel = $"/api/informes/envios/{x.CodigoReferencia}/archivos";
            }
            else
            {
                x.EndpointAcuse = ObtenerEndpointAcusePrevio(x.TipoCarga, x.CodigoReferencia);
                x.EndpointExcel = $"/api/informes/envios/{x.CodigoReferencia}/archivos";
            }

            return x;
        }).ToList();
    }

    private static string ObtenerEndpointAcuse(string tipoCarga, string codigoReferencia)
    {
        if(string.Equals(tipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase))
        {
            return $"/api/actualizaciones/{codigoReferencia}/acuse-confirmado";
        }

        return $"/api/cargas/{codigoReferencia}/acuse-confirmado";
    }

    private static string ObtenerEstadoEnvioTexto(string estado, string tipoCarga)
    {
        var estadoNormalizado = (estado ?? string.Empty).Trim().ToUpperInvariant();
        var esActualizacion = string.Equals(tipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase);
        var sufijo = esActualizacion ? "actualización" : "carga";
        return estadoNormalizado switch        {
            "CONFIRMADO" => $"Confirmado {sufijo}",
            "CONFIRMADO_ACTUALIZACION" => $"Confirmado {sufijo}",
            "PENDIENTE_APROBACION" => "Pendiente de aprobación",
            "VALIDADO_PENDIENTE" => $"Pendiente {sufijo}",
            _ => estadoNormalizado.Replace("_", " ")};
    }

    private static string ObtenerNombreMes(int mes)
    {
        return mes switch        {
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

    public async Task<InformeArchivoCargaInfo?> ObtenerCargaConfirmadaParaArchivosAsync(string codigoReferencia)
    {
        // Obtiene una carga o actualización activa no rechazada.
        // Las cargas confirmadas se descargan desde tablas finales;
        // las pendientes se descargan desde tablas temporales.
        var sql = @"
        SELECT
            c.id_carga AS IdCarga,
            c.codigo_referencia AS CodigoReferencia,
            c.tipo_carga AS TipoCarga,
            c.estado AS Estado,
            c.id_entidad_federativa AS IdEntidadFederativa,
            ef.nombre AS EntidadFederativa,
            c.mes_corte AS MesCorte,
            c.anio_corte AS AnioCorte
        FROM carga c
        INNER JOIN catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = c.id_entidad_federativa
        WHERE c.codigo_referencia = @CodigoReferencia
          AND c.activo = 1
          AND c.estado NOT LIKE 'RECHAZADO%';
    ";
        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QueryFirstOrDefaultAsync<InformeArchivoCargaInfo>(sql, new { CodigoReferencia = codigoReferencia });
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerCarpetasConfirmadasPeriodoAsync(InformeArchivoCargaInfo carga)
    {
        // Reconstruye el archivo de carpetas desde tabla final.
        // Se toman todas las carpetas activas del periodo de la carga/actualización.
        var sql = @"
        WITH cargas_periodo AS (
            SELECT
                id_carga
            FROM carga
            WHERE id_entidad_federativa = @IdEntidadFederativa
              AND mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND activo = 1
              AND (
                    (tipo_carga = 'CARGA_INICIAL' AND estado = 'CONFIRMADO')
                 OR (tipo_carga = 'ACTUALIZACION' AND estado = 'CONFIRMADO_ACTUALIZACION')
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
            ci.resumen_hechos AS rmen_de_hchos
        FROM carpeta_investigacion ci
        INNER JOIN cargas_periodo cp
            ON cp.id_carga = ci.id_carga
        WHERE ci.activo = 1
        ORDER BY
            ci.identificador_carpeta_fiscalia;
    ";
        return await QueryDictionaryAsync(sql, carga);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerDelitosConfirmadosPeriodoAsync(InformeArchivoCargaInfo carga)
    {
        // Reconstruye el archivo de delitos desde tabla final.
        // Para catálogos, se regresan las claves originales esperadas por Excel.
        var sql = @"
        WITH cargas_periodo AS (
            SELECT
                id_carga
            FROM carga
            WHERE id_entidad_federativa = @IdEntidadFederativa
              AND mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND activo = 1
              AND (
                    (tipo_carga = 'CARGA_INICIAL' AND estado = 'CONFIRMADO')
                 OR (tipo_carga = 'ACTUALIZACION' AND estado = 'CONFIRMADO_ACTUALIZACION')
              )
        )
        SELECT
            ci.identificador_carpeta_fiscalia AS id_ci,
            d.identificador_delito_fiscalia AS id_delito,
            d.delito_fiscalia AS dto,
            d.modalidad_delito_fiscalia AS moda_dto,
            CONVERT(varchar(10), fa.clave) AS forma_acc,
            CONVERT(varchar(10), d.fecha_hechos, 103) AS fha_de_hchos,
            CONVERT(varchar(8), d.fecha_hechos, 108) AS hra_de_hchos,
            CONVERT(varchar(10), ic.clave) AS emto_com_dto,
            CONVERT(varchar(10), gc.clave) AS grdo_cons,
            md.clave4 AS clasf_de_dto,

            ef.nombre AS nom_ent_hchos,
            CONVERT(varchar(10), d.id_entidad_federativa) AS id_ent_hchos,

            mun.nombre AS nom_mun_hchos,
            mun.clave AS id_mun_hchos,

            d.localidad_fiscalia_nombre AS nom_loc_hchos,
            d.id_localidad_fiscalia AS id_loc_hchos,

            d.colonia_fiscalia_nombre AS nom_col_hchos,
            d.id_colonia_fiscalia AS id_col_hchos,
            cp.codigo_postal AS cp,
            d.coordenada_x AS coord_x,
            d.coordenada_y AS coord_y,
            d.domicilio_hechos AS dom_hchos
        FROM delito d
        INNER JOIN cargas_periodo cper
            ON cper.id_carga = d.id_carga
        INNER JOIN carpeta_investigacion ci
            ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN catalogo_modalidad_delito md
            ON md.id_modalidad_delito = d.id_modalidad_delito
        INNER JOIN catalogo_forma_accion fa
            ON fa.id_forma_accion = d.id_forma_accion
        INNER JOIN catalogo_instrumento_comision ic
            ON ic.id_instrumento_comision = d.id_instrumento_comision
        INNER JOIN catalogo_grado_consumacion gc
            ON gc.id_grado_consumacion = d.id_grado_consumacion

        INNER JOIN catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = d.id_entidad_federativa
           AND ef.activo = 1

        INNER JOIN catalogo_municipio mun
            ON mun.id_municipio = d.id_municipio
           AND mun.id_entidad_federativa = ef.id_entidad_federativa
           AND mun.activo = 1

        LEFT JOIN catalogo_codigo_postal cp
            ON cp.id_codigo_postal = d.id_codigo_postal
        WHERE d.activo = 1
        ORDER BY
            ci.identificador_carpeta_fiscalia,
            d.identificador_delito_fiscalia;
    ";
        return await QueryDictionaryAsync(sql, carga);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerVictimasConfirmadasPeriodoAsync(InformeArchivoCargaInfo carga)
    {
        // Reconstruye el archivo de víctimas desde tabla final.
        // Para catálogos, se regresan las claves originales esperadas por Excel.
        var sql = @"
        WITH cargas_periodo AS (
            SELECT
                id_carga
            FROM carga
            WHERE id_entidad_federativa = @IdEntidadFederativa
              AND mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND activo = 1
              AND (
                    (tipo_carga = 'CARGA_INICIAL' AND estado = 'CONFIRMADO')
                 OR (tipo_carga = 'ACTUALIZACION' AND estado = 'CONFIRMADO_ACTUALIZACION')
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
            CONVERT(varchar(10), v.fecha_nacimiento, 103) AS fha_nac,
            v.edad AS edad,
            nac.clave AS nacional,
            v.nombre_vicfem AS nombre_vicfem,
            v.primer_apellido_vicfem AS [1apellido_vicfem],
            v.segundo_apellido_vicfem AS [2apellido_vicfem],
            v.curp_vicfem AS curp_vicfem
        FROM victima v
        INNER JOIN cargas_periodo cper
            ON cper.id_carga = v.id_carga
        INNER JOIN delito d
            ON d.id_delito = v.id_delito
           AND d.activo = 1
        INNER JOIN carpeta_investigacion ci
            ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN catalogo_tipo_victima tv
            ON tv.id_tipo_victima = v.id_tipo_victima
        LEFT JOIN catalogo_tipo_victima_moral tvm
            ON tvm.id_tipo_victima_moral = v.id_tipo_victima_moral
        LEFT JOIN catalogo_sexo sx
            ON sx.id_sexo = v.id_sexo
        LEFT JOIN catalogo_genero gen
            ON gen.id_genero = v.id_genero
        LEFT JOIN catalogo_pertenece_poblacion_indigena pob
            ON pob.id_pertenece_poblacion_indigena = v.id_pertenece_poblacion_indigena
        LEFT JOIN catalogo_presenta_discapacidad disc
            ON disc.id_presenta_discapacidad = v.id_presenta_discapacidad
        LEFT JOIN catalogo_nacionalidad nac
            ON nac.id_nacionalidad = v.id_nacionalidad
        WHERE v.activo = 1
        ORDER BY
            ci.identificador_carpeta_fiscalia,
            d.identificador_delito_fiscalia,
            v.identificador_victima_fiscalia;
    ";
        return await QueryDictionaryAsync(sql, carga);
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryAsync(string sql, InformeArchivoCargaInfo carga)
    {
        using var connection = _dbConnectionFactory.CrearConexion();
        var filas = await connection.QueryAsync(sql, new { carga.IdEntidadFederativa, carga.MesCorte, carga.AnioCorte });
        return filas.Select(fila => ((IDictionary<string, object?>)fila).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)).Cast<IDictionary<string, object?>>().ToList();
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryAnioAsync(string sql,
        int anioCorte,
        int? idEntidadFederativa = null,
        string modoPlano = "CONFIRMADO",
        int mesUltimoCorte = 0)
    {
        using var connection = _dbConnectionFactory.CrearConexion();
        var filas = await connection.QueryAsync(sql, new { AnioCorte = anioCorte, IdEntidadFederativa = idEntidadFederativa, ModoPlano = modoPlano, MesUltimoCorte = mesUltimoCorte }, commandTimeout: 300);
        return filas.Select(fila => ((IDictionary<string, object?>)fila).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)).Cast<IDictionary<string, object?>>().ToList();
    }

    public async Task<List<InformeReporteCargaItem>> ObtenerReporteCargasAsync(int? idEntidadFederativa, int? mesCorte, int? anioCorte)
    {
        var sql = @"
    WITH periodos_disponibles AS
    (
        SELECT DISTINCT
            c.mes_corte,
            c.anio_corte
        FROM dbo.carga c
        WHERE c.activo = 1
          AND c.mes_corte IS NOT NULL
          AND c.anio_corte IS NOT NULL
          AND (@MesCorte IS NULL OR c.mes_corte = @MesCorte)
          AND (@AnioCorte IS NULL OR c.anio_corte = @AnioCorte)
    ),
    entidades AS
    (
        SELECT
            ef.id_entidad_federativa,
            ef.nombre AS entidad_federativa,
            ef.clave AS clave_entidad
        FROM dbo.catalogo_entidad_federativa ef
        WHERE ef.activo = 1
          AND TRY_CONVERT(int, ef.clave) BETWEEN 1 AND 32
          AND (@IdEntidadFederativa IS NULL OR ef.id_entidad_federativa = @IdEntidadFederativa)
    ),
    periodos AS
    (
        SELECT
            entidad.id_entidad_federativa,
            entidad.entidad_federativa,
            entidad.clave_entidad,
            periodo.mes_corte,
            periodo.anio_corte,
            COUNT(DISTINCT carga.id_carga) AS intentos
        FROM entidades entidad
        CROSS JOIN periodos_disponibles periodo
        LEFT JOIN dbo.carga carga
            ON carga.id_entidad_federativa = entidad.id_entidad_federativa
           AND carga.mes_corte = periodo.mes_corte
           AND carga.anio_corte = periodo.anio_corte
           AND carga.activo = 1
        GROUP BY
            entidad.id_entidad_federativa,
            entidad.entidad_federativa,
            entidad.clave_entidad,
            periodo.mes_corte,
            periodo.anio_corte
    )
    SELECT
        periodo.id_entidad_federativa AS IdEntidadFederativa,
        periodo.entidad_federativa AS EntidadFederativa,
        periodo.clave_entidad AS ClaveEntidad,
        periodo.mes_corte AS MesCorte,
        periodo.anio_corte AS AnioCorte,
        periodo.intentos AS Intentos,
        ultimo.codigo_referencia AS UltimoIntento,
        ultimo.tipo_carga AS TipoCargaUltimoIntento,
        ultimo.estado AS EstatusUltimoIntento,
        ultimo.fecha_carga_actualizacion AS FechaCargaActualizacion,
        ultimo.fecha_aprobacion AS FechaAprobacion,
        exitosa.fecha_carga_exitosa AS FechaCargaExitosa
    FROM periodos periodo
    OUTER APPLY
    (
        SELECT TOP (1)
            carga.codigo_referencia,
            carga.tipo_carga,
            carga.estado,
            CASE
                WHEN carga.estado IN
                (
                    N'VALIDADO_PENDIENTE',
                    N'VALIDADO_PENDIENTE_ACTUALIZACION',
                    N'PENDIENTE_APROBACION',
                    N'CONFIRMADO',
                    N'CONFIRMADO_ACTUALIZACION',
                    N'RECHAZADO_ADMIN'
                )
                THEN carga.fecha_validacion
                ELSE NULL
            END AS fecha_carga_actualizacion,
            CASE
                WHEN carga.estado IN
                (
                    N'CONFIRMADO',
                    N'CONFIRMADO_ACTUALIZACION'
                )
                THEN carga.fecha_confirmacion
                ELSE NULL
            END AS fecha_aprobacion
        FROM dbo.carga carga
        WHERE carga.activo = 1
          AND carga.id_entidad_federativa = periodo.id_entidad_federativa
          AND carga.mes_corte = periodo.mes_corte
          AND carga.anio_corte = periodo.anio_corte
        ORDER BY
            COALESCE(carga.fecha_confirmacion, carga.fecha_validacion) DESC,
            carga.id_carga DESC
    ) ultimo
    OUTER APPLY
    (
        SELECT MIN(carga.fecha_validacion) AS fecha_carga_exitosa
        FROM dbo.carga carga
        WHERE carga.activo = 1
          AND carga.id_entidad_federativa = periodo.id_entidad_federativa
          AND carga.mes_corte = periodo.mes_corte
          AND carga.anio_corte = periodo.anio_corte
          AND carga.estado IN
          (
              N'VALIDADO_PENDIENTE',
              N'VALIDADO_PENDIENTE_ACTUALIZACION',
              N'PENDIENTE_APROBACION',
              N'CONFIRMADO',
              N'CONFIRMADO_ACTUALIZACION'
          )
    ) exitosa
    ORDER BY
        periodo.anio_corte DESC,
        periodo.mes_corte DESC,
        periodo.entidad_federativa;
    ";
        using var connection = _dbConnectionFactory.CrearConexion();
        var reporte = await connection.QueryAsync<InformeReporteCargaItem>(sql, new { IdEntidadFederativa = idEntidadFederativa, MesCorte = mesCorte, AnioCorte = anioCorte });
        return reporte.Select(x =>
        {
            x.Corte = $"{ObtenerNombreMes(x.MesCorte)} {x.AnioCorte}";
            x.FechaCargaActualizacionTexto = x.FechaCargaActualizacion.HasValue ? x.FechaCargaActualizacion.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
            x.FechaAprobacionTexto = x.FechaAprobacion.HasValue ? x.FechaAprobacion.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
            return x;
        }).ToList();
    }

    public async Task<InformeSabanaFirma> ObtenerFirmaSabanaAsync(int anioCorte, int? idEntidadFederativa = null)
    {
        var sql = @"
        WITH cargas_relevantes AS (
            SELECT
                c.id_carga,
                c.tipo_carga,
                c.estado,
                c.mes_corte,
                c.fecha_confirmacion,
                c.fecha_validacion
            FROM carga c
            WHERE c.activo = 1
              AND c.anio_corte = @AnioCorte
              AND (@IdEntidadFederativa IS NULL OR c.id_entidad_federativa = @IdEntidadFederativa)
              AND (
                     (c.tipo_carga = 'CARGA_INICIAL' AND c.estado = 'CONFIRMADO')
                  OR (c.tipo_carga = 'ACTUALIZACION' AND c.estado = 'CONFIRMADO_ACTUALIZACION')
                  OR c.estado = 'PENDIENTE_APROBACION'
              )
        ),
        ultimo_corte AS (
            SELECT MAX(mes_corte) AS MesUltimoCorte
            FROM cargas_relevantes
        )
        SELECT
            ISNULL(MAX(cr.id_carga), 0) AS UltimoIdCarga,

            CONVERT(bigint, ISNULL(SUM(CASE
                WHEN (cr.tipo_carga = 'CARGA_INICIAL' AND cr.estado = 'CONFIRMADO')
                  OR (cr.tipo_carga = 'ACTUALIZACION' AND cr.estado = 'CONFIRMADO_ACTUALIZACION')
                THEN 1 ELSE 0
            END), 0)) AS TotalCargasConfirmadas,

            CONVERT(bigint, ISNULL(SUM(CASE
                WHEN cr.estado = 'PENDIENTE_APROBACION'
                THEN 1 ELSE 0
            END), 0)) AS TotalCargasPendientes,

            uc.MesUltimoCorte AS MesUltimoCorte,

            MAX(COALESCE(cr.fecha_confirmacion, cr.fecha_validacion)) AS UltimaFechaMovimiento
        FROM ultimo_corte uc
        LEFT JOIN cargas_relevantes cr
            ON 1 = 1
        GROUP BY
            uc.MesUltimoCorte;
    ";
        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QuerySingleAsync<InformeSabanaFirma>(sql, new { AnioCorte = anioCorte, IdEntidadFederativa = idEntidadFederativa });
    }

    private static string ObtenerEndpointAcusePrevio(string tipoCarga, string codigoReferencia)
    {
        if(string.Equals(tipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase))
        {
            return $"/api/actualizaciones/{codigoReferencia}/acuse";
        }

        return $"/api/cargas/{codigoReferencia}/acuse";
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerCarpetasPendientesAsync(long idCarga)
    {
        const string sql = @"
        SELECT
            c.id_ci,
            c.ntra_ci,
            c.fha_de_ini,
            c.hra_de_ini,
            c.rmen_de_hchos
        FROM dbo.carga_tmp_carpeta c
        WHERE c.id_carga = @IdCarga
          AND c.activo = 1
        ORDER BY c.numero_fila;
    ";
        return await QueryDictionaryCargaAsync(sql, idCarga);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerDelitosPendientesAsync(long idCarga)
    {
        const string sql = @"
        SELECT
            d.id_ci,
            d.id_delito,
            d.dto,
            d.moda_dto,
            d.forma_acc,
            d.fha_de_hchos,
            d.hra_de_hchos,
            d.emto_com_dto,
            d.grdo_cons,
            d.clasf_de_dto,
            ISNULL(ef.nombre, '') AS nom_ent_hchos,
            d.id_ent_hchos,
            ISNULL(mun.nombre, '') AS nom_mun_hchos,
            d.id_mun_hchos,
            d.nom_loc_hchos,
            d.id_loc_hchos,
            d.nom_col_hchos,
            d.id_col_hchos,
            d.cp,
            d.coord_x,
            d.coord_y,
            d.dom_hchos
        FROM dbo.carga_tmp_delito d
        LEFT JOIN dbo.catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos)
        LEFT JOIN dbo.catalogo_municipio mun
            ON mun.id_entidad_federativa = ef.id_entidad_federativa
           AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos)
        WHERE d.id_carga = @IdCarga
          AND d.activo = 1
        ORDER BY d.numero_fila;
    ";
        return await QueryDictionaryCargaAsync(sql, idCarga);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerVictimasPendientesAsync(long idCarga)
    {
        const string sql = @"
        SELECT
            v.id_ci,
            v.id_delito,
            v.id_vicf,
            v.id_tv,
            v.id_tpm,
            v.sexo,
            v.genero,
            v.pob,
            v.disc,
            v.fha_nac,
            v.edad,
            v.nacional,
            v.nombre_vicfem AS nombre_vicfem,
            v.primer_apellido_vicfem AS [1apellido_vicfem],
            v.segundo_apellido_vicfem AS [2apellido_vicfem],
            v.curp_vicfem AS curp_vicfem
        FROM dbo.carga_tmp_victima v
        WHERE v.id_carga = @IdCarga
          AND v.activo = 1
        ORDER BY v.numero_fila;
    ";
        return await QueryDictionaryCargaAsync(sql, idCarga);
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryCargaAsync(string sql, long idCarga)
    {
        using var connection = _dbConnectionFactory.CrearConexion();
        var filas = await connection.QueryAsync(sql, new { IdCarga = idCarga });
        return filas.Select(fila => ((IDictionary<string, object?>)fila).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)).Cast<IDictionary<string, object?>>().ToList();
    }
}
