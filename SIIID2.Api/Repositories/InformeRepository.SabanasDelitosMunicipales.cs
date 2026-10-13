using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

// Sábana municipal de delitos; no mezclar el grano municipal con el estatal.
public partial class InformeRepository : IInformeRepository
{
    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalDelitosAsync(int anioCorte, int? idEntidadFederativa, string modoPlano, int mesUltimoCorte)
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
        fuente_delitos AS (
            SELECT
                c.anio_corte,
                c.mes_corte,
                c.id_entidad_federativa AS id_entidad_carga,
                d.id_entidad_federativa AS id_entidad_hechos,
                d.id_municipio,
                d.id_modalidad_delito,
                d.id_grado_consumacion,
                d.id_instrumento_comision,
                d.id_forma_accion
            FROM delito d
            INNER JOIN carga c
                ON c.id_carga = d.id_carga
               AND c.activo = 1
               AND c.anio_corte = @AnioCorte
               AND (
                      (c.tipo_carga = 'CARGA_INICIAL' AND c.estado = 'CONFIRMADO')
                   OR (c.tipo_carga = 'ACTUALIZACION' AND c.estado = 'CONFIRMADO_ACTUALIZACION')
               )
            WHERE d.activo = 1
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
                fa.id_forma_accion
            FROM pendientes p
            INNER JOIN carga_tmp_delito d
                ON d.id_carga = p.id_carga
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
            WHERE @ModoPlano IN ('PREVIO', 'MIXTO')
        ),
        sabana AS (
            SELECT
                MIN(COALESCE(ol.orden_general, s.id_delito_sabana)) AS orden_sabana,
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
        matriz AS (
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
                s.modalidad_delito_sabana
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
                fd.anio_corte,
                fd.mes_corte,
                TRY_CONVERT(int, efh.clave) AS clave_ent,
                TRY_CONVERT(int, CONCAT(
                    TRY_CONVERT(int, efh.clave),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                )) AS clave_municipio_compuesta,
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana,
                COUNT(1) AS cantidad_delitos
            FROM fuente_delitos fd
            INNER JOIN catalogo_entidad_federativa efh
                ON efh.id_entidad_federativa = fd.id_entidad_hechos
               AND efh.activo = 1
            INNER JOIN catalogo_municipio mun
                ON mun.id_municipio = fd.id_municipio
               AND mun.activo = 1
            INNER JOIN catalogo_delito_sabana s
                ON s.id_modalidad_delito = fd.id_modalidad_delito
               AND s.id_grado_consumacion = fd.id_grado_consumacion
               AND s.id_instrumento_comision = fd.id_instrumento_comision
               AND s.id_forma_accion = fd.id_forma_accion
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
            WHERE TRY_CONVERT(int, efh.clave) BETWEEN 1 AND 32
            GROUP BY
                fd.anio_corte,
                fd.mes_corte,
                TRY_CONVERT(int, efh.clave),
                TRY_CONVERT(int, CONCAT(
                    TRY_CONVERT(int, efh.clave),
                    RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                )),
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
            m.orden_sabana,
            m.orden_delito,
            m.subtipo_delito_sabana,
            m.modalidad_delito_sabana,
            m.clave_municipio_compuesta
        OPTION (RECOMPILE);
        ";

        return await QueryDictionaryAnioAsync(sql, anioCorte, idEntidadFederativa, modoPlano, mesUltimoCorte);
    }

}
