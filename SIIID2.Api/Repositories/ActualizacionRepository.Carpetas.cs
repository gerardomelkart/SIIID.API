using SIIID2.Api.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SIIID2.Api.Repositories;
// Movimientos de carpetas dentro de la transacción recibida del coordinador.
public partial class ActualizacionRepository : IActualizacionRepository
{
    private async Task InsertarHistoricoCarpetasModificadasAsync(SqlConnection connection,
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
        carpetas_actuales AS (
            SELECT
                ci.*,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, ci.id_carga DESC, ci.id_carpeta_investigacion DESC
                ) AS rn
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
        ),
        carpetas_modificadas AS (
            SELECT ca.*
            FROM carpetas_actuales ca
            INNER JOIN carga_tmp_carpeta ct
                ON ct.id_ci = ca.identificador_carpeta_fiscalia
               AND ct.id_carga = @IdCargaActualizacion
               AND ct.activo = 1
            WHERE ca.rn = 1
              AND (
                    ISNULL(ca.nomenclatura_carpeta_fiscalia, '') <> ISNULL(ct.ntra_ci, '')
                    OR ISNULL(CONVERT(varchar(19), ca.fecha_inicio, 120), '') <> ISNULL(CONVERT(varchar(19), COALESCE(
                    TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), '')), 103),
                    TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''))),
                    CASE
                        WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) IS NOT NULL
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) >= 0
                             AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) < 1
                        THEN DATEADD(
                            SECOND,
                            CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) * 86400, 0)),
                            COALESCE(
                                TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                                TRY_CONVERT(datetime2, ct.fha_de_ini)
                            )
                        )
                    END,
                    TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                    TRY_CONVERT(datetime2, ct.fha_de_ini)
                ), 120), '')
                    OR ISNULL(ca.resumen_hechos, '') <> ISNULL(ct.rmen_de_hchos, '')
                  )
        )
        INSERT INTO carpeta_investigacion_historico (
            id_carpeta_investigacion,
            identificador_carpeta_fiscalia,
            nomenclatura_carpeta_fiscalia,
            fecha_inicio,
            resumen_hechos,
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
            id_carpeta_investigacion,
            identificador_carpeta_fiscalia,
            nomenclatura_carpeta_fiscalia,
            fecha_inicio,
            resumen_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            @IdUsuarioConfirmacion,
            @IdCargaActualizacion,
            'MODIFICADO',
            SYSDATETIME(),
            activo
        FROM carpetas_modificadas;
    ";
        await connection.ExecuteAsync(sql, new { IdCargaActualizacion = idCargaActualizacion, IdUsuarioConfirmacion = idUsuarioConfirmacion }, transaction);
    }

    private async Task ActualizarCarpetasModificadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
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
        carpetas_actuales AS (
            SELECT
                ci.id_carpeta_investigacion,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, ci.id_carga DESC, ci.id_carpeta_investigacion DESC
                ) AS rn
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
        )
        UPDATE ci
        SET ci.nomenclatura_carpeta_fiscalia = ct.ntra_ci,
            ci.fecha_inicio = COALESCE(
                TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), '')), 103),
                TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''))),
                CASE
                    WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) IS NOT NULL
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) >= 0
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) < 1
                    THEN DATEADD(
                        SECOND,
                        CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) * 86400, 0)),
                        COALESCE(
                            TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                            TRY_CONVERT(datetime2, ct.fha_de_ini)
                        )
                    )
                END,
                TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                TRY_CONVERT(datetime2, ct.fha_de_ini)
            ),
            ci.resumen_hechos = ct.rmen_de_hchos,
            ci.id_carga = @IdCargaActualizacion
        FROM carpeta_investigacion ci
        INNER JOIN carpetas_actuales ca
            ON ca.id_carpeta_investigacion = ci.id_carpeta_investigacion
           AND ca.rn = 1
        INNER JOIN carga_tmp_carpeta ct
            ON ct.id_ci = ci.identificador_carpeta_fiscalia
           AND ct.id_carga = @IdCargaActualizacion
           AND ct.activo = 1
        WHERE ci.activo = 1
          AND (
                ISNULL(ci.nomenclatura_carpeta_fiscalia, '') <> ISNULL(ct.ntra_ci, '')
                OR ISNULL(CONVERT(varchar(19), ci.fecha_inicio, 120), '') <> ISNULL(CONVERT(varchar(19), COALESCE(
                TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), '')), 103),
                TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''))),
                CASE
                    WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) IS NOT NULL
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) >= 0
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) < 1
                    THEN DATEADD(
                        SECOND,
                        CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) * 86400, 0)),
                        COALESCE(
                            TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                            TRY_CONVERT(datetime2, ct.fha_de_ini)
                        )
                    )
                END,
                TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                TRY_CONVERT(datetime2, ct.fha_de_ini)
            ), 120), '')
                OR ISNULL(ci.resumen_hechos, '') <> ISNULL(ct.rmen_de_hchos, '')
              );
    ";
        await connection.ExecuteAsync(sql, new { IdCargaActualizacion = idCargaActualizacion }, transaction);
    }

    private async Task InsertarCarpetasNuevasActualizacionAsync(SqlConnection connection,
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
        )
        INSERT INTO carpeta_investigacion (
            identificador_carpeta_fiscalia,
            nomenclatura_carpeta_fiscalia,
            fecha_inicio,
            resumen_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            activo
        )
        SELECT
            ct.id_ci,
            ct.ntra_ci,
            COALESCE(
                TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), '')), 103),
                TRY_CONVERT(datetime2, CONCAT(ct.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''))),
                CASE
                    WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) IS NOT NULL
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) >= 0
                         AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) < 1
                    THEN DATEADD(
                        SECOND,
                        CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(ct.hra_de_ini)), ''), ',', '.')) * 86400, 0)),
                        COALESCE(
                            TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                            TRY_CONVERT(datetime2, ct.fha_de_ini)
                        )
                    )
                END,
                TRY_CONVERT(datetime2, ct.fha_de_ini, 103),
                TRY_CONVERT(datetime2, ct.fha_de_ini)
            ),
            ct.rmen_de_hchos,
            @IdUsuarioConfirmacion,
            SYSDATETIME(),
            @IdCargaActualizacion,
            1
        FROM carga_tmp_carpeta ct
        WHERE ct.id_carga = @IdCargaActualizacion
          AND ct.activo = 1
          AND NOT EXISTS (
              SELECT 1
              FROM carpeta_investigacion ci
              LEFT JOIN cargas_periodo cp
                  ON cp.id_carga = ci.id_carga
              WHERE ci.identificador_carpeta_fiscalia = ct.id_ci
                AND ci.activo = 1
                AND (
                      cp.id_carga IS NOT NULL
                      OR ci.id_carga = @IdCargaActualizacion
                    )
           );
    ";
        await connection.ExecuteAsync(sql, new { IdCargaActualizacion = idCargaActualizacion, IdUsuarioConfirmacion = idUsuarioConfirmacion }, transaction);
    }

    private async Task InsertarHistoricoCarpetasEliminadasAsync(SqlConnection connection,
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
        carpetas_actuales AS (
            SELECT
                ci.*,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, ci.id_carga DESC, ci.id_carpeta_investigacion DESC
                ) AS rn
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
        ),
        carpetas_eliminadas AS (
            SELECT ca.*
            FROM carpetas_actuales ca
            LEFT JOIN carga_tmp_carpeta ct
                ON ct.id_ci = ca.identificador_carpeta_fiscalia
               AND ct.id_carga = @IdCargaActualizacion
               AND ct.activo = 1
            WHERE ca.rn = 1
              AND ct.id_ci IS NULL
        )
        INSERT INTO carpeta_investigacion_historico (
            id_carpeta_investigacion,
            identificador_carpeta_fiscalia,
            nomenclatura_carpeta_fiscalia,
            fecha_inicio,
            resumen_hechos,
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
            id_carpeta_investigacion,
            identificador_carpeta_fiscalia,
            nomenclatura_carpeta_fiscalia,
            fecha_inicio,
            resumen_hechos,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            @IdUsuarioConfirmacion,
            @IdCargaActualizacion,
            'ELIMINADO',
            SYSDATETIME(),
            activo
        FROM carpetas_eliminadas;
    ";
        await connection.ExecuteAsync(sql, new { IdCargaActualizacion = idCargaActualizacion, IdUsuarioConfirmacion = idUsuarioConfirmacion }, transaction);
    }

    private async Task DesactivarCarpetasEliminadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
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
        carpetas_actuales AS (
            SELECT
                ci.id_carpeta_investigacion,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, ci.id_carga DESC, ci.id_carpeta_investigacion DESC
                ) AS rn
            FROM carpeta_investigacion ci
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = ci.id_carga
            WHERE ci.activo = 1
        )
        UPDATE ci
        SET ci.activo = 0,
            ci.id_carga = @IdCargaActualizacion
        FROM carpeta_investigacion ci
        INNER JOIN carpetas_actuales ca
            ON ca.id_carpeta_investigacion = ci.id_carpeta_investigacion
           AND ca.rn = 1
        LEFT JOIN carga_tmp_carpeta ct
            ON ct.id_ci = ci.identificador_carpeta_fiscalia
           AND ct.id_carga = @IdCargaActualizacion
           AND ct.activo = 1
        WHERE ci.activo = 1
          AND ct.id_ci IS NULL;
    ";
        await connection.ExecuteAsync(sql, new { IdCargaActualizacion = idCargaActualizacion }, transaction);
    }
}
