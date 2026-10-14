using SIIID2.Api.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SIIID2.Api.Repositories;
// Movimientos de delitos; preservan la versión anterior antes de modificar o desactivar.
public partial class ActualizacionRepository : IActualizacionRepository
{
    private async Task InsertarHistoricoDelitosModificadosAsync(SqlConnection connection,
        SqlTransaction transaction,
        long idCargaActualizacion,
        int idUsuarioConfirmacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        delitos_actuales AS (
            SELECT
                d.*,
                ci.identificador_carpeta_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, d.id_carga DESC, d.id_delito DESC
                ) AS rn
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            WHERE d.activo = 1
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
                        WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) IS NOT NULL
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) >= 0
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) < 1
                        THEN DATEADD(
                            SECOND,
                            CONVERT(int, FLOOR(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) * 86400)),
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
        delitos_modificados AS (
            SELECT da.*
            FROM delitos_actuales da
            INNER JOIN delitos_tmp dt
                ON dt.id_ci = da.identificador_carpeta_fiscalia
               AND dt.id_delito = da.identificador_delito_fiscalia
            WHERE da.rn = 1
              AND (
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
                  )
        )
        INSERT INTO delito_historico (
            id_delito,
            id_carpeta_investigacion,
            identificador_delito_fiscalia,
            delito_fiscalia,
            modalidad_delito_fiscalia,
            id_forma_accion,
            fecha_hechos,
            id_instrumento_comision,
            id_grado_consumacion,
            id_modalidad_delito,
            id_entidad_federativa,
            id_municipio,
            id_localidad_fiscalia,
            localidad_fiscalia_nombre,
            id_colonia_fiscalia,
            colonia_fiscalia_nombre,
            id_codigo_postal,
            coordenada_x,
            coordenada_y,
            domicilio_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            id_usuario_modificacion,
            id_carga_nueva,
            tipo_movimiento,
            fecha_modificacion,
            activo
        )
        SELECT
            id_delito,
            id_carpeta_investigacion,
            identificador_delito_fiscalia,
            delito_fiscalia,
            modalidad_delito_fiscalia,
            id_forma_accion,
            fecha_hechos,
            id_instrumento_comision,
            id_grado_consumacion,
            id_modalidad_delito,
            id_entidad_federativa,
            id_municipio,
            id_localidad_fiscalia,
            localidad_fiscalia_nombre,
            id_colonia_fiscalia,
            colonia_fiscalia_nombre,
            id_codigo_postal,
            coordenada_x,
            coordenada_y,
            domicilio_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            @IdUsuarioConfirmacion,
            @IdCargaActualizacion,
            'MODIFICADO',
            SYSDATETIME(),
            activo
        FROM delitos_modificados;
    ";
        await connection.ExecuteAsync(sql, new { IdCargaActualizacion = idCargaActualizacion, IdUsuarioConfirmacion = idUsuarioConfirmacion }, transaction);
    }

    private async Task ActualizarDelitosModificadosAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        delitos_actuales AS (
            SELECT
                d.id_delito,
                ci.identificador_carpeta_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, d.id_carga DESC, d.id_delito DESC
                ) AS rn
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            WHERE d.activo = 1
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
                        WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) IS NOT NULL
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) >= 0
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) < 1
                        THEN DATEADD(
                            SECOND,
                            CONVERT(int, FLOOR(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) * 86400)),
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
        )
        UPDATE de
        SET de.delito_fiscalia = dt.dto,
            de.modalidad_delito_fiscalia = dt.moda_dto,
            de.id_forma_accion = dt.id_forma_accion,
            de.fecha_hechos = dt.fecha_hechos,
            de.id_instrumento_comision = dt.id_instrumento_comision,
            de.id_grado_consumacion = dt.id_grado_consumacion,
            de.id_modalidad_delito = dt.id_modalidad_delito,
            de.id_entidad_federativa = dt.id_entidad_federativa,
            de.id_municipio = dt.id_municipio,
            de.id_localidad_fiscalia = dt.id_loc_hchos,
            de.localidad_fiscalia_nombre = dt.nom_loc_hchos,
            de.id_colonia_fiscalia = dt.id_col_hchos,
            de.colonia_fiscalia_nombre = dt.nom_col_hchos,
            de.id_codigo_postal = dt.id_codigo_postal,
            de.coordenada_x = dt.coordenada_x,
            de.coordenada_y = dt.coordenada_y,
            de.domicilio_hechos = dt.dom_hchos,
            de.id_carga = @IdCargaActualizacion
        FROM delito de
        INNER JOIN delitos_actuales da
            ON da.id_delito = de.id_delito
           AND da.rn = 1
        INNER JOIN delitos_tmp dt
            ON dt.id_ci = da.identificador_carpeta_fiscalia
           AND dt.id_delito = de.identificador_delito_fiscalia
        WHERE de.activo = 1
          AND (
                ISNULL(de.delito_fiscalia, '') <> ISNULL(dt.dto, '')
                OR ISNULL(de.modalidad_delito_fiscalia, '') <> ISNULL(dt.moda_dto, '')
                OR ISNULL(de.id_forma_accion, 0) <> ISNULL(dt.id_forma_accion, 0)
                OR ISNULL(CONVERT(varchar(19), de.fecha_hechos, 120), '') <> ISNULL(CONVERT(varchar(19), dt.fecha_hechos, 120), '')
                OR ISNULL(de.id_instrumento_comision, 0) <> ISNULL(dt.id_instrumento_comision, 0)
                OR ISNULL(de.id_grado_consumacion, 0) <> ISNULL(dt.id_grado_consumacion, 0)
                OR ISNULL(de.id_modalidad_delito, 0) <> ISNULL(dt.id_modalidad_delito, 0)
                OR ISNULL(de.id_entidad_federativa, 0) <> ISNULL(dt.id_entidad_federativa, 0)
                OR ISNULL(de.id_municipio, 0) <> ISNULL(dt.id_municipio, 0)
                OR ISNULL(de.id_localidad_fiscalia, '') <> ISNULL(dt.id_loc_hchos, '')
                OR ISNULL(de.localidad_fiscalia_nombre, '') <> ISNULL(dt.nom_loc_hchos, '')
                OR ISNULL(de.id_colonia_fiscalia, '') <> ISNULL(dt.id_col_hchos, '')
                OR ISNULL(de.colonia_fiscalia_nombre, '') <> ISNULL(dt.nom_col_hchos, '')
                OR ISNULL(de.id_codigo_postal, 0) <> ISNULL(dt.id_codigo_postal, 0)
                OR ISNULL(de.coordenada_x, 0) <> ISNULL(dt.coordenada_x, 0)
                OR ISNULL(de.coordenada_y, 0) <> ISNULL(dt.coordenada_y, 0)
                OR ISNULL(de.domicilio_hechos, '') <> ISNULL(dt.dom_hchos, '')
              );
         ";
        await connection.ExecuteAsync(sql, new { IdCargaActualizacion = idCargaActualizacion }, transaction);
    }

    private async Task InsertarDelitosNuevosActualizacionAsync(SqlConnection connection,
        SqlTransaction transaction,
        long idCargaActualizacion,
        int idUsuarioConfirmacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1

            UNION

            SELECT @IdCargaActualizacion
        ),
        carpetas_vigentes AS (
            SELECT
                ci.id_carpeta_investigacion,
                ci.identificador_carpeta_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia
                    ORDER BY ci.id_carga DESC, ci.id_carpeta_investigacion DESC
                ) AS rn
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
        )
        INSERT INTO delito (
            id_carpeta_investigacion,
            identificador_delito_fiscalia,
            delito_fiscalia,
            modalidad_delito_fiscalia,
            id_forma_accion,
            fecha_hechos,
            id_instrumento_comision,
            id_grado_consumacion,
            id_modalidad_delito,
            id_entidad_federativa,
            id_municipio,
            id_localidad_fiscalia,
            localidad_fiscalia_nombre,
            id_colonia_fiscalia,
            colonia_fiscalia_nombre,
            id_codigo_postal,
            coordenada_x,
            coordenada_y,
            domicilio_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            activo
        )
        SELECT
            cv.id_carpeta_investigacion,
            d.id_delito,
            d.dto,
            d.moda_dto,
            fa.id_forma_accion,
            COALESCE(
                TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), '')), 103),
                TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''))),
                CASE
                    WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) IS NOT NULL
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) >= 0
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) < 1
                    THEN DATEADD(
                        SECOND,
                        CONVERT(int, FLOOR(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) * 86400)),
                        COALESCE(
                            TRY_CONVERT(datetime2, d.fha_de_hchos, 103),
                            TRY_CONVERT(datetime2, d.fha_de_hchos)
                        )
                    )
                END,
                TRY_CONVERT(datetime2, d.fha_de_hchos, 103),
                TRY_CONVERT(datetime2, d.fha_de_hchos)
            ),
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
            TRY_CONVERT(decimal(10,6), NULLIF(d.coord_x, '')),
            TRY_CONVERT(decimal(10,6), NULLIF(d.coord_y, '')),
            d.dom_hchos,
            @IdUsuarioConfirmacion,
            SYSDATETIME(),
            @IdCargaActualizacion,
            1
        FROM carga_tmp_delito d
        INNER JOIN carpetas_vigentes cv
            ON cv.identificador_carpeta_fiscalia = d.id_ci
           AND cv.rn = 1
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
          AND NOT EXISTS (
                SELECT 1
                FROM delito de
                WHERE de.id_carpeta_investigacion = cv.id_carpeta_investigacion
                  AND de.identificador_delito_fiscalia = d.id_delito
                  AND de.activo = 1
          );
    ";
        await connection.ExecuteAsync(sql, new { IdCargaActualizacion = idCargaActualizacion, IdUsuarioConfirmacion = idUsuarioConfirmacion }, transaction);
    }

    private async Task InsertarHistoricoDelitosEliminadosAsync(SqlConnection connection,
        SqlTransaction transaction,
        long idCargaActualizacion,
        int idUsuarioConfirmacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        delitos_actuales AS (
            SELECT
                d.*,
                ci.identificador_carpeta_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, d.id_carga DESC, d.id_delito DESC
                ) AS rn
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            WHERE d.activo = 1
        ),
        delitos_eliminados AS (
            SELECT da.*
            FROM delitos_actuales da
            LEFT JOIN carga_tmp_delito dt
                ON dt.id_ci = da.identificador_carpeta_fiscalia
               AND dt.id_delito = da.identificador_delito_fiscalia
               AND dt.id_carga = @IdCargaActualizacion
               AND dt.activo = 1
            WHERE da.rn = 1
              AND dt.id_delito IS NULL
        )
        INSERT INTO delito_historico (
            id_delito,
            id_carpeta_investigacion,
            identificador_delito_fiscalia,
            delito_fiscalia,
            modalidad_delito_fiscalia,
            id_forma_accion,
            fecha_hechos,
            id_instrumento_comision,
            id_grado_consumacion,
            id_modalidad_delito,
            id_entidad_federativa,
            id_municipio,
            id_localidad_fiscalia,
            localidad_fiscalia_nombre,
            id_colonia_fiscalia,
            colonia_fiscalia_nombre,
            id_codigo_postal,
            coordenada_x,
            coordenada_y,
            domicilio_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            id_usuario_modificacion,
            id_carga_nueva,
            tipo_movimiento,
            fecha_modificacion,
            activo
        )
        SELECT
            id_delito,
            id_carpeta_investigacion,
            identificador_delito_fiscalia,
            delito_fiscalia,
            modalidad_delito_fiscalia,
            id_forma_accion,
            fecha_hechos,
            id_instrumento_comision,
            id_grado_consumacion,
            id_modalidad_delito,
            id_entidad_federativa,
            id_municipio,
            id_localidad_fiscalia,
            localidad_fiscalia_nombre,
            id_colonia_fiscalia,
            colonia_fiscalia_nombre,
            id_codigo_postal,
            coordenada_x,
            coordenada_y,
            domicilio_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            @IdUsuarioConfirmacion,
            @IdCargaActualizacion,
            'ELIMINADO',
            SYSDATETIME(),
            activo
        FROM delitos_eliminados;
    ";
        await connection.ExecuteAsync(sql, new { IdCargaActualizacion = idCargaActualizacion, IdUsuarioConfirmacion = idUsuarioConfirmacion }, transaction);
    }

    private async Task DesactivarDelitosEliminadosAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
    {
        var sql = @"
        ;WITH carga_actualizacion AS (
            SELECT id_carga, id_entidad_federativa, mes_corte, anio_corte
            FROM carga
            WHERE id_carga = @IdCargaActualizacion
        ),
        cargas_periodo AS (
            SELECT c.id_carga, c.fecha_confirmacion
            FROM carga c
            INNER JOIN carga_actualizacion ca
                ON ca.id_entidad_federativa = c.id_entidad_federativa
               AND ca.mes_corte = c.mes_corte
               AND ca.anio_corte = c.anio_corte
            WHERE c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
              AND c.activo = 1
        ),
        delitos_actuales AS (
            SELECT
                d.id_delito,
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, d.id_carga DESC, d.id_delito DESC
                ) AS rn
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            WHERE d.activo = 1
        )
        UPDATE de
        SET de.activo = 0,
            de.id_carga = @IdCargaActualizacion
        FROM delito de
        INNER JOIN delitos_actuales da
            ON da.id_delito = de.id_delito
           AND da.rn = 1
        LEFT JOIN carga_tmp_delito dt
            ON dt.id_ci = da.identificador_carpeta_fiscalia
           AND dt.id_delito = da.identificador_delito_fiscalia
           AND dt.id_carga = @IdCargaActualizacion
           AND dt.activo = 1
        WHERE de.activo = 1
          AND dt.id_delito IS NULL;
    ";
        await connection.ExecuteAsync(sql, new { IdCargaActualizacion = idCargaActualizacion }, transaction);
    }
}
