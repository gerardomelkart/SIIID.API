using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Globalization;

namespace SIIID2.Api.Repositories;
// Recuperación del contenido confirmado de una semana y sus bloques.
public partial class SemanalEnviosRepository : ISemanalEnviosRepository
{
    public Task<List<IDictionary<string, object?>>> ObtenerCarpetasConfirmadasSemanaAsync(SemanalEnvioReferenciaInfo referencia)
    {
        const string sql = @"
        WITH bloques_referencia AS
        (
            SELECT
                bloque.fecha_inicio_tramo,
                bloque.fecha_fin_tramo
            FROM dbo.semanal_carga_bloque bloque
            WHERE bloque.id_semanal_carga = @IdSemanalCarga
              AND bloque.activo = 1

            UNION ALL

            SELECT
                sc.fecha_inicio_tramo,
                sc.fecha_fin_tramo
            FROM dbo.semanal_carga sc
            WHERE sc.id_semanal_carga = @IdSemanalCarga
              AND sc.activo = 1
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.semanal_carga_bloque bloque
                  WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                    AND bloque.activo = 1
              )
        )
        SELECT
            ci.identificador_carpeta_fiscalia AS id_ci,
            ci.nomenclatura_carpeta_fiscalia AS ntra_ci,
            ISNULL(CONVERT(varchar(10), ci.fecha_inicio, 103), '') AS fha_de_ini,
            CASE
                WHEN CONVERT(time, ci.fecha_inicio) = '00:00:00' THEN ''
                ELSE CONVERT(varchar(8), ci.fecha_inicio, 108)
            END AS hra_de_ini,
            ci.resumen_hechos AS rmen_de_hchos,
            ci.denuncia_anonima AS denuncia_anonima,
            ci.denuncia_anonima_089 AS denuncia_anonima_089,
            ci.denuncia_anonima_otro_medio AS denuncia_anonima_otro_medio
        FROM dbo.semanal_carpeta_investigacion ci
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = ci.id_semanal_carga
        WHERE sc.id_entidad_federativa = @IdEntidadFederativa
          AND sc.id_semanal_carga = @IdSemanalCarga
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
          AND sc.activo = 1
          AND ci.activo = 1
          AND EXISTS
          (
              SELECT 1
              FROM bloques_referencia bloque
              WHERE ci.fecha_inicio >= bloque.fecha_inicio_tramo
                AND ci.fecha_inicio < DATEADD(DAY, 1, bloque.fecha_fin_tramo)
          )
        ORDER BY
            ci.identificador_carpeta_fiscalia;
        ";
        return QueryDictionaryAsync(sql, referencia);
    }

    public Task<List<IDictionary<string, object?>>> ObtenerDelitosConfirmadosSemanaAsync(SemanalEnvioReferenciaInfo referencia)
    {
        const string sql = @"
        WITH bloques_referencia AS
        (
            SELECT
                bloque.fecha_inicio_tramo,
                bloque.fecha_fin_tramo
            FROM dbo.semanal_carga_bloque bloque
            WHERE bloque.id_semanal_carga = @IdSemanalCarga
              AND bloque.activo = 1

            UNION ALL

            SELECT
                sc.fecha_inicio_tramo,
                sc.fecha_fin_tramo
            FROM dbo.semanal_carga sc
            WHERE sc.id_semanal_carga = @IdSemanalCarga
              AND sc.activo = 1
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.semanal_carga_bloque bloque
                  WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                    AND bloque.activo = 1
              )
        )
        SELECT
            ci.identificador_carpeta_fiscalia AS id_ci,
            d.identificador_delito_fiscalia AS id_delito,
            d.delito_fiscalia AS dto,
            d.modalidad_delito_fiscalia AS moda_dto,
            CONVERT(varchar(10), fa.clave) AS forma_acc,
            CONVERT(varchar(10), d.fecha_hechos, 103) AS fha_de_hchos,
            CASE
                WHEN d.fecha_hechos IS NULL THEN ''
                WHEN CONVERT(time, d.fecha_hechos) = '00:00:00' THEN ''
                ELSE CONVERT(varchar(8), d.fecha_hechos, 108)
            END AS hra_de_hchos,
            CONVERT(varchar(10), ic.clave) AS emto_com_dto,
            CONVERT(varchar(10), gc.clave) AS grdo_cons,
            md.clave4 AS clasf_de_dto,
            ISNULL(efh.nombre, '') AS nom_ent_hchos,
            CONVERT(varchar(10), d.id_entidad_federativa) AS id_ent_hchos,
            ISNULL(mun.nombre, '') AS nom_mun_hchos,
            mun.clave AS id_mun_hchos,
            d.id_localidad_fiscalia AS id_loc_hchos,
            d.localidad_fiscalia_nombre AS nom_loc_hchos,
            d.id_colonia_fiscalia AS id_col_hchos,
            d.colonia_fiscalia_nombre AS nom_col_hchos,
            cp.codigo_postal AS cp,
            d.coordenada_x AS coord_x,
            d.coordenada_y AS coord_y,
            d.domicilio_hechos AS dom_hchos
        FROM dbo.semanal_delito d
        INNER JOIN dbo.semanal_carpeta_investigacion ci
            ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = d.id_semanal_carga
        INNER JOIN dbo.catalogo_modalidad_delito md
            ON md.id_modalidad_delito = d.id_modalidad_delito
        INNER JOIN dbo.catalogo_forma_accion fa
            ON fa.id_forma_accion = d.id_forma_accion
        INNER JOIN dbo.catalogo_instrumento_comision ic
            ON ic.id_instrumento_comision = d.id_instrumento_comision
        INNER JOIN dbo.catalogo_grado_consumacion gc
            ON gc.id_grado_consumacion = d.id_grado_consumacion
        LEFT JOIN dbo.catalogo_entidad_federativa efh
            ON efh.id_entidad_federativa = d.id_entidad_federativa
        INNER JOIN dbo.catalogo_municipio mun
            ON mun.id_municipio = d.id_municipio
           AND mun.id_entidad_federativa = d.id_entidad_federativa
           AND mun.activo = 1
        LEFT JOIN dbo.catalogo_codigo_postal cp
            ON cp.id_codigo_postal = d.id_codigo_postal
        WHERE sc.id_entidad_federativa = @IdEntidadFederativa
          AND sc.id_semanal_carga = @IdSemanalCarga
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
          AND sc.activo = 1
          AND d.activo = 1
          AND EXISTS
          (
              SELECT 1
              FROM bloques_referencia bloque
              WHERE ci.fecha_inicio >= bloque.fecha_inicio_tramo
                AND ci.fecha_inicio < DATEADD(DAY, 1, bloque.fecha_fin_tramo)
          )
        ORDER BY
            ci.identificador_carpeta_fiscalia,
            d.identificador_delito_fiscalia;
        ";
        return QueryDictionaryAsync(sql, referencia);
    }

    public Task<List<IDictionary<string, object?>>> ObtenerVictimasConfirmadasSemanaAsync(SemanalEnvioReferenciaInfo referencia)
    {
        const string sql = @"
        WITH bloques_referencia AS
        (
            SELECT
                bloque.fecha_inicio_tramo,
                bloque.fecha_fin_tramo
            FROM dbo.semanal_carga_bloque bloque
            WHERE bloque.id_semanal_carga = @IdSemanalCarga
              AND bloque.activo = 1

            UNION ALL

            SELECT
                sc.fecha_inicio_tramo,
                sc.fecha_fin_tramo
            FROM dbo.semanal_carga sc
            WHERE sc.id_semanal_carga = @IdSemanalCarga
              AND sc.activo = 1
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.semanal_carga_bloque bloque
                  WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                    AND bloque.activo = 1
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
        FROM dbo.semanal_victima v
        INNER JOIN dbo.semanal_delito d
            ON d.id_semanal_delito = v.id_semanal_delito
           AND d.activo = 1
        INNER JOIN dbo.semanal_carpeta_investigacion ci
            ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = v.id_semanal_carga
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
        WHERE sc.id_entidad_federativa = @IdEntidadFederativa
          AND sc.id_semanal_carga = @IdSemanalCarga
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
          AND sc.activo = 1
          AND v.activo = 1
          AND EXISTS
          (
              SELECT 1
              FROM bloques_referencia bloque
              WHERE ci.fecha_inicio >= bloque.fecha_inicio_tramo
                AND ci.fecha_inicio < DATEADD(DAY, 1, bloque.fecha_fin_tramo)
          )
        ORDER BY
            ci.identificador_carpeta_fiscalia,
            d.identificador_delito_fiscalia,
            v.identificador_victima_fiscalia;
        ";
        return QueryDictionaryAsync(sql, referencia);
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryAsync(string sql, SemanalEnvioReferenciaInfo referencia)
    {
        using var connection = _dbConnectionFactory.CrearConexion();
        var filas = await connection.QueryAsync(sql, new { referencia.IdSemanalCarga, referencia.IdEntidadFederativa });
        return filas.Select(fila => ((IDictionary<string, object?>)fila).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)).Cast<IDictionary<string, object?>>().ToList();
    }
}
