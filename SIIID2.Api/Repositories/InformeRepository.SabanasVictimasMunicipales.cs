using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

// Conteo municipal de víctimas; mantiene las reglas de clasificación existentes.
public partial class InformeRepository : IInformeRepository
{
    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalVictimasAsync(int anioCorte, int? idEntidadFederativa, string modoPlano, int mesUltimoCorte)
    {
        var sql = @"
                WITH pendientes_rankeadas AS (
            SELECT
                c.id_carga,
                c.id_entidad_federativa,
                c.mes_corte,
                c.anio_corte,
                ROW_NUMBER() OVER (
                    PARTITION BY c.id_entidad_federativa, c.mes_corte, c.anio_corte
                    ORDER BY c.fecha_validacion DESC, c.id_carga DESC
                ) AS rn
            FROM carga c
            WHERE c.activo = 1
              AND c.estado = 'PENDIENTE_APROBACION'
              AND c.anio_corte = @AnioCorte
              AND (@IdEntidadFederativa IS NULL OR c.id_entidad_federativa = @IdEntidadFederativa)
        ),
        pendientes AS (
            SELECT
                id_carga,
                id_entidad_federativa,
                mes_corte,
                anio_corte
            FROM pendientes_rankeadas
            WHERE rn = 1
        ),
        fuente_victimas AS (
            SELECT
                c.anio_corte,
                c.mes_corte,
                c.id_entidad_federativa AS id_entidad_carga,
                d.id_entidad_federativa AS id_entidad_hechos,
                d.id_municipio,
                d.id_modalidad_delito,
                d.id_grado_consumacion,
                d.id_instrumento_comision,
                d.id_forma_accion,
                tv.clave AS tipo_victima_clave,
                sx.clave AS sexo_clave,
                sx.descripcion AS sexo_descripcion,
                TRY_CONVERT(int, v.edad) AS edad
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
            INNER JOIN catalogo_tipo_victima tv
                ON tv.id_tipo_victima = v.id_tipo_victima
               AND tv.activo = 1
            LEFT JOIN catalogo_sexo sx
                ON sx.id_sexo = v.id_sexo
               AND sx.activo = 1
            WHERE v.activo = 1
              AND (@IdEntidadFederativa IS NULL OR c.id_entidad_federativa = @IdEntidadFederativa)
                AND (
                    @ModoPlano = 'CONFIRMADO'
                    OR (
                        @ModoPlano = 'PREVIO'
                        AND NOT EXISTS (
                            SELECT 1
                            FROM pendientes p
                            WHERE p.anio_corte = c.anio_corte
                              AND p.mes_corte = c.mes_corte
                        )
                    )
                    OR (
                        @ModoPlano = 'MIXTO'
                        AND NOT EXISTS (
                            SELECT 1
                            FROM pendientes p
                            WHERE p.id_entidad_federativa = c.id_entidad_federativa
                              AND p.anio_corte = c.anio_corte
                              AND p.mes_corte = c.mes_corte
                        )
                    )
                )

            UNION ALL

            SELECT
                p.anio_corte,
                p.mes_corte,
                p.id_entidad_federativa,
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
                    WHEN TRY_CONVERT(int, NULLIF(v.edad, '')) = 999 THEN NULL
                    ELSE TRY_CONVERT(int, NULLIF(v.edad, ''))
                END
            FROM pendientes p
            INNER JOIN carga_tmp_victima v
                ON v.id_carga = p.id_carga
               AND v.activo = 1
            INNER JOIN carga_tmp_delito d
                ON d.id_carga = v.id_carga
               AND d.id_ci = v.id_ci
               AND d.id_delito = v.id_delito
               AND d.activo = 1
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
            INNER JOIN catalogo_tipo_victima tv
                ON tv.clave = TRY_CONVERT(tinyint, v.id_tv)
               AND tv.activo = 1
            LEFT JOIN catalogo_sexo sx
                ON sx.clave = TRY_CONVERT(tinyint, NULLIF(v.sexo, ''))
               AND sx.activo = 1
            WHERE @ModoPlano IN ('PREVIO', 'MIXTO')
        ),
        sabana AS (
        SELECT
            MIN(COALESCE(ol.orden_municipal_victimas, ol.orden_general, s.id_delito_sabana)) AS orden_sabana,
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
            LEFT JOIN dbo.catalogo_sabana_orden_legacy ol
                ON ol.bien_juridico = bj.bien_juridico
               AND ol.delito_sabana = s.delito_sabana
               AND ol.subtipo_delito_sabana = s.subtipo_delito_sabana
               AND ol.modalidad_delito_sabana = s.modalidad_delito_sabana
               AND ol.activo = 1
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
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                )) AS clave_municipio_compuesta,
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
              AND (@IdEntidadFederativa IS NULL OR ef.id_entidad_federativa = @IdEntidadFederativa)
        ),
            conteos AS (
                SELECT
                    fv.anio_corte,
                    TRY_CONVERT(int, efh.clave) AS clave_ent,
                    efh.nombre AS entidad,
                    TRY_CONVERT(int, CONCAT(
                        TRY_CONVERT(int, efh.clave),
                        RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                    )) AS clave_municipio_compuesta,
                    mun.nombre AS municipio,
                    MIN(COALESCE(ol.orden_municipal_victimas, ol.orden_general, s.id_delito_sabana)) AS orden_sabana,
                    MIN(cd.id_delito) AS orden_delito,
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana,
                    CASE
                        WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave = 1 THEN 'Hombre'
                        WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave = 2 THEN 'Mujer'
                        ELSE 'No identificado'
                    END AS sexo,
                    CASE
                        WHEN fv.tipo_victima_clave <> 1 THEN 'No especificado'
                        WHEN fv.edad IS NULL THEN 'No especificado'
                        WHEN fv.edad BETWEEN 0 AND 12 THEN '0 a 12 años'
                        WHEN fv.edad BETWEEN 13 AND 17 THEN '13 a 17 años'
                        WHEN fv.edad BETWEEN 18 AND 29 THEN '18 a 29 años'
                        WHEN fv.edad BETWEEN 30 AND 60 THEN '30 a 60 años'
                        WHEN fv.edad BETWEEN 61 AND 120 THEN 'Más de 60 años'
                        ELSE 'No especificado'
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
                INNER JOIN catalogo_entidad_federativa efh
                    ON efh.id_entidad_federativa = fv.id_entidad_hechos
                   AND efh.activo = 1
                INNER JOIN catalogo_municipio mun
                    ON mun.id_municipio = fv.id_municipio
                   AND mun.activo = 1
                INNER JOIN catalogo_delito_sabana s
                    ON s.id_modalidad_delito = fv.id_modalidad_delito
                   AND s.id_grado_consumacion = fv.id_grado_consumacion
                   AND s.id_instrumento_comision = fv.id_instrumento_comision
                   AND s.id_forma_accion = fv.id_forma_accion
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
                LEFT JOIN dbo.catalogo_sabana_orden_legacy ol
                    ON ol.bien_juridico = bj.bien_juridico
                   AND ol.delito_sabana = s.delito_sabana
                   AND ol.subtipo_delito_sabana = s.subtipo_delito_sabana
                   AND ol.modalidad_delito_sabana = s.modalidad_delito_sabana
                   AND ol.activo = 1
                WHERE TRY_CONVERT(int, efh.clave) BETWEEN 1 AND 32
                GROUP BY
                    fv.anio_corte,
                    TRY_CONVERT(int, efh.clave),
                    efh.nombre,
                    TRY_CONVERT(int, CONCAT(
                        TRY_CONVERT(int, efh.clave),
                        RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                    )),
                    mun.nombre,
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana,
                    CASE
                        WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave = 1 THEN 'Hombre'
                        WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave = 2 THEN 'Mujer'
                        ELSE 'No identificado'
                    END,
                    CASE
                        WHEN fv.tipo_victima_clave <> 1 THEN 'No especificado'
                        WHEN fv.edad IS NULL THEN 'No especificado'
                        WHEN fv.edad BETWEEN 0 AND 12 THEN '0 a 12 años'
                        WHEN fv.edad BETWEEN 13 AND 17 THEN '13 a 17 años'
                        WHEN fv.edad BETWEEN 18 AND 29 THEN '18 a 29 años'
                        WHEN fv.edad BETWEEN 30 AND 60 THEN '30 a 60 años'
                        WHEN fv.edad BETWEEN 61 AND 120 THEN 'Más de 60 años'
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
            bloque_resultado,

            CASE WHEN bloque_resultado = 1 THEN clave_ent END,
            CASE WHEN bloque_resultado = 1 THEN clave_municipio_compuesta END,
            CASE WHEN bloque_resultado = 1 THEN orden_sabana END,

            CASE WHEN bloque_resultado = 2 THEN orden_sabana END,
            CASE WHEN bloque_resultado = 2 THEN clave_ent END,
            CASE WHEN bloque_resultado = 2 THEN clave_municipio_compuesta END,

            CASE sexo
                WHEN 'Hombre' THEN 1
                WHEN 'Mujer' THEN 2
                ELSE 3
            END,
            CASE rango_edad
                WHEN '0 a 12 años' THEN 1
                WHEN '13 a 17 años' THEN 2
                WHEN '18 a 29 años' THEN 3
                WHEN '30 a 60 años' THEN 4
                WHEN 'Más de 60 años' THEN 5
                ELSE 6
            END
        OPTION (RECOMPILE);";

        return await QueryDictionaryAnioAsync(sql, anioCorte, idEntidadFederativa, modoPlano, mesUltimoCorte);
    }

}
