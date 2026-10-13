using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Globalization;

namespace SIIID2.Api.Repositories;

// Agregación estatal de delitos semanales; respeta el corte y los bloques originales.
public partial class SemanalEnviosRepository : ISemanalEnviosRepository
{
    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaEstatalDelitosAsync(int anioCorte, int? idEntidadFederativa, int? idUsuarioCarga, string modoPlano, int mesUltimoCorte)
    {
        var sql = @"
            WITH periodos_carga AS
            (
                SELECT
                    sc.id_semanal_carga,
                    sc.id_entidad_federativa,
                    sc.id_usuario_carga,
                    bloque.anio_corte,
                    bloque.mes_corte,
                    sc.fecha_validacion
                FROM dbo.semanal_carga sc
                INNER JOIN dbo.semanal_carga_bloque bloque
                    ON bloque.id_semanal_carga = sc.id_semanal_carga
                   AND bloque.activo = 1
                WHERE sc.activo = 1
                  AND sc.estado = N'PENDIENTE_APROBACION'

                UNION ALL

                SELECT
                    sc.id_semanal_carga,
                    sc.id_entidad_federativa,
                    sc.id_usuario_carga,
                    sc.anio_corte,
                    sc.mes_corte,
                    sc.fecha_validacion
                FROM dbo.semanal_carga sc
                WHERE sc.activo = 1
                  AND sc.estado = N'PENDIENTE_APROBACION'
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.semanal_carga_bloque bloque
                      WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                        AND bloque.activo = 1
                  )
            ),
            pendientes_rankeadas AS
            (
                SELECT
                    periodo.id_semanal_carga,
                    periodo.id_entidad_federativa,
                    periodo.id_usuario_carga,
                    periodo.anio_corte,
                    periodo.mes_corte,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY
                            periodo.id_entidad_federativa,
                            periodo.id_usuario_carga,
                            periodo.anio_corte,
                            periodo.mes_corte
                        ORDER BY
                            periodo.fecha_validacion DESC,
                            periodo.id_semanal_carga DESC
                    ) AS rn
                FROM periodos_carga periodo
                WHERE periodo.anio_corte = @AnioCorte
                  AND (@IdEntidadFederativa IS NULL OR periodo.id_entidad_federativa = @IdEntidadFederativa)
                  AND (@IdUsuarioCarga IS NULL OR periodo.id_usuario_carga = @IdUsuarioCarga)
            ),
            pendientes AS
            (
                SELECT
                    id_semanal_carga,
                    id_entidad_federativa,
                    id_usuario_carga,
                    anio_corte,
                    mes_corte
                FROM pendientes_rankeadas
                WHERE rn = 1
            ),
            fuente_delitos AS
            (
                SELECT
                    YEAR(ci.fecha_inicio) AS anio_corte,
                    MONTH(ci.fecha_inicio) AS mes_corte,
                    sc.id_entidad_federativa AS id_entidad_carga,
                    sc.id_usuario_carga,
                    d.id_entidad_federativa AS id_entidad_hechos,
                    d.id_municipio,
                    d.id_modalidad_delito,
                    d.id_grado_consumacion,
                    d.id_instrumento_comision,
                    d.id_forma_accion
                FROM dbo.semanal_delito d
                INNER JOIN dbo.semanal_carpeta_investigacion ci
                    ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
                   AND ci.activo = 1
                INNER JOIN dbo.semanal_carga sc
                    ON sc.id_semanal_carga = d.id_semanal_carga
                   AND sc.activo = 1
                   AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
                   AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                WHERE d.activo = 1
                  AND YEAR(ci.fecha_inicio) = @AnioCorte
                  AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
                  AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
                  AND
                  (
                      @ModoPlano = N'CONFIRMADO'
                      OR
                      (
                          @ModoPlano = N'PREVIO'
                          AND NOT EXISTS
                          (
                              SELECT 1
                              FROM pendientes p
                              WHERE p.anio_corte = YEAR(ci.fecha_inicio)
                                AND p.mes_corte = MONTH(ci.fecha_inicio)
                          )
                      )
                      OR
                      (
                          @ModoPlano = N'MIXTO'
                          AND NOT EXISTS
                          (
                              SELECT 1
                              FROM pendientes p
                              WHERE p.id_entidad_federativa = sc.id_entidad_federativa
                                AND p.id_usuario_carga = sc.id_usuario_carga
                                AND p.anio_corte = YEAR(ci.fecha_inicio)
                                AND p.mes_corte = MONTH(ci.fecha_inicio)
                          )
                      )
                  )

                UNION ALL

                SELECT
                    p.anio_corte,
                    p.mes_corte,
                    p.id_entidad_federativa,
                    p.id_usuario_carga,
                    ef.id_entidad_federativa,
                    mun.id_municipio,
                    md.id_modalidad_delito,
                    gc.id_grado_consumacion,
                    ic.id_instrumento_comision,
                    fa.id_forma_accion
                FROM pendientes p
                INNER JOIN dbo.semanal_carga_tmp_carpeta carpeta
                    ON carpeta.id_semanal_carga = p.id_semanal_carga
                   AND carpeta.incluido = 1
                   AND carpeta.activo = 1
                INNER JOIN dbo.semanal_carga_tmp_delito d
                    ON d.id_semanal_carga = carpeta.id_semanal_carga
                   AND d.id_ci = carpeta.id_ci
                   AND d.incluido = 1
                   AND d.activo = 1
                CROSS APPLY
                (
                    SELECT COALESCE
                    (
                        TRY_CONVERT(date, NULLIF(LTRIM(RTRIM(carpeta.fha_de_ini)), N''), 103),
                        TRY_CONVERT(date, NULLIF(LTRIM(RTRIM(carpeta.fha_de_ini)), N''))
                    ) AS fecha_inicio
                ) fecha
                INNER JOIN dbo.catalogo_modalidad_delito md
                    ON LTRIM(RTRIM(md.clave4)) = LTRIM(RTRIM(d.clasf_de_dto))
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
                  AND fecha.fecha_inicio IS NOT NULL
                  AND YEAR(fecha.fecha_inicio) = p.anio_corte
                  AND MONTH(fecha.fecha_inicio) = p.mes_corte
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
              AND (@IdEntidadFederativa IS NULL OR ef.id_entidad_federativa = @IdEntidadFederativa)
        ),
        conteos AS (
            SELECT
                fd.anio_corte,
                fd.mes_corte,
                TRY_CONVERT(int, efh.clave) AS clave_ent,
                bj.bien_juridico,
                s.delito_sabana,
                s.subtipo_delito_sabana,
                s.modalidad_delito_sabana,
                COUNT(1) AS cantidad_delitos
            FROM fuente_delitos fd
            INNER JOIN catalogo_entidad_federativa efh
                ON efh.id_entidad_federativa = fd.id_entidad_hechos
               AND efh.activo = 1
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
            m.orden_sabana,
            m.orden_delito,
            m.subtipo_delito_sabana,
            m.modalidad_delito_sabana
        OPTION (RECOMPILE);
        ";

        return await QueryDictionarySabanaAsync(sql, anioCorte, idEntidadFederativa, idUsuarioCarga, modoPlano, mesUltimoCorte);
    }

}
