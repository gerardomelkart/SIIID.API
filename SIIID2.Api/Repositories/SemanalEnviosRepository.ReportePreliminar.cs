using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Globalization;

namespace SIIID2.Api.Repositories;

// Selección y exportación preliminar por periodo, delito y alcance del usuario.
public partial class SemanalEnviosRepository : ISemanalEnviosRepository
{
    public async Task<List<SemanalReportePreliminarEntidadItem>> ObtenerEntidadesReportePreliminarAsync(int anioCorte, int mesCorte, string modoReporte, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        var sql = "WITH " + SqlPendientesReportePreliminar + @",
        fuente AS
        (
            SELECT
                sc.id_entidad_federativa
            FROM dbo.semanal_delito d
            INNER JOIN dbo.semanal_carpeta_investigacion ci
                ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN dbo.semanal_carga sc
                ON sc.id_semanal_carga = d.id_semanal_carga
            WHERE @ModoReporte IN (N'CONFIRMADO', N'MIXTO')
              AND ci.fecha_inicio >= @FechaInicio
              AND ci.fecha_inicio < @FechaFinExclusiva
              AND d.activo = 1
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
              AND sc.activo = 1
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
              AND
              (
                  @ModoReporte = N'CONFIRMADO'
                  OR NOT EXISTS
                  (
                      SELECT 1
                      FROM pendientes p
                      WHERE p.id_entidad_federativa = sc.id_entidad_federativa
                        AND p.id_usuario_carga = sc.id_usuario_carga
                        AND p.id_delito = d.id_catalogo_delito
                        AND ci.fecha_inicio >= p.fecha_inicio_tramo
                        AND ci.fecha_inicio < DATEADD(DAY, 1, p.fecha_fin_tramo)
                  )
              )

            UNION ALL

            SELECT
                p.id_entidad_federativa
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
            WHERE @ModoReporte IN (N'PREVIO', N'MIXTO')
              AND fecha.fecha_inicio >= p.fecha_inicio_tramo
              AND fecha.fecha_inicio < DATEADD(DAY, 1, p.fecha_fin_tramo)
              AND fecha.fecha_inicio >= @FechaInicio
              AND fecha.fecha_inicio < @FechaFinExclusiva
        )
        SELECT DISTINCT
            fuente.id_entidad_federativa AS IdEntidadFederativa,
            ef.nombre AS EntidadFederativa
        FROM fuente
        INNER JOIN dbo.catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = fuente.id_entidad_federativa
           AND ef.activo = 1
        ORDER BY
            ef.nombre
OPTION (RECOMPILE);
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        var fechaInicio = new DateTime(anioCorte, mesCorte, 1);

        return (await connection.QueryAsync<SemanalReportePreliminarEntidadItem>(sql, new
        {
            AnioCorte = anioCorte,
            MesCorte = mesCorte,
            FechaInicio = fechaInicio,
            FechaFinExclusiva = fechaInicio.AddMonths(1),
            ModoReporte = modoReporte,
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga
        }, commandTimeout: 300)).ToList();
    }

    public async Task<List<SemanalReportePreliminarDelitoItem>> ObtenerDelitosReportePreliminarAsync(int anioCorte, int mesCorte, string modoReporte, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        var sql = "WITH " + SqlPendientesReportePreliminar + @",
        fuente AS
        (
            SELECT
                d.id_catalogo_delito AS id_delito
            FROM dbo.semanal_delito d
            INNER JOIN dbo.semanal_carpeta_investigacion ci
                ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN dbo.semanal_carga sc
                ON sc.id_semanal_carga = d.id_semanal_carga
            WHERE @ModoReporte IN (N'CONFIRMADO', N'MIXTO')
              AND ci.fecha_inicio >= @FechaInicio
              AND ci.fecha_inicio < @FechaFinExclusiva
              AND d.activo = 1
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
              AND sc.activo = 1
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
              AND
              (
                  @ModoReporte = N'CONFIRMADO'
                  OR NOT EXISTS
                  (
                      SELECT 1
                      FROM pendientes p
                      WHERE p.id_entidad_federativa = sc.id_entidad_federativa
                        AND p.id_usuario_carga = sc.id_usuario_carga
                        AND p.id_delito = d.id_catalogo_delito
                        AND ci.fecha_inicio >= p.fecha_inicio_tramo
                        AND ci.fecha_inicio < DATEADD(DAY, 1, p.fecha_fin_tramo)
                  )
              )

            UNION ALL

            SELECT
                p.id_delito
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
            WHERE @ModoReporte IN (N'PREVIO', N'MIXTO')
              AND fecha.fecha_inicio >= p.fecha_inicio_tramo
              AND fecha.fecha_inicio < DATEADD(DAY, 1, p.fecha_fin_tramo)
              AND fecha.fecha_inicio >= @FechaInicio
              AND fecha.fecha_inicio < @FechaFinExclusiva
        )
        SELECT DISTINCT
            cd.id_delito AS IdDelito,
            cd.clave2 AS ClaveDelito,
            cd.delito AS Delito
        FROM fuente
        INNER JOIN dbo.catalogo_delito cd
            ON cd.id_delito = fuente.id_delito
           AND cd.activo = 1
        ORDER BY
            cd.clave2,
            cd.delito
OPTION (RECOMPILE);
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        var fechaInicio = new DateTime(anioCorte, mesCorte, 1);

        return (await connection.QueryAsync<SemanalReportePreliminarDelitoItem>(sql, new
        {
            AnioCorte = anioCorte,
            MesCorte = mesCorte,
            FechaInicio = fechaInicio,
            FechaFinExclusiva = fechaInicio.AddMonths(1),
            ModoReporte = modoReporte,
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga
        }, commandTimeout: 300)).ToList();
    }

    public Task<List<IDictionary<string, object?>>> ObtenerCarpetasReportePreliminarAsync(int anioCorte, int mesCorte, int idDelito, string modoReporte, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        var sql = "WITH " + SqlPendientesReportePreliminar + @",
        fuente AS
        (
            SELECT
                ef.nombre AS [Nombre entidad],
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
            INNER JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = sc.id_entidad_federativa
               AND ef.activo = 1
            WHERE @ModoReporte IN (N'CONFIRMADO', N'MIXTO')
              AND ci.fecha_inicio >= @FechaInicio
              AND ci.fecha_inicio < @FechaFinExclusiva
              AND ci.activo = 1
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
              AND sc.activo = 1
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.semanal_delito d
                  WHERE d.id_semanal_carpeta_investigacion = ci.id_semanal_carpeta_investigacion
                    AND d.id_catalogo_delito = @IdDelito
                    AND d.activo = 1
              )
              AND
              (
                  @ModoReporte = N'CONFIRMADO'
                  OR NOT EXISTS
                  (
                      SELECT 1
                      FROM pendientes p
                      WHERE p.id_entidad_federativa = sc.id_entidad_federativa
                        AND p.id_usuario_carga = sc.id_usuario_carga
                        AND p.id_delito = @IdDelito
                        AND ci.fecha_inicio >= p.fecha_inicio_tramo
                        AND ci.fecha_inicio < DATEADD(DAY, 1, p.fecha_fin_tramo)
                  )
              )

            UNION ALL

            SELECT
                ef.nombre AS [Nombre entidad],
                carpeta.id_ci,
                carpeta.ntra_ci,
                carpeta.fha_de_ini,
                ISNULL(carpeta.hra_de_ini, N'') AS hra_de_ini,
                carpeta.rmen_de_hchos,
                carpeta.denuncia_anonima,
                carpeta.denuncia_anonima_089,
                carpeta.denuncia_anonima_otro_medio
            FROM pendientes p
            INNER JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = p.id_entidad_federativa
               AND ef.activo = 1
            INNER JOIN dbo.semanal_carga_tmp_carpeta carpeta
                ON carpeta.id_semanal_carga = p.id_semanal_carga
               AND carpeta.incluido = 1
               AND carpeta.activo = 1
            CROSS APPLY
            (
                SELECT COALESCE
                (
                    TRY_CONVERT(date, NULLIF(LTRIM(RTRIM(carpeta.fha_de_ini)), N''), 103),
                    TRY_CONVERT(date, NULLIF(LTRIM(RTRIM(carpeta.fha_de_ini)), N''))
                ) AS fecha_inicio
            ) fecha
            WHERE @ModoReporte IN (N'PREVIO', N'MIXTO')
              AND fecha.fecha_inicio >= p.fecha_inicio_tramo
              AND fecha.fecha_inicio < DATEADD(DAY, 1, p.fecha_fin_tramo)
              AND p.id_delito = @IdDelito
              AND fecha.fecha_inicio >= @FechaInicio
              AND fecha.fecha_inicio < @FechaFinExclusiva
              AND EXISTS
              (
                  SELECT 1
                  FROM dbo.semanal_carga_tmp_delito d
                  WHERE d.id_semanal_carga = carpeta.id_semanal_carga
                    AND d.id_ci = carpeta.id_ci
                    AND d.incluido = 1
                    AND d.activo = 1
              )
        )
        SELECT *
        FROM fuente
        ORDER BY
            [Nombre entidad],
            id_ci
OPTION (RECOMPILE);
        ";

        return QueryDictionaryReportePreliminarAsync(sql, anioCorte, mesCorte, idDelito, modoReporte, idEntidadFederativa, idUsuarioCarga);
    }

    public Task<List<IDictionary<string, object?>>> ObtenerDelitosReportePreliminarAsync(int anioCorte, int mesCorte, int idDelito, string modoReporte, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        var sql = "WITH " + SqlPendientesReportePreliminar + @",
        fuente AS
        (
            SELECT
                ef.nombre AS [Nombre entidad],
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
                CONVERT(varchar(50), d.coordenada_x) AS coord_x,
                CONVERT(varchar(50), d.coordenada_y) AS coord_y,
                d.domicilio_hechos AS dom_hchos
            FROM dbo.semanal_delito d
            INNER JOIN dbo.semanal_carpeta_investigacion ci
                ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN dbo.semanal_carga sc
                ON sc.id_semanal_carga = d.id_semanal_carga
            INNER JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = sc.id_entidad_federativa
               AND ef.activo = 1
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
            WHERE @ModoReporte IN (N'CONFIRMADO', N'MIXTO')
              AND ci.fecha_inicio >= @FechaInicio
              AND ci.fecha_inicio < @FechaFinExclusiva
              AND d.id_catalogo_delito = @IdDelito
              AND d.activo = 1
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
              AND sc.activo = 1
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
              AND
              (
                  @ModoReporte = N'CONFIRMADO'
                  OR NOT EXISTS
                  (
                      SELECT 1
                      FROM pendientes p
                      WHERE p.id_entidad_federativa = sc.id_entidad_federativa
                        AND p.id_usuario_carga = sc.id_usuario_carga
                        AND p.id_delito = @IdDelito
                        AND ci.fecha_inicio >= p.fecha_inicio_tramo
                        AND ci.fecha_inicio < DATEADD(DAY, 1, p.fecha_fin_tramo)
                  )
              )

            UNION ALL

            SELECT
                ef.nombre AS [Nombre entidad],
                d.id_ci,
                d.id_delito,
                d.dto,
                d.moda_dto,
                d.forma_acc,
                d.fha_de_hchos,
                ISNULL(d.hra_de_hchos, N'') AS hra_de_hchos,
                d.emto_com_dto,
                d.grdo_cons,
                d.clasf_de_dto,
                ISNULL(efh.nombre, '') AS nom_ent_hchos,
                d.id_ent_hchos,
                ISNULL(mun.nombre, '') AS nom_mun_hchos,
                d.id_mun_hchos,
                d.id_loc_hchos,
                d.nom_loc_hchos,
                d.id_col_hchos,
                d.nom_col_hchos,
                d.cp,
                d.coord_x,
                d.coord_y,
                d.dom_hchos
            FROM pendientes p
            INNER JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = p.id_entidad_federativa
               AND ef.activo = 1
            INNER JOIN dbo.semanal_carga_tmp_carpeta carpeta
                ON carpeta.id_semanal_carga = p.id_semanal_carga
               AND carpeta.incluido = 1
               AND carpeta.activo = 1
            INNER JOIN dbo.semanal_carga_tmp_delito d
                ON d.id_semanal_carga = carpeta.id_semanal_carga
               AND d.id_ci = carpeta.id_ci
               AND d.incluido = 1
               AND d.activo = 1
            LEFT JOIN dbo.catalogo_entidad_federativa efh
                ON efh.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos)
            LEFT JOIN dbo.catalogo_municipio mun
                ON mun.id_entidad_federativa = efh.id_entidad_federativa
               AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos)
            CROSS APPLY
            (
                SELECT COALESCE
                (
                    TRY_CONVERT(date, NULLIF(LTRIM(RTRIM(carpeta.fha_de_ini)), N''), 103),
                    TRY_CONVERT(date, NULLIF(LTRIM(RTRIM(carpeta.fha_de_ini)), N''))
                ) AS fecha_inicio
            ) fecha
            WHERE @ModoReporte IN (N'PREVIO', N'MIXTO')
              AND fecha.fecha_inicio >= p.fecha_inicio_tramo
              AND fecha.fecha_inicio < DATEADD(DAY, 1, p.fecha_fin_tramo)
              AND p.id_delito = @IdDelito
              AND fecha.fecha_inicio >= @FechaInicio
              AND fecha.fecha_inicio < @FechaFinExclusiva
        )
        SELECT *
        FROM fuente
        ORDER BY
            [Nombre entidad],
            id_ci,
            id_delito
OPTION (RECOMPILE);
        ";

        return QueryDictionaryReportePreliminarAsync(sql, anioCorte, mesCorte, idDelito, modoReporte, idEntidadFederativa, idUsuarioCarga);
    }

    public Task<List<IDictionary<string, object?>>> ObtenerVictimasReportePreliminarAsync(int anioCorte, int mesCorte, int idDelito, string modoReporte, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        var sql = "WITH " + SqlPendientesReportePreliminar + @",
        fuente AS
        (
            SELECT
                ef.nombre AS [Nombre entidad],
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
                CONVERT(varchar(10), v.edad) AS edad,
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
            INNER JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = sc.id_entidad_federativa
               AND ef.activo = 1
            INNER JOIN dbo.catalogo_tipo_victima tv
                ON tv.id_tipo_victima = v.id_tipo_victima
            LEFT JOIN dbo.catalogo_tipo_victima_moral tvm
                ON tvm.id_tipo_victima_moral = v.id_tipo_victima_moral
            LEFT JOIN dbo.catalogo_sexo sx
                ON sx.id_sexo = v.id_sexo
            LEFT JOIN dbo.catalogo_genero gen
                ON gen.id_genero = v.id_genero
            LEFT JOIN dbo.catalogo_nacionalidad nac
                ON nac.id_nacionalidad = v.id_nacionalidad
            LEFT JOIN dbo.catalogo_pertenece_poblacion_indigena pob
                ON pob.id_pertenece_poblacion_indigena = v.id_pertenece_poblacion_indigena
            LEFT JOIN dbo.catalogo_presenta_discapacidad disc
                ON disc.id_presenta_discapacidad = v.id_presenta_discapacidad
            WHERE @ModoReporte IN (N'CONFIRMADO', N'MIXTO')
              AND ci.fecha_inicio >= @FechaInicio
              AND ci.fecha_inicio < @FechaFinExclusiva
              AND d.id_catalogo_delito = @IdDelito
              AND v.activo = 1
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
              AND sc.activo = 1
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
              AND
              (
                  @ModoReporte = N'CONFIRMADO'
                  OR NOT EXISTS
                  (
                      SELECT 1
                      FROM pendientes p
                      WHERE p.id_entidad_federativa = sc.id_entidad_federativa
                        AND p.id_usuario_carga = sc.id_usuario_carga
                        AND p.id_delito = @IdDelito
                        AND ci.fecha_inicio >= p.fecha_inicio_tramo
                        AND ci.fecha_inicio < DATEADD(DAY, 1, p.fecha_fin_tramo)
                  )
              )

            UNION ALL

            SELECT
                ef.nombre AS [Nombre entidad],
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
                v.nacional
            FROM pendientes p
            INNER JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = p.id_entidad_federativa
               AND ef.activo = 1
            INNER JOIN dbo.semanal_carga_tmp_carpeta carpeta
                ON carpeta.id_semanal_carga = p.id_semanal_carga
               AND carpeta.incluido = 1
               AND carpeta.activo = 1
            INNER JOIN dbo.semanal_carga_tmp_delito d
                ON d.id_semanal_carga = carpeta.id_semanal_carga
               AND d.id_ci = carpeta.id_ci
               AND d.incluido = 1
               AND d.activo = 1
            INNER JOIN dbo.semanal_carga_tmp_victima v
                ON v.id_semanal_carga = d.id_semanal_carga
               AND v.id_ci = d.id_ci
               AND v.id_delito = d.id_delito
               AND v.incluido = 1
               AND v.activo = 1
            CROSS APPLY
            (
                SELECT COALESCE
                (
                    TRY_CONVERT(date, NULLIF(LTRIM(RTRIM(carpeta.fha_de_ini)), N''), 103),
                    TRY_CONVERT(date, NULLIF(LTRIM(RTRIM(carpeta.fha_de_ini)), N''))
                ) AS fecha_inicio
            ) fecha
            WHERE @ModoReporte IN (N'PREVIO', N'MIXTO')
              AND fecha.fecha_inicio >= p.fecha_inicio_tramo
              AND fecha.fecha_inicio < DATEADD(DAY, 1, p.fecha_fin_tramo)
              AND p.id_delito = @IdDelito
              AND fecha.fecha_inicio >= @FechaInicio
              AND fecha.fecha_inicio < @FechaFinExclusiva
        )
        SELECT *
        FROM fuente
        ORDER BY
            [Nombre entidad],
            id_ci,
            id_delito,
            id_vicf
OPTION (RECOMPILE);
        ";

        return QueryDictionaryReportePreliminarAsync(sql, anioCorte, mesCorte, idDelito, modoReporte, idEntidadFederativa, idUsuarioCarga);
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryReportePreliminarAsync(string sql, int anioCorte, int mesCorte, int idDelito, string modoReporte, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        using var connection = _dbConnectionFactory.CrearConexion();
        var fechaInicio = new DateTime(anioCorte, mesCorte, 1);

        var filas = await connection.QueryAsync(sql, new
        {
            AnioCorte = anioCorte,
            MesCorte = mesCorte,
            FechaInicio = fechaInicio,
            FechaFinExclusiva = fechaInicio.AddMonths(1),
            IdDelito = idDelito,
            ModoReporte = modoReporte,
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga
        }, commandTimeout: 300);

        return filas.Select(fila => ((IDictionary<string, object?>)fila).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)).Cast<IDictionary<string, object?>>().ToList();
    }


}
