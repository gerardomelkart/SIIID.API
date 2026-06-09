using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class InformeRepository : IInformeRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public InformeRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<List<InformeEnvioItem>> ObtenerEnviosAsync(bool esSuperUsuario, int? idEntidadFederativaUsuario, int? idEntidadFederativa, int? mesCorte, int? anioCorte)
    {
        // Obtiene el último envío confirmado por entidad y periodo.
        // Considera tanto carga inicial confirmada como actualizaciones confirmadas.
        // Si hay varias actualizaciones confirmadas para el mismo corte,
        // se toma únicamente la más reciente.
        var sql = @"
            WITH envios_confirmados AS (
                SELECT
                    c.id_carga,
                    c.codigo_referencia,
                    c.tipo_carga,
                    c.id_entidad_federativa,
                    c.mes_corte,
                    c.anio_corte,
                    COALESCE(c.fecha_confirmacion, c.fecha_validacion) AS fecha_envio,
                    COALESCE(uconf.usuario, ucarga.usuario) AS usuario_envio,
                    ROW_NUMBER() OVER (
                        PARTITION BY
                            c.id_entidad_federativa,
                            c.mes_corte,
                            c.anio_corte
                        ORDER BY
                            COALESCE(c.fecha_confirmacion, c.fecha_validacion) DESC,
                            c.id_carga DESC
                    ) AS rn
                FROM carga c
                INNER JOIN usuario ucarga
                    ON ucarga.id_usuario = c.id_usuario_carga
                LEFT JOIN usuario uconf
                    ON uconf.id_usuario = c.id_usuario_confirmacion
                WHERE c.activo = 1
                  AND (
                        (c.tipo_carga = 'CARGA_INICIAL' AND c.estado = 'CONFIRMADO')
                     OR (c.tipo_carga = 'ACTUALIZACION' AND c.estado = 'CONFIRMADO_ACTUALIZACION')
                  )
            )
            SELECT
                e.id_carga AS IdCarga,
                e.codigo_referencia AS CodigoReferencia,
                e.tipo_carga AS TipoCarga,
                e.id_entidad_federativa AS IdEntidadFederativa,
                ef.nombre AS EntidadFederativa,
                ef.clave AS ClaveEntidad,
                e.fecha_envio AS FechaEnvio,
                e.mes_corte AS MesCorte,
                e.anio_corte AS AnioCorte,
                e.usuario_envio AS UsuarioEnvio
            FROM envios_confirmados e
            INNER JOIN catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = e.id_entidad_federativa
            WHERE e.rn = 1
              AND (@EsSuperUsuario = 1 OR e.id_entidad_federativa = @IdEntidadFederativaUsuario)
              AND (@IdEntidadFederativa IS NULL OR e.id_entidad_federativa = @IdEntidadFederativa)
              AND (@MesCorte IS NULL OR e.mes_corte = @MesCorte)
              AND (@AnioCorte IS NULL OR e.anio_corte = @AnioCorte)
            ORDER BY
                ef.nombre,
                e.anio_corte DESC,
                e.mes_corte DESC;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var envios = await connection.QueryAsync<InformeEnvioItem>(sql, new
        {
            EsSuperUsuario = esSuperUsuario,
            IdEntidadFederativaUsuario = idEntidadFederativaUsuario,
            IdEntidadFederativa = esSuperUsuario ? idEntidadFederativa : null,
            MesCorte = mesCorte,
            AnioCorte = anioCorte
        });

        return envios
            .Select(x =>
            {
                x.FechaEnvioTexto = x.FechaEnvio.ToString("dd-MM-yyyy");
                x.Corte = $"{ObtenerNombreMes(x.MesCorte)} {x.AnioCorte}";
                x.EndpointAcuse = ObtenerEndpointAcuse(x.TipoCarga, x.CodigoReferencia);
                x.EndpointExcel = $"/api/informes/envios/{x.CodigoReferencia}/archivos";

                return x;
            })
            .ToList();
    }

    private static string ObtenerEndpointAcuse(string tipoCarga, string codigoReferencia)
    {
        if (string.Equals(tipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase))
        {
            return $"/api/actualizaciones/{codigoReferencia}/acuse-confirmado";
        }

        return $"/api/cargas/{codigoReferencia}/acuse-confirmado";
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

    public async Task<InformeArchivoCargaInfo?> ObtenerCargaConfirmadaParaArchivosAsync(string codigoReferencia)
    {
        // Obtiene una carga o actualización confirmada.
        // Solo se permite descargar archivos de envíos ya confirmados.
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
          AND (
                (c.tipo_carga = 'CARGA_INICIAL' AND c.estado = 'CONFIRMADO')
             OR (c.tipo_carga = 'ACTUALIZACION' AND c.estado = 'CONFIRMADO_ACTUALIZACION')
          );
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<InformeArchivoCargaInfo>(sql, new
        {
            CodigoReferencia = codigoReferencia
        });
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
            CONVERT(varchar(10), d.id_entidad_federativa) AS id_ent_hchos,
            mun.clave AS id_mun_hchos,
            d.id_localidad_fiscalia AS id_loc_hchos,
            d.localidad_fiscalia_nombre AS nom_loc_hchos,
            d.id_colonia_fiscalia AS id_col_hchos,
            d.colonia_fiscalia_nombre AS nom_col_hchos,
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
        INNER JOIN catalogo_municipio mun
            ON mun.id_municipio = d.id_municipio
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
            nac.clave AS nacional
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

        var filas = await connection.QueryAsync(sql, new
        {
            carga.IdEntidadFederativa,
            carga.MesCorte,
            carga.AnioCorte
        });

        return filas
            .Select(fila => ((IDictionary<string, object?>)fila)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase))
            .Cast<IDictionary<string, object?>>()
            .ToList();
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryAnioAsync(string sql, int anioCorte)
    {
        using var connection = _dbConnectionFactory.CrearConexion();

        var filas = await connection.QueryAsync(sql, new
        {
            AnioCorte = anioCorte
        });

        return filas
            .Select(fila => ((IDictionary<string, object?>)fila)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase))
            .Cast<IDictionary<string, object?>>()
            .ToList();
    }

    public async Task<List<InformeReporteCargaItem>> ObtenerReporteCargasAsync(int? idEntidadFederativa, int? mesCorte, int? anioCorte)
    {
        // Reporte de cargas por entidad y periodo.
        // Solo SUPER_USUARIO consume este reporte.
        //
        // Si no se envían filtros, regresa todos los periodos que tengan intentos.
        // Cada fila representa entidad + mes_corte + anio_corte.
        var sql = @"
    WITH periodos AS (
        SELECT
            c.id_entidad_federativa,
            c.mes_corte,
            c.anio_corte,
            COUNT(1) AS intentos
        FROM carga c
        WHERE c.activo = 1
          AND c.mes_corte IS NOT NULL
          AND c.anio_corte IS NOT NULL
          AND (@IdEntidadFederativa IS NULL OR c.id_entidad_federativa = @IdEntidadFederativa)
          AND (@MesCorte IS NULL OR c.mes_corte = @MesCorte)
          AND (@AnioCorte IS NULL OR c.anio_corte = @AnioCorte)
        GROUP BY
            c.id_entidad_federativa,
            c.mes_corte,
            c.anio_corte
    )
    SELECT
        ef.id_entidad_federativa AS IdEntidadFederativa,
        ef.nombre AS EntidadFederativa,
        ef.clave AS ClaveEntidad,
        p.mes_corte AS MesCorte,
        p.anio_corte AS AnioCorte,
        p.intentos AS Intentos,
        ultimo.codigo_referencia AS UltimoIntento,
        ultimo.tipo_carga AS TipoCargaUltimoIntento,
        ultimo.estado AS EstatusUltimoIntento,
        ultimo.fecha_ultimo_movimiento AS FechaUltimaCarga
    FROM periodos p
    INNER JOIN catalogo_entidad_federativa ef
        ON ef.id_entidad_federativa = p.id_entidad_federativa
       AND ef.activo = 1
    OUTER APPLY (
        SELECT TOP 1
            c.codigo_referencia,
            c.tipo_carga,
            c.estado,
            COALESCE(c.fecha_confirmacion, c.fecha_validacion) AS fecha_ultimo_movimiento
        FROM carga c
        WHERE c.activo = 1
          AND c.id_entidad_federativa = p.id_entidad_federativa
          AND c.mes_corte = p.mes_corte
          AND c.anio_corte = p.anio_corte
        ORDER BY
            CASE
                WHEN c.tipo_carga = 'ACTUALIZACION'
                     AND c.estado = 'CONFIRMADO_ACTUALIZACION'
                    THEN 1
                WHEN c.tipo_carga = 'CARGA_INICIAL'
                     AND c.estado = 'CONFIRMADO'
                    THEN 1
                ELSE 0
            END DESC,
            COALESCE(c.fecha_confirmacion, c.fecha_validacion) DESC,
            c.id_carga DESC
    ) ultimo
    ORDER BY
        p.anio_corte DESC,
        p.mes_corte DESC,
        ef.nombre;
";

        using var connection = _dbConnectionFactory.CrearConexion();

        var reporte = await connection.QueryAsync<InformeReporteCargaItem>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa,
            MesCorte = mesCorte,
            AnioCorte = anioCorte
        });

        return reporte
            .Select(x =>
            {
                x.Corte = $"{ObtenerNombreMes(x.MesCorte)} {x.AnioCorte}";
                x.FechaUltimaCargaTexto = x.FechaUltimaCarga.HasValue
                    ? x.FechaUltimaCarga.Value.ToString("yyyy-MM-dd HH:mm:ss")
                    : string.Empty;

                return x;
            })
            .ToList();
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaEstatalDelitosAsync(int anioCorte)
    {
        var sql = @"
            WITH sabana AS (
            SELECT
                MIN(s.id_delito_sabana) AS orden_sabana,
                MIN(cd.id_delito) AS orden_delito,
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana
                FROM catalogo_delito_sabana s
                INNER JOIN catalogo_modalidad_delito md
                    ON md.id_modalidad_delito = s.id_modalidad_delito
                   AND md.activo = 1
                INNER JOIN catalogo_subtipo_delito sd
                    ON sd.id_subtipo_delito = md.id_subtipo_delito
                   AND sd.activo = 1
                INNER JOIN catalogo_delito cd
                    ON cd.id_delito = sd.id_delito
                   AND cd.activo = 1
                INNER JOIN catalogo_bien_juridico bj
                    ON bj.id_bien_juridico = cd.id_bien_juridico
                   AND bj.activo = 1
                WHERE s.activo = 1
                GROUP BY
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana
            ),
        matriz AS (
            SELECT
                @AnioCorte AS anio_corte,
                TRY_CONVERT(int, ef.clave) AS clave_ent,
                ef.nombre AS entidad,
                s.orden_sabana,
                s.orden_delito,
                s.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana
            FROM catalogo_entidad_federativa ef
            CROSS JOIN sabana s
            WHERE ef.activo = 1
              AND TRY_CONVERT(int, ef.clave) BETWEEN 1 AND 32
        ),
        conteos AS (
            SELECT
                c.anio_corte,
                c.mes_corte,
                TRY_CONVERT(int, efh.clave) AS clave_ent,
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana,
                COUNT(1) AS cantidad_delitos
            FROM delito d
            INNER JOIN carga c
                ON c.id_carga = d.id_carga
               AND c.activo = 1
               AND c.anio_corte = @AnioCorte
               AND (
                      (c.tipo_carga = 'CARGA_INICIAL' AND c.estado = 'CONFIRMADO')
                   OR (c.tipo_carga = 'ACTUALIZACION' AND c.estado = 'CONFIRMADO_ACTUALIZACION')
               )
            INNER JOIN catalogo_entidad_federativa efh
                ON efh.id_entidad_federativa = d.id_entidad_federativa
               AND efh.activo = 1
            INNER JOIN catalogo_delito_sabana s
                ON s.id_modalidad_delito = d.id_modalidad_delito
               AND s.id_grado_consumacion = d.id_grado_consumacion
               AND s.id_instrumento_comision = d.id_instrumento_comision
               AND s.id_forma_accion = d.id_forma_accion
               AND s.activo = 1
            INNER JOIN catalogo_modalidad_delito md
                ON md.id_modalidad_delito = s.id_modalidad_delito
               AND md.activo = 1
            INNER JOIN catalogo_subtipo_delito sd
                ON sd.id_subtipo_delito = md.id_subtipo_delito
               AND sd.activo = 1
            INNER JOIN catalogo_delito cd
                ON cd.id_delito = sd.id_delito
               AND cd.activo = 1
            INNER JOIN catalogo_bien_juridico bj
                ON bj.id_bien_juridico = cd.id_bien_juridico
               AND bj.activo = 1
            WHERE d.activo = 1
              AND TRY_CONVERT(int, efh.clave) BETWEEN 1 AND 32
            GROUP BY
                c.anio_corte,
                c.mes_corte,
                TRY_CONVERT(int, efh.clave),
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana
        )
        SELECT
            m.anio_corte AS [Año],
            RIGHT('00' + CONVERT(varchar(2), m.clave_ent), 2) AS [Clave_Ent],
            m.entidad AS [Entidad],
            m.bien_juridico AS [Bien jurídico afectado],
            m.delito_sabana AS [Tipo de delito],
            m.subtipo_delito_sabana AS [Subtipo de delito],
            m.modalidad_delito_sabana AS [Modalidad],
            ISNULL(SUM(CASE WHEN c.mes_corte = 1 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Enero],
            ISNULL(SUM(CASE WHEN c.mes_corte = 2 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Febrero],
            ISNULL(SUM(CASE WHEN c.mes_corte = 3 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Marzo],
            ISNULL(SUM(CASE WHEN c.mes_corte = 4 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Abril],
            ISNULL(SUM(CASE WHEN c.mes_corte = 5 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Mayo],
            ISNULL(SUM(CASE WHEN c.mes_corte = 6 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Junio],
            ISNULL(SUM(CASE WHEN c.mes_corte = 7 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Julio],
            ISNULL(SUM(CASE WHEN c.mes_corte = 8 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Agosto],
            ISNULL(SUM(CASE WHEN c.mes_corte = 9 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Septiembre],
            ISNULL(SUM(CASE WHEN c.mes_corte = 10 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Octubre],
            ISNULL(SUM(CASE WHEN c.mes_corte = 11 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Noviembre],
            ISNULL(SUM(CASE WHEN c.mes_corte = 12 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Diciembre]
        FROM matriz m
        LEFT JOIN conteos c
            ON c.anio_corte = m.anio_corte
           AND c.clave_ent = m.clave_ent
           AND c.bien_juridico = m.bien_juridico
           AND c.delito_sabana = m.delito_sabana
           AND c.subtipo_delito_sabana = m.subtipo_delito_sabana
           AND c.modalidad_delito_sabana = m.modalidad_delito_sabana
        GROUP BY
            m.anio_corte,
            m.clave_ent,
            m.entidad,
            m.orden_sabana,
            m.orden_delito,
            m.bien_juridico,
            m.delito_sabana,
            m.subtipo_delito_sabana,
            m.modalidad_delito_sabana
        ORDER BY
            m.clave_ent,
            m.orden_sabana;
        ";

        return await QueryDictionaryAnioAsync(sql, anioCorte);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalDelitosAsync(int anioCorte)
    {
        var sql = @"
            WITH sabana AS (
            SELECT
                MIN(s.id_delito_sabana) AS orden_sabana,
                MIN(cd.id_delito) AS orden_delito,
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana
                FROM catalogo_delito_sabana s
                INNER JOIN catalogo_modalidad_delito md
                    ON md.id_modalidad_delito = s.id_modalidad_delito
                   AND md.activo = 1
                INNER JOIN catalogo_subtipo_delito sd
                    ON sd.id_subtipo_delito = md.id_subtipo_delito
                   AND sd.activo = 1
                INNER JOIN catalogo_delito cd
                    ON cd.id_delito = sd.id_delito
                   AND cd.activo = 1
                INNER JOIN catalogo_bien_juridico bj
                    ON bj.id_bien_juridico = cd.id_bien_juridico
                   AND bj.activo = 1
                WHERE s.activo = 1
                GROUP BY
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana
            ),
        matriz AS (
            SELECT
                @AnioCorte AS anio_corte,
                TRY_CONVERT(int, ef.clave) AS clave_ent,
                ef.nombre AS entidad,
                CONCAT(
                    RIGHT('00' + CONVERT(varchar(2), TRY_CONVERT(int, ef.clave)), 2),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                ) AS clave_municipio_compuesta,
                mun.nombre AS municipio,
                s.orden_sabana,
                s.orden_delito,
                s.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana
            FROM catalogo_entidad_federativa ef
            INNER JOIN catalogo_municipio mun
                ON mun.id_entidad_federativa = ef.id_entidad_federativa
               AND mun.activo = 1
            CROSS JOIN sabana s
            WHERE ef.activo = 1
              AND TRY_CONVERT(int, ef.clave) BETWEEN 1 AND 32
        ),
        conteos AS (
            SELECT
                c.anio_corte,
                c.mes_corte,
                TRY_CONVERT(int, efh.clave) AS clave_ent,
                CONCAT(
                    RIGHT('00' + CONVERT(varchar(2), TRY_CONVERT(int, efh.clave)), 2),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                ) AS clave_municipio_compuesta,
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana,
                COUNT(1) AS cantidad_delitos
            FROM delito d
            INNER JOIN carga c
                ON c.id_carga = d.id_carga
               AND c.activo = 1
               AND c.anio_corte = @AnioCorte
               AND (
                      (c.tipo_carga = 'CARGA_INICIAL' AND c.estado = 'CONFIRMADO')
                   OR (c.tipo_carga = 'ACTUALIZACION' AND c.estado = 'CONFIRMADO_ACTUALIZACION')
               )
            INNER JOIN catalogo_entidad_federativa efh
                ON efh.id_entidad_federativa = d.id_entidad_federativa
               AND efh.activo = 1
            INNER JOIN catalogo_municipio mun
                ON mun.id_municipio = d.id_municipio
               AND mun.activo = 1
            INNER JOIN catalogo_delito_sabana s
                ON s.id_modalidad_delito = d.id_modalidad_delito
               AND s.id_grado_consumacion = d.id_grado_consumacion
               AND s.id_instrumento_comision = d.id_instrumento_comision
               AND s.id_forma_accion = d.id_forma_accion
               AND s.activo = 1
            INNER JOIN catalogo_modalidad_delito md
                ON md.id_modalidad_delito = s.id_modalidad_delito
               AND md.activo = 1
            INNER JOIN catalogo_subtipo_delito sd
                ON sd.id_subtipo_delito = md.id_subtipo_delito
               AND sd.activo = 1
            INNER JOIN catalogo_delito cd
                ON cd.id_delito = sd.id_delito
               AND cd.activo = 1
            INNER JOIN catalogo_bien_juridico bj
                ON bj.id_bien_juridico = cd.id_bien_juridico
               AND bj.activo = 1
            WHERE d.activo = 1
              AND TRY_CONVERT(int, efh.clave) BETWEEN 1 AND 32
            GROUP BY
                c.anio_corte,
                c.mes_corte,
                TRY_CONVERT(int, efh.clave),
                CONCAT(
                    RIGHT('00' + CONVERT(varchar(2), TRY_CONVERT(int, efh.clave)), 2),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                ),
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana
        )
        SELECT
            m.anio_corte AS [Año],
            RIGHT('00' + CONVERT(varchar(2), m.clave_ent), 2) AS [Clave_Ent],
            m.entidad AS [Entidad],
            m.clave_municipio_compuesta AS [Cve. Municipio],
            m.municipio AS [Municipio],
            m.bien_juridico AS [Bien jurídico afectado],
            m.delito_sabana AS [Tipo de delito],
            m.subtipo_delito_sabana AS [Subtipo de delito],
            m.modalidad_delito_sabana AS [Modalidad],
            ISNULL(SUM(CASE WHEN c.mes_corte = 1 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Enero],
            ISNULL(SUM(CASE WHEN c.mes_corte = 2 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Febrero],
            ISNULL(SUM(CASE WHEN c.mes_corte = 3 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Marzo],
            ISNULL(SUM(CASE WHEN c.mes_corte = 4 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Abril],
            ISNULL(SUM(CASE WHEN c.mes_corte = 5 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Mayo],
            ISNULL(SUM(CASE WHEN c.mes_corte = 6 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Junio],
            ISNULL(SUM(CASE WHEN c.mes_corte = 7 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Julio],
            ISNULL(SUM(CASE WHEN c.mes_corte = 8 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Agosto],
            ISNULL(SUM(CASE WHEN c.mes_corte = 9 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Septiembre],
            ISNULL(SUM(CASE WHEN c.mes_corte = 10 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Octubre],
            ISNULL(SUM(CASE WHEN c.mes_corte = 11 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Noviembre],
            ISNULL(SUM(CASE WHEN c.mes_corte = 12 THEN c.cantidad_delitos ELSE 0 END), 0) AS [Diciembre]
        FROM matriz m
        LEFT JOIN conteos c
            ON c.anio_corte = m.anio_corte
           AND c.clave_ent = m.clave_ent
           AND c.clave_municipio_compuesta = m.clave_municipio_compuesta
           AND c.bien_juridico = m.bien_juridico
           AND c.delito_sabana = m.delito_sabana
           AND c.subtipo_delito_sabana = m.subtipo_delito_sabana
           AND c.modalidad_delito_sabana = m.modalidad_delito_sabana
        GROUP BY
            m.anio_corte,
            m.clave_ent,
            m.entidad,
            m.clave_municipio_compuesta,
            m.municipio,
            m.orden_sabana,
            m.orden_delito,
            m.bien_juridico,
            m.delito_sabana,
            m.subtipo_delito_sabana,
            m.modalidad_delito_sabana
        ORDER BY
            m.clave_ent,
            m.clave_municipio_compuesta,
            m.orden_sabana;
        ";

        return await QueryDictionaryAnioAsync(sql, anioCorte);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaEstatalVictimasAsync(int anioCorte)
    {
        var sql = @"
        WITH sexos AS (
            SELECT 1 AS orden_sexo, 'Hombre' AS sexo
            UNION ALL SELECT 2, 'Mujer'
            UNION ALL SELECT 3, 'No identificado'
        ),
        rangos_edad AS (
            SELECT 1 AS orden_rango, '0 a 12 años' AS rango_edad
            UNION ALL SELECT 2, '13 a 17 años'
            UNION ALL SELECT 3, '18 a 29 años'
            UNION ALL SELECT 4, '30 a 60 años'
            UNION ALL SELECT 5, 'Más de 60 años'
            UNION ALL SELECT 6, 'No especificado'
        ),
        sabana AS (
        SELECT
            MIN(s.id_delito_sabana) AS orden_sabana,
            MIN(cd.id_delito) AS orden_delito,
            bj.bien_juridico,
            s.delito_sabana,
            s.subtipo_delito_sabana,
            s.modalidad_delito_sabana
            FROM catalogo_delito_sabana s
            INNER JOIN catalogo_modalidad_delito md
                ON md.id_modalidad_delito = s.id_modalidad_delito
               AND md.activo = 1
            INNER JOIN catalogo_subtipo_delito sd
                ON sd.id_subtipo_delito = md.id_subtipo_delito
               AND sd.activo = 1
            INNER JOIN catalogo_delito cd
                ON cd.id_delito = sd.id_delito
               AND cd.activo = 1
            INNER JOIN catalogo_bien_juridico bj
                ON bj.id_bien_juridico = cd.id_bien_juridico
               AND bj.activo = 1
            WHERE s.activo = 1
            GROUP BY
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana
        ),
        matriz AS (
            SELECT
                @AnioCorte AS anio_corte,
                TRY_CONVERT(int, ef.clave) AS clave_ent,
                ef.nombre AS entidad,
                s.orden_sabana,
                s.orden_delito,
                s.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana,
                sx.orden_sexo,
                sx.sexo,
                re.orden_rango,
                re.rango_edad
            FROM catalogo_entidad_federativa ef
            CROSS JOIN sabana s
            CROSS JOIN sexos sx
            CROSS JOIN rangos_edad re
            WHERE ef.activo = 1
              AND TRY_CONVERT(int, ef.clave) BETWEEN 1 AND 32
        ),
        conteos AS (
            SELECT
                c.anio_corte,
                c.mes_corte,
                TRY_CONVERT(int, efh.clave) AS clave_ent,
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana,
                CASE
                    WHEN tv.clave = 1 AND sx.clave IN (1, 2, 3) THEN sx.descripcion
                    ELSE 'No identificado'
                END AS sexo,
                CASE
                    WHEN tv.clave <> 1 THEN 'No especificado'
                    WHEN TRY_CONVERT(int, v.edad) IS NULL THEN 'No especificado'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 0 AND 12 THEN '0 a 12 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 13 AND 17 THEN '13 a 17 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 18 AND 29 THEN '18 a 29 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 30 AND 60 THEN '30 a 60 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 61 AND 120 THEN 'Más de 60 años'
                    ELSE 'No especificado'
                END AS rango_edad,
                COUNT(1) AS cantidad_victimas
            FROM victima v
            INNER JOIN delito d
                ON d.id_delito = v.id_delito
               AND d.activo = 1
            INNER JOIN carga c
                ON c.id_carga = v.id_carga
               AND c.activo = 1
               AND c.anio_corte = @AnioCorte
               AND (
                      (c.tipo_carga = 'CARGA_INICIAL' AND c.estado = 'CONFIRMADO')
                   OR (c.tipo_carga = 'ACTUALIZACION' AND c.estado = 'CONFIRMADO_ACTUALIZACION')
               )
            INNER JOIN catalogo_entidad_federativa efh
                ON efh.id_entidad_federativa = d.id_entidad_federativa
               AND efh.activo = 1
            INNER JOIN catalogo_tipo_victima tv
                ON tv.id_tipo_victima = v.id_tipo_victima
               AND tv.activo = 1
            LEFT JOIN catalogo_sexo sx
                ON sx.id_sexo = v.id_sexo
               AND sx.activo = 1
            INNER JOIN catalogo_delito_sabana s
                ON s.id_modalidad_delito = d.id_modalidad_delito
               AND s.id_grado_consumacion = d.id_grado_consumacion
               AND s.id_instrumento_comision = d.id_instrumento_comision
               AND s.id_forma_accion = d.id_forma_accion
               AND s.activo = 1
            INNER JOIN catalogo_modalidad_delito md
                ON md.id_modalidad_delito = s.id_modalidad_delito
               AND md.activo = 1
            INNER JOIN catalogo_subtipo_delito sd
                ON sd.id_subtipo_delito = md.id_subtipo_delito
               AND sd.activo = 1
            INNER JOIN catalogo_delito cd
                ON cd.id_delito = sd.id_delito
               AND cd.activo = 1
            INNER JOIN catalogo_bien_juridico bj
                ON bj.id_bien_juridico = cd.id_bien_juridico
               AND bj.activo = 1
            WHERE v.activo = 1
            AND TRY_CONVERT(int, efh.clave) BETWEEN 1 AND 32
            GROUP BY
                c.anio_corte,
                c.mes_corte,
                TRY_CONVERT(int, efh.clave),
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana,
                CASE
                    WHEN tv.clave = 1 AND sx.clave IN (1, 2, 3) THEN sx.descripcion
                    ELSE 'No identificado'
                END,
                CASE
                    WHEN tv.clave <> 1 THEN 'No especificado'
                    WHEN TRY_CONVERT(int, v.edad) IS NULL THEN 'No especificado'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 0 AND 12 THEN '0 a 12 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 13 AND 17 THEN '13 a 17 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 18 AND 29 THEN '18 a 29 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 30 AND 60 THEN '30 a 60 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 61 AND 120 THEN 'Más de 60 años'
                    ELSE 'No especificado'
                END
        )
        SELECT
            m.anio_corte AS [Año],
            RIGHT('00' + CONVERT(varchar(2), m.clave_ent), 2) AS [Clave_Ent],
            m.entidad AS [Entidad],
            m.bien_juridico AS [Bien jurídico afectado],
            m.delito_sabana AS [Tipo de delito],
            m.subtipo_delito_sabana AS [Subtipo de delito],
            m.modalidad_delito_sabana AS [Modalidad],
            m.sexo AS [Sexo],
            m.rango_edad AS [Rango de edad],
            ISNULL(SUM(CASE WHEN c.mes_corte = 1 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Enero],
            ISNULL(SUM(CASE WHEN c.mes_corte = 2 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Febrero],
            ISNULL(SUM(CASE WHEN c.mes_corte = 3 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Marzo],
            ISNULL(SUM(CASE WHEN c.mes_corte = 4 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Abril],
            ISNULL(SUM(CASE WHEN c.mes_corte = 5 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Mayo],
            ISNULL(SUM(CASE WHEN c.mes_corte = 6 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Junio],
            ISNULL(SUM(CASE WHEN c.mes_corte = 7 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Julio],
            ISNULL(SUM(CASE WHEN c.mes_corte = 8 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Agosto],
            ISNULL(SUM(CASE WHEN c.mes_corte = 9 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Septiembre],
            ISNULL(SUM(CASE WHEN c.mes_corte = 10 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Octubre],
            ISNULL(SUM(CASE WHEN c.mes_corte = 11 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Noviembre],
            ISNULL(SUM(CASE WHEN c.mes_corte = 12 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Diciembre]
        FROM matriz m
        LEFT JOIN conteos c
            ON c.anio_corte = m.anio_corte
           AND c.clave_ent = m.clave_ent
           AND c.bien_juridico = m.bien_juridico
           AND c.delito_sabana = m.delito_sabana
           AND c.subtipo_delito_sabana = m.subtipo_delito_sabana
           AND c.modalidad_delito_sabana = m.modalidad_delito_sabana
           AND c.sexo = m.sexo
           AND c.rango_edad = m.rango_edad
        GROUP BY
            m.anio_corte,
            m.clave_ent,
            m.entidad,
            m.orden_sabana,
            m.orden_delito,
            m.bien_juridico,
            m.delito_sabana,
            m.subtipo_delito_sabana,
            m.modalidad_delito_sabana,
            m.orden_sexo,
            m.sexo,
            m.orden_rango,
            m.rango_edad
        ORDER BY
            m.clave_ent,
            m.orden_sabana,
            m.orden_sexo,
            m.orden_rango;
        ";

        return await QueryDictionaryAnioAsync(sql, anioCorte);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalVictimasAsync(int anioCorte)
    {
        var sql = @"
        WITH sabana AS (
        SELECT
            MIN(s.id_delito_sabana) AS orden_sabana,
            MIN(cd.id_delito) AS orden_delito,
            bj.bien_juridico,
            s.delito_sabana,
            s.subtipo_delito_sabana,
            s.modalidad_delito_sabana
            FROM catalogo_delito_sabana s
            INNER JOIN catalogo_modalidad_delito md
                ON md.id_modalidad_delito = s.id_modalidad_delito
               AND md.activo = 1
            INNER JOIN catalogo_subtipo_delito sd
                ON sd.id_subtipo_delito = md.id_subtipo_delito
               AND sd.activo = 1
            INNER JOIN catalogo_delito cd
                ON cd.id_delito = sd.id_delito
               AND cd.activo = 1
            INNER JOIN catalogo_bien_juridico bj
                ON bj.id_bien_juridico = cd.id_bien_juridico
               AND bj.activo = 1
            WHERE s.activo = 1
            GROUP BY
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana
        ),
        matriz_municipal_sin_conteo AS (
            SELECT
                @AnioCorte AS anio_corte,
                TRY_CONVERT(int, ef.clave) AS clave_ent,
                ef.nombre AS entidad,
                CONCAT(
                    RIGHT('00' + CONVERT(varchar(2), TRY_CONVERT(int, ef.clave)), 2),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                ) AS clave_municipio_compuesta,
                mun.nombre AS municipio,
                s.orden_sabana,
                s.orden_delito,
                s.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana,
                'No identificado' AS sexo,
                'No especificado' AS rango_edad,
                0 AS enero,
                0 AS febrero,
                0 AS marzo,
                0 AS abril,
                0 AS mayo,
                0 AS junio,
                0 AS julio,
                0 AS agosto,
                0 AS septiembre,
                0 AS octubre,
                0 AS noviembre,
                0 AS diciembre
            FROM catalogo_entidad_federativa ef
            INNER JOIN catalogo_municipio mun
                ON mun.id_entidad_federativa = ef.id_entidad_federativa
               AND mun.activo = 1
            CROSS JOIN sabana s
            WHERE ef.activo = 1
              AND TRY_CONVERT(int, ef.clave) BETWEEN 1 AND 32
        ),
        conteos AS (
            SELECT
                c.anio_corte,
                TRY_CONVERT(int, efh.clave) AS clave_ent,
                efh.nombre AS entidad,
                CONCAT(
                    RIGHT('00' + CONVERT(varchar(2), TRY_CONVERT(int, efh.clave)), 2),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                ) AS clave_municipio_compuesta,
                mun.nombre AS municipio,
                MIN(s.id_delito_sabana) AS orden_sabana,
                MIN(cd.id_delito) AS orden_delito,
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana,
                CASE
                    WHEN tv.clave = 1 AND sx.clave = 1 THEN 'Hombre'
                    WHEN tv.clave = 1 AND sx.clave = 2 THEN 'Mujer'
                    ELSE 'No identificado'
                END AS sexo,
                CASE
                    WHEN tv.clave <> 1 THEN 'No especificado'
                    WHEN TRY_CONVERT(int, v.edad) IS NULL THEN 'No especificado'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 0 AND 12 THEN '0 a 12 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 13 AND 17 THEN '13 a 17 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 18 AND 29 THEN '18 a 29 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 30 AND 60 THEN '30 a 60 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 61 AND 120 THEN 'Más de 60 años'
                    ELSE 'No especificado'
                END AS rango_edad,
                SUM(CASE WHEN c.mes_corte = 1 THEN 1 ELSE 0 END) AS enero,
                SUM(CASE WHEN c.mes_corte = 2 THEN 1 ELSE 0 END) AS febrero,
                SUM(CASE WHEN c.mes_corte = 3 THEN 1 ELSE 0 END) AS marzo,
                SUM(CASE WHEN c.mes_corte = 4 THEN 1 ELSE 0 END) AS abril,
                SUM(CASE WHEN c.mes_corte = 5 THEN 1 ELSE 0 END) AS mayo,
                SUM(CASE WHEN c.mes_corte = 6 THEN 1 ELSE 0 END) AS junio,
                SUM(CASE WHEN c.mes_corte = 7 THEN 1 ELSE 0 END) AS julio,
                SUM(CASE WHEN c.mes_corte = 8 THEN 1 ELSE 0 END) AS agosto,
                SUM(CASE WHEN c.mes_corte = 9 THEN 1 ELSE 0 END) AS septiembre,
                SUM(CASE WHEN c.mes_corte = 10 THEN 1 ELSE 0 END) AS octubre,
                SUM(CASE WHEN c.mes_corte = 11 THEN 1 ELSE 0 END) AS noviembre,
                SUM(CASE WHEN c.mes_corte = 12 THEN 1 ELSE 0 END) AS diciembre
            FROM victima v
            INNER JOIN delito d
                ON d.id_delito = v.id_delito
               AND d.activo = 1
            INNER JOIN carga c
                ON c.id_carga = v.id_carga
               AND c.activo = 1
               AND c.anio_corte = @AnioCorte
               AND (
                      (c.tipo_carga = 'CARGA_INICIAL' AND c.estado = 'CONFIRMADO')
                   OR (c.tipo_carga = 'ACTUALIZACION' AND c.estado = 'CONFIRMADO_ACTUALIZACION')
               )
            INNER JOIN catalogo_entidad_federativa efh
                ON efh.id_entidad_federativa = d.id_entidad_federativa
               AND efh.activo = 1
            INNER JOIN catalogo_municipio mun
                ON mun.id_municipio = d.id_municipio
               AND mun.activo = 1
            INNER JOIN catalogo_tipo_victima tv
                ON tv.id_tipo_victima = v.id_tipo_victima
               AND tv.activo = 1
            LEFT JOIN catalogo_sexo sx
                ON sx.id_sexo = v.id_sexo
               AND sx.activo = 1
            INNER JOIN catalogo_delito_sabana s
                ON s.id_modalidad_delito = d.id_modalidad_delito
               AND s.id_grado_consumacion = d.id_grado_consumacion
               AND s.id_instrumento_comision = d.id_instrumento_comision
               AND s.id_forma_accion = d.id_forma_accion
               AND s.activo = 1
            INNER JOIN catalogo_modalidad_delito md
                ON md.id_modalidad_delito = s.id_modalidad_delito
               AND md.activo = 1
            INNER JOIN catalogo_subtipo_delito sd
                ON sd.id_subtipo_delito = md.id_subtipo_delito
               AND sd.activo = 1
            INNER JOIN catalogo_delito cd
                ON cd.id_delito = sd.id_delito
               AND cd.activo = 1
            INNER JOIN catalogo_bien_juridico bj
                ON bj.id_bien_juridico = cd.id_bien_juridico
               AND bj.activo = 1
            WHERE v.activo = 1
              AND TRY_CONVERT(int, efh.clave) BETWEEN 1 AND 32
            GROUP BY
                c.anio_corte,
                TRY_CONVERT(int, efh.clave),
                efh.nombre,
                CONCAT(
                    RIGHT('00' + CONVERT(varchar(2), TRY_CONVERT(int, efh.clave)), 2),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                ),
                mun.nombre,
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana,
                CASE
                    WHEN tv.clave = 1 AND sx.clave = 1 THEN 'Hombre'
                    WHEN tv.clave = 1 AND sx.clave = 2 THEN 'Mujer'
                    ELSE 'No identificado'
                END,
                CASE
                    WHEN tv.clave <> 1 THEN 'No especificado'
                    WHEN TRY_CONVERT(int, v.edad) IS NULL THEN 'No especificado'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 0 AND 12 THEN '0 a 12 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 13 AND 17 THEN '13 a 17 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 18 AND 29 THEN '18 a 29 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 30 AND 60 THEN '30 a 60 años'
                    WHEN TRY_CONVERT(int, v.edad) BETWEEN 61 AND 120 THEN 'Más de 60 años'
                    ELSE 'No especificado'
                END
        ),
        municipios_con_conteo AS (
            SELECT DISTINCT
                clave_municipio_compuesta
            FROM conteos
        ),
        resultado AS (
            SELECT
                anio_corte,
                clave_ent,
                entidad,
                clave_municipio_compuesta,
                municipio,
                orden_delito,
                bien_juridico,
                delito_sabana,
                subtipo_delito_sabana,
                modalidad_delito_sabana,
                sexo,
                rango_edad,
                enero,
                febrero,
                marzo,
                abril,
                mayo,
                junio,
                julio,
                agosto,
                septiembre,
                octubre,
                noviembre,
                diciembre
            FROM conteos

            UNION ALL

            SELECT
                m.anio_corte,
                m.clave_ent,
                m.entidad,
                m.clave_municipio_compuesta,
                m.municipio,
                m.orden_delito,
                m.bien_juridico,
                m.delito_sabana,
                m.subtipo_delito_sabana,
                m.modalidad_delito_sabana,
                m.sexo,
                m.rango_edad,
                m.enero,
                m.febrero,
                m.marzo,
                m.abril,
                m.mayo,
                m.junio,
                m.julio,
                m.agosto,
                m.septiembre,
                m.octubre,
                m.noviembre,
                m.diciembre
            FROM matriz_municipal_sin_conteo m
            LEFT JOIN municipios_con_conteo mc
                ON mc.clave_municipio_compuesta = m.clave_municipio_compuesta
            WHERE mc.clave_municipio_compuesta IS NULL
        )
        SELECT
            anio_corte AS [Año],
            RIGHT('00' + CONVERT(varchar(2), clave_ent), 2) AS [Clave_Ent],
            entidad AS [Entidad],
            clave_municipio_compuesta AS [Cve. Municipio],
            municipio AS [Municipio],
            bien_juridico AS [Bien jurídico afectado],
            delito_sabana AS [Tipo de delito],
            subtipo_delito_sabana AS [Subtipo de delito],
            modalidad_delito_sabana AS [Modalidad],
            sexo AS [Sexo],
            rango_edad AS [Rango de edad],
            enero AS [Enero],
            febrero AS [Febrero],
            marzo AS [Marzo],
            abril AS [Abril],
            mayo AS [Mayo],
            junio AS [Junio],
            julio AS [Julio],
            agosto AS [Agosto],
            septiembre AS [Septiembre],
            octubre AS [Octubre],
            noviembre AS [Noviembre],
            diciembre AS [Diciembre]
        FROM resultado
        ORDER BY
            clave_ent,
            clave_municipio_compuesta,
            orden_delito,
            subtipo_delito_sabana,
            modalidad_delito_sabana,
            sexo,
            rango_edad;
        ";

        return await QueryDictionaryAnioAsync(sql, anioCorte);
    }
}