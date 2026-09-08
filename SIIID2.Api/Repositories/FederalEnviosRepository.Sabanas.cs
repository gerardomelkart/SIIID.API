using Dapper;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public partial class FederalEnviosRepository
{
    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaEstatalDelitosAsync(
        int anioCorte,
        string modoPlano,
        int mesUltimoCorte)
    {
        var sql = ConstruirSqlSabanaDelitos(false);

        return await QueryDictionaryAsync(sql, new
        {
            AnioCorte = anioCorte,
            ModoPlano = modoPlano,
            MesUltimoCorte = mesUltimoCorte
        });
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalDelitosAsync(
        int anioCorte,
        string modoPlano,
        int mesUltimoCorte)
    {
        var sql = ConstruirSqlSabanaDelitos(true);

        return await QueryDictionaryAsync(sql, new
        {
            AnioCorte = anioCorte,
            ModoPlano = modoPlano,
            MesUltimoCorte = mesUltimoCorte
        });
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaEstatalVictimasAsync(
        int anioCorte,
        string modoPlano,
        int mesUltimoCorte)
    {
        var sql = ConstruirSqlSabanaVictimas(false);

        return await QueryDictionaryAsync(sql, new
        {
            AnioCorte = anioCorte,
            ModoPlano = modoPlano,
            MesUltimoCorte = mesUltimoCorte
        });
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalVictimasAsync(
        int anioCorte,
        string modoPlano,
        int mesUltimoCorte)
    {
        var sql = ConstruirSqlSabanaMunicipalVictimas();

        return await QueryDictionaryAsync(sql, new
        {
            AnioCorte = anioCorte,
            ModoPlano = modoPlano,
            MesUltimoCorte = mesUltimoCorte
        });
    }

    public async Task<InformeSabanaFirma> ObtenerFirmaSabanaAsync(int anioCorte)
    {
        const string sql = """
            WITH cargas_relevantes AS
            (
                SELECT
                    c.id_federal_carga,
                    c.tipo_carga,
                    c.estado,
                    c.mes_corte,
                    c.anio_corte,
                    c.fecha_validacion,
                    c.fecha_confirmacion
                FROM dbo.federal_carga c
                WHERE c.activo = 1
                  AND c.anio_corte = @AnioCorte
                  AND
                  (
                        (c.tipo_carga = N'CARGA_INICIAL' AND c.estado = N'CONFIRMADO')
                     OR (c.tipo_carga = N'ACTUALIZACION' AND c.estado = N'CONFIRMADO_ACTUALIZACION')
                     OR c.estado = N'PENDIENTE_APROBACION'
                  )
            ),
            ultimo_corte AS
            (
                SELECT MAX(mes_corte) AS MesUltimoCorte
                FROM cargas_relevantes
            )
            SELECT
                ISNULL(MAX(cr.id_federal_carga), 0) AS UltimoIdCarga,

                CONVERT(bigint, ISNULL(SUM(
                    CASE
                        WHEN
                            (cr.tipo_carga = N'CARGA_INICIAL' AND cr.estado = N'CONFIRMADO')
                            OR
                            (cr.tipo_carga = N'ACTUALIZACION' AND cr.estado = N'CONFIRMADO_ACTUALIZACION')
                        THEN 1
                        ELSE 0
                    END
                ), 0)) AS TotalCargasConfirmadas,

                CONVERT(bigint, ISNULL(SUM(
                    CASE
                        WHEN cr.estado = N'PENDIENTE_APROBACION'
                        THEN 1
                        ELSE 0
                    END
                ), 0)) AS TotalCargasPendientes,

                uc.MesUltimoCorte,

                MAX(COALESCE(cr.fecha_confirmacion, cr.fecha_validacion)) AS UltimaFechaMovimiento
            FROM ultimo_corte uc
            LEFT JOIN cargas_relevantes cr
                ON 1 = 1
            GROUP BY uc.MesUltimoCorte;
            """;

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QuerySingleAsync<InformeSabanaFirma>(sql, new
        {
            AnioCorte = anioCorte
        });
    }

    private static string ConstruirSqlSabanaDelitos(bool municipal)
    {
        var matrizMunicipioSelect = municipal
            ? """
                ,
                TRY_CONVERT(int, CONCAT(
                    TRY_CONVERT(int, ef.clave),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                )) AS clave_municipio_compuesta,
                mun.nombre AS municipio
              """
            : string.Empty;

        var matrizMunicipioJoin = municipal
            ? """
                INNER JOIN dbo.catalogo_municipio mun
                    ON mun.id_entidad_federativa = ef.id_entidad_federativa
                   AND mun.activo = 1
              """
            : string.Empty;

        var conteoMunicipioSelect = municipal
            ? """
                ,
                TRY_CONVERT(int, CONCAT(
                    TRY_CONVERT(int, efh.clave),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                )) AS clave_municipio_compuesta
              """
            : string.Empty;

        var conteoMunicipioJoin = municipal
            ? """
                INNER JOIN dbo.catalogo_municipio mun
                    ON mun.id_municipio = fd.id_municipio
                   AND mun.id_entidad_federativa = efh.id_entidad_federativa
                   AND mun.activo = 1
              """
            : string.Empty;

        var conteoMunicipioGroup = municipal
            ? """
                ,
                TRY_CONVERT(int, CONCAT(
                    TRY_CONVERT(int, efh.clave),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                ))
              """
            : string.Empty;

        var salidaMunicipio = municipal
            ? """
                m.clave_municipio_compuesta AS [Cve. Municipio],
                m.municipio AS [Municipio],
              """
            : string.Empty;

        var enlaceMunicipio = municipal
            ? """
               AND c.clave_municipio_compuesta = m.clave_municipio_compuesta
              """
            : string.Empty;

        var grupoMunicipio = municipal
            ? """
                ,
                m.clave_municipio_compuesta,
                m.municipio
              """
            : string.Empty;

        var ordenMunicipio = municipal
            ? """
                ,
                m.clave_municipio_compuesta
              """
            : string.Empty;

        return $"""
            WITH pendientes_rankeadas AS
            (
                SELECT
                    c.id_federal_carga,
                    c.mes_corte,
                    c.anio_corte,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY c.mes_corte, c.anio_corte
                        ORDER BY c.fecha_validacion DESC, c.id_federal_carga DESC
                    ) AS rn
                FROM dbo.federal_carga c
                WHERE c.activo = 1
                  AND c.estado = N'PENDIENTE_APROBACION'
                  AND c.anio_corte = @AnioCorte
            ),
            pendientes AS
            (
                SELECT
                    id_federal_carga,
                    mes_corte,
                    anio_corte
                FROM pendientes_rankeadas
                WHERE rn = 1
            ),
            fuente_delitos AS
            (
                SELECT
                    c.anio_corte,
                    c.mes_corte,
                    d.id_entidad_federativa,
                    d.id_municipio,
                    d.id_modalidad_delito,
                    d.id_grado_consumacion,
                    d.id_instrumento_comision,
                    d.id_forma_accion
                FROM dbo.federal_delito d
                INNER JOIN dbo.federal_carga c
                    ON c.id_federal_carga = d.id_federal_carga
                   AND c.activo = 1
                   AND c.anio_corte = @AnioCorte
                   AND
                   (
                        (c.tipo_carga = N'CARGA_INICIAL' AND c.estado = N'CONFIRMADO')
                        OR
                        (c.tipo_carga = N'ACTUALIZACION' AND c.estado = N'CONFIRMADO_ACTUALIZACION')
                   )
                WHERE d.activo = 1
                  AND
                  (
                        @ModoPlano = N'CONFIRMADO'
                        OR
                        (
                            @ModoPlano IN (N'PREVIO', N'MIXTO')
                            AND NOT EXISTS
                            (
                                SELECT 1
                                FROM pendientes p
                                WHERE p.anio_corte = c.anio_corte
                                  AND p.mes_corte = c.mes_corte
                            )
                        )
                  )

                UNION ALL

                SELECT
                    p.anio_corte,
                    p.mes_corte,
                    ef.id_entidad_federativa,
                    mun.id_municipio,
                    md.id_modalidad_delito,
                    gc.id_grado_consumacion,
                    ic.id_instrumento_comision,
                    fa.id_forma_accion
                FROM pendientes p
                INNER JOIN dbo.federal_carga_tmp_delito d
                    ON d.id_federal_carga = p.id_federal_carga
                   AND d.activo = 1
                INNER JOIN dbo.federal_catalogo_modalidad_delito md
                    ON md.clave4 = d.clasf_de_dto
                   AND md.activo = 1
                INNER JOIN dbo.catalogo_forma_accion fa
                    ON fa.clave = TRY_CONVERT(tinyint, d.forma_acc)
                   AND fa.activo = 1
                INNER JOIN dbo.catalogo_instrumento_comision ic
                    ON ic.clave = TRY_CONVERT(tinyint, d.emto_com_dto)
                   AND ic.activo = 1
                INNER JOIN dbo.catalogo_grado_consumacion gc
                    ON gc.clave = TRY_CONVERT(tinyint, d.grdo_cons)
                   AND gc.activo = 1
                INNER JOIN dbo.catalogo_entidad_federativa ef
                    ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos)
                   AND ef.activo = 1
                INNER JOIN dbo.catalogo_municipio mun
                    ON mun.id_entidad_federativa = ef.id_entidad_federativa
                   AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos)
                   AND mun.activo = 1
                WHERE @ModoPlano IN (N'PREVIO', N'MIXTO')
            ),
            sabana AS
            (
                SELECT
                    MIN(s.id_delito_sabana) AS orden_sabana,
                    MIN(cd.id_delito) AS orden_delito,
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana
                FROM dbo.federal_catalogo_delito_sabana s
                INNER JOIN dbo.federal_catalogo_modalidad_delito md
                    ON md.id_modalidad_delito = s.id_modalidad_delito
                   AND md.activo = 1
                INNER JOIN dbo.federal_catalogo_subtipo_delito sd
                    ON sd.id_subtipo_delito = md.id_subtipo_delito
                   AND sd.activo = 1
                INNER JOIN dbo.federal_catalogo_delito cd
                    ON cd.id_delito = sd.id_delito
                   AND cd.activo = 1
                INNER JOIN dbo.federal_catalogo_bien_juridico bj
                    ON bj.id_bien_juridico = cd.id_bien_juridico
                   AND bj.activo = 1
                WHERE s.activo = 1
                GROUP BY
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana
            ),
            matriz AS
            (
                SELECT
                    @AnioCorte AS anio_corte,
                    TRY_CONVERT(int, ef.clave) AS clave_ent,
                    ef.nombre AS entidad
                    {matrizMunicipioSelect},
                    s.orden_sabana,
                    s.orden_delito,
                    s.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana
                FROM dbo.catalogo_entidad_federativa ef
                {matrizMunicipioJoin}
                CROSS JOIN sabana s
                WHERE ef.activo = 1
                  AND TRY_CONVERT(int, ef.clave) BETWEEN 1 AND 33
            ),
            conteos AS
            (
                SELECT
                    fd.anio_corte,
                    fd.mes_corte,
                    TRY_CONVERT(int, efh.clave) AS clave_ent
                    {conteoMunicipioSelect},
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana,
                    COUNT(1) AS cantidad
                FROM fuente_delitos fd
                INNER JOIN dbo.catalogo_entidad_federativa efh
                    ON efh.id_entidad_federativa = fd.id_entidad_federativa
                   AND efh.activo = 1
                {conteoMunicipioJoin}
                INNER JOIN dbo.federal_catalogo_delito_sabana s
                    ON s.id_modalidad_delito = fd.id_modalidad_delito
                   AND s.id_grado_consumacion = fd.id_grado_consumacion
                   AND s.id_instrumento_comision = fd.id_instrumento_comision
                   AND s.id_forma_accion = fd.id_forma_accion
                   AND s.activo = 1
                INNER JOIN dbo.federal_catalogo_modalidad_delito md
                    ON md.id_modalidad_delito = s.id_modalidad_delito
                   AND md.activo = 1
                INNER JOIN dbo.federal_catalogo_subtipo_delito sd
                    ON sd.id_subtipo_delito = md.id_subtipo_delito
                   AND sd.activo = 1
                INNER JOIN dbo.federal_catalogo_delito cd
                    ON cd.id_delito = sd.id_delito
                   AND cd.activo = 1
                INNER JOIN dbo.federal_catalogo_bien_juridico bj
                    ON bj.id_bien_juridico = cd.id_bien_juridico
                   AND bj.activo = 1
                WHERE TRY_CONVERT(int, efh.clave) BETWEEN 1 AND 33
                GROUP BY
                    fd.anio_corte,
                    fd.mes_corte,
                    TRY_CONVERT(int, efh.clave)
                    {conteoMunicipioGroup},
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana
            )
            SELECT
                m.anio_corte AS [Año],
                RIGHT('00' + CONVERT(varchar(2), m.clave_ent), 2) AS [Clave_Ent],
                m.entidad AS [Entidad],
                {salidaMunicipio}
                m.bien_juridico AS [Bien jurídico afectado],
                m.delito_sabana AS [Tipo de delito],
                m.subtipo_delito_sabana AS [Subtipo de delito],
                m.modalidad_delito_sabana AS [Modalidad],
                ISNULL(SUM(CASE WHEN c.mes_corte = 1 THEN c.cantidad ELSE 0 END), 0) AS [Enero],
                ISNULL(SUM(CASE WHEN c.mes_corte = 2 THEN c.cantidad ELSE 0 END), 0) AS [Febrero],
                ISNULL(SUM(CASE WHEN c.mes_corte = 3 THEN c.cantidad ELSE 0 END), 0) AS [Marzo],
                ISNULL(SUM(CASE WHEN c.mes_corte = 4 THEN c.cantidad ELSE 0 END), 0) AS [Abril],
                ISNULL(SUM(CASE WHEN c.mes_corte = 5 THEN c.cantidad ELSE 0 END), 0) AS [Mayo],
                ISNULL(SUM(CASE WHEN c.mes_corte = 6 THEN c.cantidad ELSE 0 END), 0) AS [Junio],
                ISNULL(SUM(CASE WHEN c.mes_corte = 7 THEN c.cantidad ELSE 0 END), 0) AS [Julio],
                ISNULL(SUM(CASE WHEN c.mes_corte = 8 THEN c.cantidad ELSE 0 END), 0) AS [Agosto],
                ISNULL(SUM(CASE WHEN c.mes_corte = 9 THEN c.cantidad ELSE 0 END), 0) AS [Septiembre],
                ISNULL(SUM(CASE WHEN c.mes_corte = 10 THEN c.cantidad ELSE 0 END), 0) AS [Octubre],
                ISNULL(SUM(CASE WHEN c.mes_corte = 11 THEN c.cantidad ELSE 0 END), 0) AS [Noviembre],
                ISNULL(SUM(CASE WHEN c.mes_corte = 12 THEN c.cantidad ELSE 0 END), 0) AS [Diciembre]
            FROM matriz m
            LEFT JOIN conteos c
                ON c.anio_corte = m.anio_corte
               AND c.clave_ent = m.clave_ent
               {enlaceMunicipio}
               AND c.bien_juridico = m.bien_juridico
               AND c.delito_sabana = m.delito_sabana
               AND c.subtipo_delito_sabana = m.subtipo_delito_sabana
               AND c.modalidad_delito_sabana = m.modalidad_delito_sabana
            GROUP BY
                m.anio_corte,
                m.clave_ent,
                m.entidad
                {grupoMunicipio},
                m.orden_sabana,
                m.orden_delito,
                m.bien_juridico,
                m.delito_sabana,
                m.subtipo_delito_sabana,
                m.modalidad_delito_sabana
            ORDER BY
                m.clave_ent
                {ordenMunicipio},
                m.orden_sabana,
                m.orden_delito,
                m.subtipo_delito_sabana,
                m.modalidad_delito_sabana
            OPTION (RECOMPILE);
            """;
    }

    private static string ConstruirSqlSabanaVictimas(bool municipal)
    {
        var matrizMunicipioSelect = municipal
            ? """
                ,
                TRY_CONVERT(int, CONCAT(
                    TRY_CONVERT(int, ef.clave),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                )) AS clave_municipio_compuesta,
                mun.nombre AS municipio
              """
            : string.Empty;

        var matrizMunicipioJoin = municipal
            ? """
                INNER JOIN dbo.catalogo_municipio mun
                    ON mun.id_entidad_federativa = ef.id_entidad_federativa
                   AND mun.activo = 1
              """
            : string.Empty;

        var conteoMunicipioSelect = municipal
            ? """
                ,
                TRY_CONVERT(int, CONCAT(
                    TRY_CONVERT(int, efh.clave),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                )) AS clave_municipio_compuesta
              """
            : string.Empty;

        var conteoMunicipioJoin = municipal
            ? """
                INNER JOIN dbo.catalogo_municipio mun
                    ON mun.id_municipio = fv.id_municipio
                   AND mun.id_entidad_federativa = efh.id_entidad_federativa
                   AND mun.activo = 1
              """
            : string.Empty;

        var conteoMunicipioGroup = municipal
            ? """
                ,
                TRY_CONVERT(int, CONCAT(
                    TRY_CONVERT(int, efh.clave),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                ))
              """
            : string.Empty;

        var salidaMunicipio = municipal
            ? """
                m.clave_municipio_compuesta AS [Cve. Municipio],
                m.municipio AS [Municipio],
              """
            : string.Empty;

        var enlaceMunicipio = municipal
            ? """
               AND c.clave_municipio_compuesta = m.clave_municipio_compuesta
              """
            : string.Empty;

        var grupoMunicipio = municipal
            ? """
                ,
                m.clave_municipio_compuesta,
                m.municipio
              """
            : string.Empty;

        var ordenMunicipio = municipal
            ? """
                ,
                m.clave_municipio_compuesta
              """
            : string.Empty;

        return $"""
            WITH pendientes_rankeadas AS
            (
                SELECT
                    c.id_federal_carga,
                    c.mes_corte,
                    c.anio_corte,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY c.mes_corte, c.anio_corte
                        ORDER BY c.fecha_validacion DESC, c.id_federal_carga DESC
                    ) AS rn
                FROM dbo.federal_carga c
                WHERE c.activo = 1
                  AND c.estado = N'PENDIENTE_APROBACION'
                  AND c.anio_corte = @AnioCorte
            ),
            pendientes AS
            (
                SELECT
                    id_federal_carga,
                    mes_corte,
                    anio_corte
                FROM pendientes_rankeadas
                WHERE rn = 1
            ),
            fuente_victimas AS
            (
                SELECT
                    c.anio_corte,
                    c.mes_corte,
                    d.id_entidad_federativa,
                    d.id_municipio,
                    d.id_modalidad_delito,
                    d.id_grado_consumacion,
                    d.id_instrumento_comision,
                    d.id_forma_accion,
                    tv.clave AS tipo_victima_clave,
                    sx.clave AS sexo_clave,
                    sx.descripcion AS sexo_descripcion,
                    CASE
                        WHEN v.edad = 999 THEN NULL
                        ELSE TRY_CONVERT(int, v.edad)
                    END AS edad
                FROM dbo.federal_victima v
                INNER JOIN dbo.federal_delito d
                    ON d.id_federal_delito = v.id_federal_delito
                   AND d.activo = 1
                INNER JOIN dbo.federal_carga c
                    ON c.id_federal_carga = v.id_federal_carga
                   AND c.activo = 1
                   AND c.anio_corte = @AnioCorte
                   AND
                   (
                        (c.tipo_carga = N'CARGA_INICIAL' AND c.estado = N'CONFIRMADO')
                        OR
                        (c.tipo_carga = N'ACTUALIZACION' AND c.estado = N'CONFIRMADO_ACTUALIZACION')
                   )
                INNER JOIN dbo.catalogo_tipo_victima tv
                    ON tv.id_tipo_victima = v.id_tipo_victima
                   AND tv.activo = 1
                LEFT JOIN dbo.catalogo_sexo sx
                    ON sx.id_sexo = v.id_sexo
                   AND sx.activo = 1
                WHERE v.activo = 1
                  AND
                  (
                        @ModoPlano = N'CONFIRMADO'
                        OR
                        (
                            @ModoPlano IN (N'PREVIO', N'MIXTO')
                            AND NOT EXISTS
                            (
                                SELECT 1
                                FROM pendientes p
                                WHERE p.anio_corte = c.anio_corte
                                  AND p.mes_corte = c.mes_corte
                            )
                        )
                  )

                UNION ALL

                SELECT
                    p.anio_corte,
                    p.mes_corte,
                    ef.id_entidad_federativa,
                    mun.id_municipio,
                    md.id_modalidad_delito,
                    gc.id_grado_consumacion,
                    ic.id_instrumento_comision,
                    fa.id_forma_accion,
                    tv.clave,
                    sx.clave,
                    sx.descripcion,
                    CASE
                        WHEN TRY_CONVERT(int, NULLIF(v.edad, N'')) = 999 THEN NULL
                        ELSE TRY_CONVERT(int, NULLIF(v.edad, N''))
                    END
                FROM pendientes p
                INNER JOIN dbo.federal_carga_tmp_victima v
                    ON v.id_federal_carga = p.id_federal_carga
                   AND v.activo = 1
                INNER JOIN dbo.federal_carga_tmp_delito d
                    ON d.id_federal_carga = v.id_federal_carga
                   AND d.id_ci = v.id_ci
                   AND d.id_delito = v.id_delito
                   AND d.activo = 1
                INNER JOIN dbo.federal_catalogo_modalidad_delito md
                    ON md.clave4 = d.clasf_de_dto
                   AND md.activo = 1
                INNER JOIN dbo.catalogo_forma_accion fa
                    ON fa.clave = TRY_CONVERT(tinyint, d.forma_acc)
                   AND fa.activo = 1
                INNER JOIN dbo.catalogo_instrumento_comision ic
                    ON ic.clave = TRY_CONVERT(tinyint, d.emto_com_dto)
                   AND ic.activo = 1
                INNER JOIN dbo.catalogo_grado_consumacion gc
                    ON gc.clave = TRY_CONVERT(tinyint, d.grdo_cons)
                   AND gc.activo = 1
                INNER JOIN dbo.catalogo_entidad_federativa ef
                    ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos)
                   AND ef.activo = 1
                INNER JOIN dbo.catalogo_municipio mun
                    ON mun.id_entidad_federativa = ef.id_entidad_federativa
                   AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos)
                   AND mun.activo = 1
                INNER JOIN dbo.catalogo_tipo_victima tv
                    ON tv.clave = TRY_CONVERT(tinyint, v.id_tv)
                   AND tv.activo = 1
                LEFT JOIN dbo.catalogo_sexo sx
                    ON sx.clave = TRY_CONVERT(tinyint, NULLIF(v.sexo, N''))
                   AND sx.activo = 1
                WHERE @ModoPlano IN (N'PREVIO', N'MIXTO')
            ),
            sexos AS
            (
                SELECT 1 AS orden_sexo, N'Hombre' AS sexo
                UNION ALL SELECT 2, N'Mujer'
                UNION ALL SELECT 3, N'No identificado'
            ),
            rangos_edad AS
            (
                SELECT 1 AS orden_rango, N'0 a 12 años' AS rango_edad
                UNION ALL SELECT 2, N'13 a 17 años'
                UNION ALL SELECT 3, N'18 a 29 años'
                UNION ALL SELECT 4, N'30 a 60 años'
                UNION ALL SELECT 5, N'Más de 60 años'
                UNION ALL SELECT 6, N'No especificado'
            ),
            sabana AS
            (
                SELECT
                    MIN(s.id_delito_sabana) AS orden_sabana,
                    MIN(cd.id_delito) AS orden_delito,
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana
                FROM dbo.federal_catalogo_delito_sabana s
                INNER JOIN dbo.federal_catalogo_modalidad_delito md
                    ON md.id_modalidad_delito = s.id_modalidad_delito
                   AND md.activo = 1
                INNER JOIN dbo.federal_catalogo_subtipo_delito sd
                    ON sd.id_subtipo_delito = md.id_subtipo_delito
                   AND sd.activo = 1
                INNER JOIN dbo.federal_catalogo_delito cd
                    ON cd.id_delito = sd.id_delito
                   AND cd.activo = 1
                INNER JOIN dbo.federal_catalogo_bien_juridico bj
                    ON bj.id_bien_juridico = cd.id_bien_juridico
                   AND bj.activo = 1
                WHERE s.activo = 1
                GROUP BY
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana
            ),
            matriz AS
            (
                SELECT
                    @AnioCorte AS anio_corte,
                    TRY_CONVERT(int, ef.clave) AS clave_ent,
                    ef.nombre AS entidad
                    {matrizMunicipioSelect},
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
                FROM dbo.catalogo_entidad_federativa ef
                {matrizMunicipioJoin}
                CROSS JOIN sabana s
                CROSS JOIN sexos sx
                CROSS JOIN rangos_edad re
                WHERE ef.activo = 1
                  AND TRY_CONVERT(int, ef.clave) BETWEEN 1 AND 33
            ),
            conteos AS
            (
                SELECT
                    fv.anio_corte,
                    fv.mes_corte,
                    TRY_CONVERT(int, efh.clave) AS clave_ent
                    {conteoMunicipioSelect},
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana,
                    CASE
                        WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave IN (1, 2, 3)
                            THEN ISNULL(fv.sexo_descripcion, N'No identificado')
                        ELSE N'No identificado'
                    END AS sexo,
                    CASE
                        WHEN fv.tipo_victima_clave <> 1 THEN N'No especificado'
                        WHEN fv.edad IS NULL THEN N'No especificado'
                        WHEN fv.edad BETWEEN 0 AND 12 THEN N'0 a 12 años'
                        WHEN fv.edad BETWEEN 13 AND 17 THEN N'13 a 17 años'
                        WHEN fv.edad BETWEEN 18 AND 29 THEN N'18 a 29 años'
                        WHEN fv.edad BETWEEN 30 AND 60 THEN N'30 a 60 años'
                        WHEN fv.edad BETWEEN 61 AND 120 THEN N'Más de 60 años'
                        ELSE N'No especificado'
                    END AS rango_edad,
                    COUNT(1) AS cantidad
                FROM fuente_victimas fv
                INNER JOIN dbo.catalogo_entidad_federativa efh
                    ON efh.id_entidad_federativa = fv.id_entidad_federativa
                   AND efh.activo = 1
                {conteoMunicipioJoin}
                INNER JOIN dbo.federal_catalogo_delito_sabana s
                    ON s.id_modalidad_delito = fv.id_modalidad_delito
                   AND s.id_grado_consumacion = fv.id_grado_consumacion
                   AND s.id_instrumento_comision = fv.id_instrumento_comision
                   AND s.id_forma_accion = fv.id_forma_accion
                   AND s.activo = 1
                INNER JOIN dbo.federal_catalogo_modalidad_delito md
                    ON md.id_modalidad_delito = s.id_modalidad_delito
                   AND md.activo = 1
                INNER JOIN dbo.federal_catalogo_subtipo_delito sd
                    ON sd.id_subtipo_delito = md.id_subtipo_delito
                   AND sd.activo = 1
                INNER JOIN dbo.federal_catalogo_delito cd
                    ON cd.id_delito = sd.id_delito
                   AND cd.activo = 1
                INNER JOIN dbo.federal_catalogo_bien_juridico bj
                    ON bj.id_bien_juridico = cd.id_bien_juridico
                   AND bj.activo = 1
                WHERE TRY_CONVERT(int, efh.clave) BETWEEN 1 AND 33
                GROUP BY
                    fv.anio_corte,
                    fv.mes_corte,
                    TRY_CONVERT(int, efh.clave)
                    {conteoMunicipioGroup},
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana,
                    CASE
                        WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave IN (1, 2, 3)
                            THEN ISNULL(fv.sexo_descripcion, N'No identificado')
                        ELSE N'No identificado'
                    END,
                    CASE
                        WHEN fv.tipo_victima_clave <> 1 THEN N'No especificado'
                        WHEN fv.edad IS NULL THEN N'No especificado'
                        WHEN fv.edad BETWEEN 0 AND 12 THEN N'0 a 12 años'
                        WHEN fv.edad BETWEEN 13 AND 17 THEN N'13 a 17 años'
                        WHEN fv.edad BETWEEN 18 AND 29 THEN N'18 a 29 años'
                        WHEN fv.edad BETWEEN 30 AND 60 THEN N'30 a 60 años'
                        WHEN fv.edad BETWEEN 61 AND 120 THEN N'Más de 60 años'
                        ELSE N'No especificado'
                    END
            )
            SELECT
                m.anio_corte AS [Año],
                RIGHT('00' + CONVERT(varchar(2), m.clave_ent), 2) AS [Clave_Ent],
                m.entidad AS [Entidad],
                {salidaMunicipio}
                m.bien_juridico AS [Bien jurídico afectado],
                m.delito_sabana AS [Tipo de delito],
                m.subtipo_delito_sabana AS [Subtipo de delito],
                m.modalidad_delito_sabana AS [Modalidad],
                m.sexo AS [Sexo],
                m.rango_edad AS [Rango de edad],
                ISNULL(SUM(CASE WHEN c.mes_corte = 1 THEN c.cantidad ELSE 0 END), 0) AS [Enero],
                ISNULL(SUM(CASE WHEN c.mes_corte = 2 THEN c.cantidad ELSE 0 END), 0) AS [Febrero],
                ISNULL(SUM(CASE WHEN c.mes_corte = 3 THEN c.cantidad ELSE 0 END), 0) AS [Marzo],
                ISNULL(SUM(CASE WHEN c.mes_corte = 4 THEN c.cantidad ELSE 0 END), 0) AS [Abril],
                ISNULL(SUM(CASE WHEN c.mes_corte = 5 THEN c.cantidad ELSE 0 END), 0) AS [Mayo],
                ISNULL(SUM(CASE WHEN c.mes_corte = 6 THEN c.cantidad ELSE 0 END), 0) AS [Junio],
                ISNULL(SUM(CASE WHEN c.mes_corte = 7 THEN c.cantidad ELSE 0 END), 0) AS [Julio],
                ISNULL(SUM(CASE WHEN c.mes_corte = 8 THEN c.cantidad ELSE 0 END), 0) AS [Agosto],
                ISNULL(SUM(CASE WHEN c.mes_corte = 9 THEN c.cantidad ELSE 0 END), 0) AS [Septiembre],
                ISNULL(SUM(CASE WHEN c.mes_corte = 10 THEN c.cantidad ELSE 0 END), 0) AS [Octubre],
                ISNULL(SUM(CASE WHEN c.mes_corte = 11 THEN c.cantidad ELSE 0 END), 0) AS [Noviembre],
                ISNULL(SUM(CASE WHEN c.mes_corte = 12 THEN c.cantidad ELSE 0 END), 0) AS [Diciembre]
            FROM matriz m
            LEFT JOIN conteos c
                ON c.anio_corte = m.anio_corte
               AND c.clave_ent = m.clave_ent
               {enlaceMunicipio}
               AND c.bien_juridico = m.bien_juridico
               AND c.delito_sabana = m.delito_sabana
               AND c.subtipo_delito_sabana = m.subtipo_delito_sabana
               AND c.modalidad_delito_sabana = m.modalidad_delito_sabana
               AND c.sexo = m.sexo
               AND c.rango_edad = m.rango_edad
            GROUP BY
                m.anio_corte,
                m.clave_ent,
                m.entidad
                {grupoMunicipio},
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
                m.clave_ent
                {ordenMunicipio},
                m.orden_sabana,
                m.orden_delito,
                m.subtipo_delito_sabana,
                m.modalidad_delito_sabana,
                m.orden_sexo,
                m.orden_rango
            OPTION (RECOMPILE);
            """;
    }

    private static string ConstruirSqlSabanaMunicipalVictimas()
    {
        return """
            WITH pendientes_rankeadas AS
            (
                SELECT
                    c.id_federal_carga,
                    c.mes_corte,
                    c.anio_corte,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY c.mes_corte, c.anio_corte
                        ORDER BY c.fecha_validacion DESC, c.id_federal_carga DESC
                    ) AS rn
                FROM dbo.federal_carga c
                WHERE c.activo = 1
                  AND c.estado = N'PENDIENTE_APROBACION'
                  AND c.anio_corte = @AnioCorte
            ),
            pendientes AS
            (
                SELECT
                    id_federal_carga,
                    mes_corte,
                    anio_corte
                FROM pendientes_rankeadas
                WHERE rn = 1
            ),
            fuente_victimas AS
            (
                SELECT
                    c.anio_corte,
                    c.mes_corte,
                    d.id_entidad_federativa,
                    d.id_municipio,
                    d.id_modalidad_delito,
                    d.id_grado_consumacion,
                    d.id_instrumento_comision,
                    d.id_forma_accion,
                    tv.clave AS tipo_victima_clave,
                    sx.clave AS sexo_clave,
                    sx.descripcion AS sexo_descripcion,
                    CASE
                        WHEN v.edad = 999 THEN NULL
                        ELSE TRY_CONVERT(int, v.edad)
                    END AS edad
                FROM dbo.federal_victima v
                INNER JOIN dbo.federal_delito d
                    ON d.id_federal_delito = v.id_federal_delito
                   AND d.activo = 1
                INNER JOIN dbo.federal_carga c
                    ON c.id_federal_carga = v.id_federal_carga
                   AND c.activo = 1
                   AND c.anio_corte = @AnioCorte
                   AND
                   (
                        (c.tipo_carga = N'CARGA_INICIAL' AND c.estado = N'CONFIRMADO')
                        OR
                        (c.tipo_carga = N'ACTUALIZACION' AND c.estado = N'CONFIRMADO_ACTUALIZACION')
                   )
                INNER JOIN dbo.catalogo_tipo_victima tv
                    ON tv.id_tipo_victima = v.id_tipo_victima
                   AND tv.activo = 1
                LEFT JOIN dbo.catalogo_sexo sx
                    ON sx.id_sexo = v.id_sexo
                   AND sx.activo = 1
                WHERE v.activo = 1
                  AND
                  (
                        @ModoPlano = N'CONFIRMADO'
                        OR
                        (
                            @ModoPlano IN (N'PREVIO', N'MIXTO')
                            AND NOT EXISTS
                            (
                                SELECT 1
                                FROM pendientes p
                                WHERE p.anio_corte = c.anio_corte
                                  AND p.mes_corte = c.mes_corte
                            )
                        )
                  )

                UNION ALL

                SELECT
                    p.anio_corte,
                    p.mes_corte,
                    ef.id_entidad_federativa,
                    mun.id_municipio,
                    md.id_modalidad_delito,
                    gc.id_grado_consumacion,
                    ic.id_instrumento_comision,
                    fa.id_forma_accion,
                    tv.clave,
                    sx.clave,
                    sx.descripcion,
                    CASE
                        WHEN TRY_CONVERT(int, NULLIF(v.edad, N'')) = 999 THEN NULL
                        ELSE TRY_CONVERT(int, NULLIF(v.edad, N''))
                    END
                FROM pendientes p
                INNER JOIN dbo.federal_carga_tmp_victima v
                    ON v.id_federal_carga = p.id_federal_carga
                   AND v.activo = 1
                INNER JOIN dbo.federal_carga_tmp_delito d
                    ON d.id_federal_carga = v.id_federal_carga
                   AND d.id_ci = v.id_ci
                   AND d.id_delito = v.id_delito
                   AND d.activo = 1
                INNER JOIN dbo.federal_catalogo_modalidad_delito md
                    ON md.clave4 = d.clasf_de_dto
                   AND md.activo = 1
                INNER JOIN dbo.catalogo_forma_accion fa
                    ON fa.clave = TRY_CONVERT(tinyint, d.forma_acc)
                   AND fa.activo = 1
                INNER JOIN dbo.catalogo_instrumento_comision ic
                    ON ic.clave = TRY_CONVERT(tinyint, d.emto_com_dto)
                   AND ic.activo = 1
                INNER JOIN dbo.catalogo_grado_consumacion gc
                    ON gc.clave = TRY_CONVERT(tinyint, d.grdo_cons)
                   AND gc.activo = 1
                INNER JOIN dbo.catalogo_entidad_federativa ef
                    ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos)
                   AND ef.activo = 1
                INNER JOIN dbo.catalogo_municipio mun
                    ON mun.id_entidad_federativa = ef.id_entidad_federativa
                   AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos)
                   AND mun.activo = 1
                INNER JOIN dbo.catalogo_tipo_victima tv
                    ON tv.clave = TRY_CONVERT(tinyint, v.id_tv)
                   AND tv.activo = 1
                LEFT JOIN dbo.catalogo_sexo sx
                    ON sx.clave = TRY_CONVERT(tinyint, NULLIF(v.sexo, N''))
                   AND sx.activo = 1
                WHERE @ModoPlano IN (N'PREVIO', N'MIXTO')
            ),
            sabana AS (
            SELECT
                MIN(s.id_delito_sabana) AS orden_sabana,
                        MIN(cd.id_delito) AS orden_delito,
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana
                FROM dbo.federal_catalogo_delito_sabana s
                INNER JOIN dbo.federal_catalogo_modalidad_delito md
                    ON md.id_modalidad_delito = s.id_modalidad_delito
                   AND md.activo = 1
                INNER JOIN dbo.federal_catalogo_subtipo_delito sd
                    ON sd.id_subtipo_delito = md.id_subtipo_delito
                   AND sd.activo = 1
                INNER JOIN dbo.federal_catalogo_delito cd
                    ON cd.id_delito = sd.id_delito
                   AND cd.activo = 1
                INNER JOIN dbo.federal_catalogo_bien_juridico bj
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
                    TRY_CONVERT(int, CONCAT(
                        TRY_CONVERT(int, ef.clave),
                        RIGHT(N'000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                    )) AS clave_municipio_compuesta,
                    mun.nombre AS municipio,
                    s.orden_sabana,
                    s.orden_delito,
                    s.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana,
                    N'No identificado' AS sexo,
                    N'No especificado' AS rango_edad,
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
                FROM dbo.catalogo_entidad_federativa ef
                INNER JOIN dbo.catalogo_municipio mun
                    ON mun.id_entidad_federativa = ef.id_entidad_federativa
                   AND mun.activo = 1
                CROSS JOIN sabana s
                WHERE ef.activo = 1
                  AND TRY_CONVERT(int, ef.clave) BETWEEN 1 AND 33
            ),
                conteos AS (
                    SELECT
                        fv.anio_corte,
                        TRY_CONVERT(int, efh.clave) AS clave_ent,
                        efh.nombre AS entidad,
                        TRY_CONVERT(int, CONCAT(
                            TRY_CONVERT(int, efh.clave),
                            RIGHT(N'000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                        )) AS clave_municipio_compuesta,
                        mun.nombre AS municipio,
                        MIN(s.id_delito_sabana) AS orden_sabana,
                        MIN(cd.id_delito) AS orden_delito,
                        bj.bien_juridico,
                        s.delito_sabana,
                        s.subtipo_delito_sabana,
                        s.modalidad_delito_sabana,
                        CASE
                            WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave = 1 THEN N'Hombre'
                            WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave = 2 THEN N'Mujer'
                            ELSE N'No identificado'
                        END AS sexo,
                        CASE
                            WHEN fv.tipo_victima_clave <> 1 THEN N'No especificado'
                            WHEN fv.edad IS NULL THEN N'No especificado'
                            WHEN fv.edad BETWEEN 0 AND 12 THEN N'0 a 12 años'
                            WHEN fv.edad BETWEEN 13 AND 17 THEN N'13 a 17 años'
                            WHEN fv.edad BETWEEN 18 AND 29 THEN N'18 a 29 años'
                            WHEN fv.edad BETWEEN 30 AND 60 THEN N'30 a 60 años'
                            WHEN fv.edad BETWEEN 61 AND 120 THEN N'Más de 60 años'
                            ELSE N'No especificado'
                        END AS rango_edad,
                        SUM(CASE WHEN fv.mes_corte = 1 THEN 1 ELSE 0 END) AS enero,
                        SUM(CASE WHEN fv.mes_corte = 2 THEN 1 ELSE 0 END) AS febrero,
                        SUM(CASE WHEN fv.mes_corte = 3 THEN 1 ELSE 0 END) AS marzo,
                        SUM(CASE WHEN fv.mes_corte = 4 THEN 1 ELSE 0 END) AS abril,
                        SUM(CASE WHEN fv.mes_corte = 5 THEN 1 ELSE 0 END) AS mayo,
                        SUM(CASE WHEN fv.mes_corte = 6 THEN 1 ELSE 0 END) AS junio,
                        SUM(CASE WHEN fv.mes_corte = 7 THEN 1 ELSE 0 END) AS julio,
                        SUM(CASE WHEN fv.mes_corte = 8 THEN 1 ELSE 0 END) AS agosto,
                        SUM(CASE WHEN fv.mes_corte = 9 THEN 1 ELSE 0 END) AS septiembre,
                        SUM(CASE WHEN fv.mes_corte = 10 THEN 1 ELSE 0 END) AS octubre,
                        SUM(CASE WHEN fv.mes_corte = 11 THEN 1 ELSE 0 END) AS noviembre,
                        SUM(CASE WHEN fv.mes_corte = 12 THEN 1 ELSE 0 END) AS diciembre
                    FROM fuente_victimas fv
                    INNER JOIN dbo.catalogo_entidad_federativa efh
                        ON efh.id_entidad_federativa = fv.id_entidad_federativa
                       AND efh.activo = 1
                    INNER JOIN dbo.catalogo_municipio mun
                        ON mun.id_municipio = fv.id_municipio
                       AND mun.id_entidad_federativa = efh.id_entidad_federativa
                       AND mun.activo = 1
                    INNER JOIN dbo.federal_catalogo_delito_sabana s
                        ON s.id_modalidad_delito = fv.id_modalidad_delito
                       AND s.id_grado_consumacion = fv.id_grado_consumacion
                       AND s.id_instrumento_comision = fv.id_instrumento_comision
                       AND s.id_forma_accion = fv.id_forma_accion
                       AND s.activo = 1
                    INNER JOIN dbo.federal_catalogo_modalidad_delito md
                        ON md.id_modalidad_delito = s.id_modalidad_delito
                       AND md.activo = 1
                    INNER JOIN dbo.federal_catalogo_subtipo_delito sd
                        ON sd.id_subtipo_delito = md.id_subtipo_delito
                       AND sd.activo = 1
                    INNER JOIN dbo.federal_catalogo_delito cd
                        ON cd.id_delito = sd.id_delito
                       AND cd.activo = 1
                    INNER JOIN dbo.federal_catalogo_bien_juridico bj
                        ON bj.id_bien_juridico = cd.id_bien_juridico
                       AND bj.activo = 1
                    WHERE TRY_CONVERT(int, efh.clave) BETWEEN 1 AND 33
                    GROUP BY
                        fv.anio_corte,
                        TRY_CONVERT(int, efh.clave),
                        efh.nombre,
                        TRY_CONVERT(int, CONCAT(
                            TRY_CONVERT(int, efh.clave),
                            RIGHT(N'000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                        )),
                        mun.nombre,
                        bj.bien_juridico,
                        s.delito_sabana,
                        s.subtipo_delito_sabana,
                        s.modalidad_delito_sabana,
                        CASE
                            WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave = 1 THEN N'Hombre'
                            WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave = 2 THEN N'Mujer'
                            ELSE N'No identificado'
                        END,
                        CASE
                            WHEN fv.tipo_victima_clave <> 1 THEN N'No especificado'
                            WHEN fv.edad IS NULL THEN N'No especificado'
                            WHEN fv.edad BETWEEN 0 AND 12 THEN N'0 a 12 años'
                            WHEN fv.edad BETWEEN 13 AND 17 THEN N'13 a 17 años'
                            WHEN fv.edad BETWEEN 18 AND 29 THEN N'18 a 29 años'
                            WHEN fv.edad BETWEEN 30 AND 60 THEN N'30 a 60 años'
                            WHEN fv.edad BETWEEN 61 AND 120 THEN N'Más de 60 años'
                            ELSE N'No especificado'
                        END

            ),
            municipios_con_conteo AS (
                SELECT DISTINCT
                    clave_municipio_compuesta
                FROM conteos
            ),
            resultado AS (
                SELECT
                    1 AS bloque_resultado,
                    anio_corte,
                    clave_ent,
                    entidad,
                    clave_municipio_compuesta,
                    municipio,
                    orden_sabana,
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
                    2 AS bloque_resultado,
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
                RIGHT(N'00' + CONVERT(varchar(2), clave_ent), 2) AS [Clave_Ent],
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
                bloque_resultado,

                CASE WHEN bloque_resultado = 1 THEN clave_ent END,
                CASE WHEN bloque_resultado = 1 THEN clave_municipio_compuesta END,
                CASE WHEN bloque_resultado = 1 THEN orden_sabana END,

                CASE WHEN bloque_resultado = 2 THEN orden_sabana END,
                CASE WHEN bloque_resultado = 2 THEN clave_ent END,
                CASE WHEN bloque_resultado = 2 THEN clave_municipio_compuesta END,

                CASE sexo
                    WHEN N'Hombre' THEN 1
                    WHEN N'Mujer' THEN 2
                    ELSE 3
                END,
                CASE rango_edad
                    WHEN N'0 a 12 años' THEN 1
                    WHEN N'13 a 17 años' THEN 2
                    WHEN N'18 a 29 años' THEN 3
                    WHEN N'30 a 60 años' THEN 4
                    WHEN N'Más de 60 años' THEN 5
                    ELSE 6
                END
            OPTION (RECOMPILE);
            """;
    }
}