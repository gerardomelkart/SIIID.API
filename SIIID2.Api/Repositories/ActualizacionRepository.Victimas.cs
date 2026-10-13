using SIIID2.Api.Services;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SIIID2.Api.Repositories;

// Movimientos de víctimas; todas las escrituras utilizan la transacción del coordinador.
public partial class ActualizacionRepository : IActualizacionRepository
{
    private async Task InsertarHistoricoVictimasModificadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
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
        victimas_actuales AS (
            SELECT
                v.*,
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia, v.identificador_victima_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, v.id_carga DESC, v.id_victima DESC
                ) AS rn
            FROM victima v
            INNER JOIN delito d
                ON d.id_delito = v.id_delito
               AND d.activo = 1
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = v.id_carga
            WHERE v.activo = 1
        ),
        victimas_tmp AS (
            SELECT
                v.id_ci,
                v.id_delito,
                v.id_vicf,
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
                TRY_CONVERT(smallint, NULLIF(v.edad, '')) AS edad,
                NULLIF(v.nombre_vicfem, '') AS nombre_vicfem,
                NULLIF(v.primer_apellido_vicfem, '') AS primer_apellido_vicfem,
                NULLIF(v.segundo_apellido_vicfem, '') AS segundo_apellido_vicfem,
                NULLIF(v.curp_vicfem, '') AS curp_vicfem
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
            WHERE v.id_carga = @IdCargaActualizacion
              AND v.activo = 1
        ),
        victimas_modificadas AS (
            SELECT va.*
            FROM victimas_actuales va
            INNER JOIN victimas_tmp vt
                ON vt.id_ci = va.identificador_carpeta_fiscalia
               AND vt.id_delito = va.identificador_delito_fiscalia
               AND vt.id_vicf = va.identificador_victima_fiscalia
            WHERE va.rn = 1
              AND (
                    ISNULL(va.id_tipo_victima, 0) <> ISNULL(vt.id_tipo_victima, 0)
                    OR ISNULL(va.id_tipo_victima_moral, 0) <> ISNULL(vt.id_tipo_victima_moral, 0)
                    OR ISNULL(va.id_sexo, 0) <> ISNULL(vt.id_sexo, 0)
                    OR ISNULL(va.id_genero, 0) <> ISNULL(vt.id_genero, 0)
                    OR ISNULL(va.id_nacionalidad, 0) <> ISNULL(vt.id_nacionalidad, 0)
                    OR ISNULL(va.id_pertenece_poblacion_indigena, 0) <> ISNULL(vt.id_pertenece_poblacion_indigena, 0)
                    OR ISNULL(va.id_presenta_discapacidad, 0) <> ISNULL(vt.id_presenta_discapacidad, 0)
                    OR ISNULL(CONVERT(varchar(10), va.fecha_nacimiento, 120), '') <> ISNULL(CONVERT(varchar(10), vt.fecha_nacimiento, 120), '')
                    OR ISNULL(va.edad, 0) <> ISNULL(vt.edad, 0)
                    OR ISNULL(va.nombre_vicfem, '') <> ISNULL(vt.nombre_vicfem, '')
                    OR ISNULL(va.primer_apellido_vicfem, '') <> ISNULL(vt.primer_apellido_vicfem, '')
                    OR ISNULL(va.segundo_apellido_vicfem, '') <> ISNULL(vt.segundo_apellido_vicfem, '')
                    OR ISNULL(va.curp_vicfem, '') <> ISNULL(vt.curp_vicfem, '')
                  )
        )
        INSERT INTO victima_historico (
            id_victima,
            id_delito,
            identificador_victima_fiscalia,
            id_tipo_victima,
            id_tipo_victima_moral,
            id_sexo,
            id_genero,
            id_nacionalidad,
            id_pertenece_poblacion_indigena,
            id_presenta_discapacidad,
            fecha_nacimiento,
            edad,
            nombre_vicfem,
            primer_apellido_vicfem,
            segundo_apellido_vicfem,
            curp_vicfem,
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
            id_victima,
            id_delito,
            identificador_victima_fiscalia,
            id_tipo_victima,
            id_tipo_victima_moral,
            id_sexo,
            id_genero,
            id_nacionalidad,
            id_pertenece_poblacion_indigena,
            id_presenta_discapacidad,
            fecha_nacimiento,
            edad,
            nombre_vicfem,
            primer_apellido_vicfem,
            segundo_apellido_vicfem,
            curp_vicfem,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            @IdUsuarioConfirmacion,
            @IdCargaActualizacion,
            'MODIFICADO',
            SYSDATETIME(),
            activo
        FROM victimas_modificadas;
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion,
            IdUsuarioConfirmacion = idUsuarioConfirmacion
        }, transaction);
    }

    private async Task ActualizarVictimasModificadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
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
        victimas_actuales AS (
            SELECT
                v.id_victima,
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia, v.identificador_victima_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, v.id_carga DESC, v.id_victima DESC
                ) AS rn
            FROM victima v
            INNER JOIN delito d
                ON d.id_delito = v.id_delito
               AND d.activo = 1
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = v.id_carga
            WHERE v.activo = 1
        ),
        victimas_tmp AS (
            SELECT
                v.id_ci,
                v.id_delito,
                v.id_vicf,
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
                TRY_CONVERT(smallint, NULLIF(v.edad, '')) AS edad,
                NULLIF(v.nombre_vicfem, '') AS nombre_vicfem,
                NULLIF(v.primer_apellido_vicfem, '') AS primer_apellido_vicfem,
                NULLIF(v.segundo_apellido_vicfem, '') AS segundo_apellido_vicfem,
                NULLIF(v.curp_vicfem, '') AS curp_vicfem
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
            WHERE v.id_carga = @IdCargaActualizacion
              AND v.activo = 1
        )
        UPDATE vi
        SET vi.id_tipo_victima = vt.id_tipo_victima,
            vi.id_tipo_victima_moral = vt.id_tipo_victima_moral,
            vi.id_sexo = vt.id_sexo,
            vi.id_genero = vt.id_genero,
            vi.id_nacionalidad = vt.id_nacionalidad,
            vi.id_pertenece_poblacion_indigena = vt.id_pertenece_poblacion_indigena,
            vi.id_presenta_discapacidad = vt.id_presenta_discapacidad,
            vi.fecha_nacimiento = vt.fecha_nacimiento,
            vi.edad = vt.edad,
            vi.nombre_vicfem = vt.nombre_vicfem,
            vi.primer_apellido_vicfem = vt.primer_apellido_vicfem,
            vi.segundo_apellido_vicfem = vt.segundo_apellido_vicfem,
            vi.curp_vicfem = vt.curp_vicfem,
            vi.id_carga = @IdCargaActualizacion
        FROM victima vi
        INNER JOIN victimas_actuales va
            ON va.id_victima = vi.id_victima
           AND va.rn = 1
        INNER JOIN victimas_tmp vt
            ON vt.id_ci = va.identificador_carpeta_fiscalia
           AND vt.id_delito = va.identificador_delito_fiscalia
           AND vt.id_vicf = vi.identificador_victima_fiscalia
        WHERE vi.activo = 1
          AND (
                ISNULL(vi.id_tipo_victima, 0) <> ISNULL(vt.id_tipo_victima, 0)
                OR ISNULL(vi.id_tipo_victima_moral, 0) <> ISNULL(vt.id_tipo_victima_moral, 0)
                OR ISNULL(vi.id_sexo, 0) <> ISNULL(vt.id_sexo, 0)
                OR ISNULL(vi.id_genero, 0) <> ISNULL(vt.id_genero, 0)
                OR ISNULL(vi.id_nacionalidad, 0) <> ISNULL(vt.id_nacionalidad, 0)
                OR ISNULL(vi.id_pertenece_poblacion_indigena, 0) <> ISNULL(vt.id_pertenece_poblacion_indigena, 0)
                OR ISNULL(vi.id_presenta_discapacidad, 0) <> ISNULL(vt.id_presenta_discapacidad, 0)
                OR ISNULL(CONVERT(varchar(10), vi.fecha_nacimiento, 120), '') <> ISNULL(CONVERT(varchar(10), vt.fecha_nacimiento, 120), '')
                OR ISNULL(vi.edad, 0) <> ISNULL(vt.edad, 0)
                OR ISNULL(vi.nombre_vicfem, '') <> ISNULL(vt.nombre_vicfem, '')
                OR ISNULL(vi.primer_apellido_vicfem, '') <> ISNULL(vt.primer_apellido_vicfem, '')
                OR ISNULL(vi.segundo_apellido_vicfem, '') <> ISNULL(vt.segundo_apellido_vicfem, '')
                OR ISNULL(vi.curp_vicfem, '') <> ISNULL(vt.curp_vicfem, '')
              );
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion
        }, transaction);
    }

    private async Task InsertarVictimasNuevasActualizacionAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
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
        delitos_vigentes AS (
            SELECT
                d.id_delito,
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                    ORDER BY d.id_carga DESC, d.id_delito DESC
                ) AS rn
            FROM delito d
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = d.id_carga
            WHERE d.activo = 1
        )
        INSERT INTO victima (
            id_delito,
            identificador_victima_fiscalia,
            id_tipo_victima,
            id_tipo_victima_moral,
            id_sexo,
            id_genero,
            id_nacionalidad,
            id_pertenece_poblacion_indigena,
            id_presenta_discapacidad,
            fecha_nacimiento,
            edad,
            nombre_vicfem,
            primer_apellido_vicfem,
            segundo_apellido_vicfem,
            curp_vicfem,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            activo
        )
        SELECT
            dv.id_delito,
            v.id_vicf,
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
            ),
            TRY_CONVERT(smallint, NULLIF(v.edad, '')),
            NULLIF(v.nombre_vicfem, ''),
            NULLIF(v.primer_apellido_vicfem, ''),
            NULLIF(v.segundo_apellido_vicfem, ''),
            NULLIF(v.curp_vicfem, ''),
            @IdUsuarioConfirmacion,
            SYSDATETIME(),
            @IdCargaActualizacion,
            1
        FROM carga_tmp_victima v
        INNER JOIN delitos_vigentes dv
            ON dv.identificador_carpeta_fiscalia = v.id_ci
           AND dv.identificador_delito_fiscalia = v.id_delito
           AND dv.rn = 1
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
        WHERE v.id_carga = @IdCargaActualizacion
          AND v.activo = 1
          AND NOT EXISTS (
                SELECT 1
                FROM victima vi
                WHERE vi.id_delito = dv.id_delito
                  AND vi.identificador_victima_fiscalia = v.id_vicf
                  AND vi.activo = 1
          );
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion,
            IdUsuarioConfirmacion = idUsuarioConfirmacion
        }, transaction);
    }

    private async Task InsertarHistoricoVictimasEliminadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion, int idUsuarioConfirmacion)
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
        victimas_actuales AS (
            SELECT
                v.*,
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia, v.identificador_victima_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, v.id_carga DESC, v.id_victima DESC
                ) AS rn
            FROM victima v
            INNER JOIN delito d
                ON d.id_delito = v.id_delito
               AND d.activo = 1
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = v.id_carga
            WHERE v.activo = 1
        ),
        victimas_eliminadas AS (
            SELECT va.*
            FROM victimas_actuales va
            LEFT JOIN carga_tmp_victima vt
                ON vt.id_ci = va.identificador_carpeta_fiscalia
               AND vt.id_delito = va.identificador_delito_fiscalia
               AND vt.id_vicf = va.identificador_victima_fiscalia
               AND vt.id_carga = @IdCargaActualizacion
               AND vt.activo = 1
            WHERE va.rn = 1
              AND vt.id_vicf IS NULL
        )
        INSERT INTO victima_historico (
            id_victima,
            id_delito,
            identificador_victima_fiscalia,
            id_tipo_victima,
            id_tipo_victima_moral,
            id_sexo,
            id_genero,
            id_nacionalidad,
            id_pertenece_poblacion_indigena,
            id_presenta_discapacidad,
            fecha_nacimiento,
            edad,
            nombre_vicfem,
            primer_apellido_vicfem,
            segundo_apellido_vicfem,
            curp_vicfem,
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
            id_victima,
            id_delito,
            identificador_victima_fiscalia,
            id_tipo_victima,
            id_tipo_victima_moral,
            id_sexo,
            id_genero,
            id_nacionalidad,
            id_pertenece_poblacion_indigena,
            id_presenta_discapacidad,
            fecha_nacimiento,
            edad,
            nombre_vicfem,
            primer_apellido_vicfem,
            segundo_apellido_vicfem,
            curp_vicfem,
            id_usuario_registro,
            fecha_registro,
            id_carga,
            @IdUsuarioConfirmacion,
            @IdCargaActualizacion,
            'ELIMINADO',
            SYSDATETIME(),
            activo
        FROM victimas_eliminadas;
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion,
            IdUsuarioConfirmacion = idUsuarioConfirmacion
        }, transaction);
    }

    private async Task DesactivarVictimasEliminadasAsync(SqlConnection connection, SqlTransaction transaction, long idCargaActualizacion)
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
        victimas_actuales AS (
            SELECT
                v.id_victima,
                ci.identificador_carpeta_fiscalia,
                d.identificador_delito_fiscalia,
                v.identificador_victima_fiscalia,
                ROW_NUMBER() OVER (
                    PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia, v.identificador_victima_fiscalia
                    ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, v.id_carga DESC, v.id_victima DESC
                ) AS rn
            FROM victima v
            INNER JOIN delito d
                ON d.id_delito = v.id_delito
               AND d.activo = 1
            INNER JOIN carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN cargas_periodo cp
                ON cp.id_carga = v.id_carga
            WHERE v.activo = 1
        )
        UPDATE vi
        SET vi.activo = 0,
            vi.id_carga = @IdCargaActualizacion
        FROM victima vi
        INNER JOIN victimas_actuales va
            ON va.id_victima = vi.id_victima
           AND va.rn = 1
        LEFT JOIN carga_tmp_victima vt
            ON vt.id_ci = va.identificador_carpeta_fiscalia
           AND vt.id_delito = va.identificador_delito_fiscalia
           AND vt.id_vicf = va.identificador_victima_fiscalia
           AND vt.id_carga = @IdCargaActualizacion
           AND vt.activo = 1
        WHERE vi.activo = 1
          AND vt.id_vicf IS NULL;
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdCargaActualizacion = idCargaActualizacion
        }, transaction);
    }

}
