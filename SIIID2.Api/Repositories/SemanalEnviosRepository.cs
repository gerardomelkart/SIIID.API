using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class SemanalEnviosRepository : ISemanalEnviosRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SemanalEnviosRepository(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    public async Task<List<SemanalEnvioItem>> ObtenerEnviosAsync(bool esSuperUsuario, int? idEntidadFederativaUsuario, int? idEntidadFederativa, int? anioSemana, int? numeroSemana, string? tipoCarga, string? estado)
    {
        const string sql = @"
        WITH ultimo_visible AS
        (
            SELECT
                sc.id_semanal_carga,
                ROW_NUMBER() OVER
                (
                    PARTITION BY
                        sc.id_entidad_federativa,
                        sc.anio_semana,
                        sc.numero_semana
                    ORDER BY
                        COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) DESC,
                        sc.id_semanal_carga DESC
                ) AS rn
            FROM dbo.semanal_carga sc
            WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND
              (
                  sc.estado NOT LIKE N'RECHAZADO%'
                  OR sc.estado = N'RECHAZADO_ADMIN'
              )
              AND sc.activo = 1
        ),
        cargas_visibles AS
        (
            SELECT id_semanal_carga
            FROM ultimo_visible
            WHERE rn = 1
        )
        SELECT
            sc.id_semanal_carga AS IdSemanalCarga,
            sc.codigo_referencia AS CodigoReferencia,
            sc.tipo_carga AS TipoCarga,
            sc.id_entidad_federativa AS IdEntidadFederativa,
            ISNULL(ef.nombre, N'') AS EntidadFederativa,
            ISNULL(ef.clave, N'') AS ClaveEntidad,
            sc.anio_semana AS AnioSemana,
            sc.numero_semana AS NumeroSemana,
            sc.fecha_inicio_semana AS FechaInicioSemana,
            sc.fecha_fin_semana AS FechaFinSemana,
            sc.fecha_inicio_tramo AS FechaInicioTramo,
            sc.fecha_fin_tramo AS FechaFinTramo,
            sc.mes_corte AS MesCorte,
            sc.anio_corte AS AnioCorte,
            sc.id_usuario_carga AS IdUsuarioCarga,
            u.usuario AS UsuarioCarga,
            COALESCE
            (
                NULLIF
                (
                    LTRIM(RTRIM(CONCAT
                    (
                        u.nombre,
                        N' ',
                        u.primer_apellido,
                        CASE
                            WHEN NULLIF(u.segundo_apellido, N'') IS NULL THEN N''
                            ELSE CONCAT(N' ', u.segundo_apellido)
                        END
                    ))),
                    N''
                ),
                u.usuario
            ) AS NombreUsuarioCarga,
            sc.total_carpetas_incluidas AS TotalCarpetasIncluidas,
            sc.total_delitos_incluidos AS TotalDelitosIncluidos,
            sc.total_victimas_incluidas AS TotalVictimasIncluidas,
            (
                SELECT COUNT(1)
                FROM dbo.semanal_carga_advertencia advertencia
                WHERE advertencia.id_semanal_carga = sc.id_semanal_carga
                  AND advertencia.activo = 1
            ) AS TotalAdvertencias,
            sc.estado AS Estado,
            sc.fecha_carga AS FechaCarga,
            sc.fecha_validacion AS FechaValidacion,
            sc.fecha_confirmacion AS FechaConfirmacion,
            COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) AS FechaMovimiento,
            CASE
                WHEN sc.estado = N'RECHAZADO_ADMIN' THEN sc.mensaje_error
                ELSE NULL
            END AS MotivoRechazo,
            usuario_resolucion.usuario AS UsuarioResolucion,
            CONVERT
            (
                bit,
                CASE
                    WHEN EXISTS
                    (
                        SELECT 1
                        FROM dbo.semanal_carga_tmp_carpeta carpeta
                        WHERE carpeta.id_semanal_carga = sc.id_semanal_carga
                          AND carpeta.activo = 1
                    )
                    OR EXISTS
                    (
                        SELECT 1
                        FROM dbo.semanal_carga_tmp_delito delito
                        WHERE delito.id_semanal_carga = sc.id_semanal_carga
                          AND delito.activo = 1
                    )
                    OR EXISTS
                    (
                        SELECT 1
                        FROM dbo.semanal_carga_tmp_victima victima
                        WHERE victima.id_semanal_carga = sc.id_semanal_carga
                          AND victima.activo = 1
                    )
                    THEN 1
                    ELSE 0
                END
            ) AS TieneStagingDisponible
        FROM cargas_visibles visible
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = visible.id_semanal_carga
        INNER JOIN dbo.usuario u
            ON u.id_usuario = sc.id_usuario_carga
        LEFT JOIN dbo.catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = sc.id_entidad_federativa
        LEFT JOIN dbo.usuario usuario_resolucion
            ON usuario_resolucion.id_usuario = sc.id_usuario_confirmacion
        WHERE (@EsSuperUsuario = 1 OR sc.id_entidad_federativa = @IdEntidadFederativaUsuario)
          AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
          AND (@AnioSemana IS NULL OR sc.anio_semana = @AnioSemana)
          AND (@NumeroSemana IS NULL OR sc.numero_semana = @NumeroSemana)
          AND (@TipoCarga IS NULL OR sc.tipo_carga = @TipoCarga)
          AND (@Estado IS NULL OR sc.estado = @Estado)
        ORDER BY
            ef.nombre,
            sc.anio_semana DESC,
            sc.numero_semana DESC,
            COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) DESC,
            sc.id_semanal_carga DESC;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return (await connection.QueryAsync<SemanalEnvioItem>(sql, new
        {
            EsSuperUsuario = esSuperUsuario,
            IdEntidadFederativaUsuario = idEntidadFederativaUsuario,
            IdEntidadFederativa = esSuperUsuario ? idEntidadFederativa : null,
            AnioSemana = anioSemana,
            NumeroSemana = numeroSemana,
            TipoCarga = tipoCarga,
            Estado = estado
        })).ToList();
    }

    public async Task<SemanalEnvioReferenciaInfo?> ObtenerReferenciaAsync(string codigoReferencia)
    {
        const string sql = @"
            SELECT TOP (1)
                sc.id_semanal_carga AS IdSemanalCarga,
                sc.codigo_referencia AS CodigoReferencia,
                sc.tipo_carga AS TipoCarga,
                sc.estado AS Estado,
                sc.id_entidad_federativa AS IdEntidadFederativa,
                ISNULL(ef.nombre, N'') AS EntidadFederativa,
                sc.anio_semana AS AnioSemana,
                sc.numero_semana AS NumeroSemana
            FROM dbo.semanal_carga sc
            LEFT JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = sc.id_entidad_federativa
            WHERE sc.codigo_referencia = @CodigoReferencia
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.activo = 1;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QueryFirstOrDefaultAsync<SemanalEnvioReferenciaInfo>(sql, new { CodigoReferencia = codigoReferencia });
    }

    public Task<List<IDictionary<string, object?>>> ObtenerCarpetasConfirmadasSemanaAsync(SemanalEnvioReferenciaInfo referencia)
    {
        const string sql = @"
        WITH cargas_semana AS
        (
            SELECT
                sc.id_semanal_carga
            FROM dbo.semanal_carga sc
            WHERE sc.id_entidad_federativa = @IdEntidadFederativa
              AND sc.anio_semana = @AnioSemana
              AND sc.numero_semana = @NumeroSemana
              AND sc.activo = 1
              AND
              (
                  sc.tipo_carga = N'CARGA_INICIAL'
                  AND sc.estado = N'CONFIRMADO'
                  OR
                  sc.tipo_carga = N'ACTUALIZACION'
                  AND sc.estado = N'CONFIRMADO_ACTUALIZACION'
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
            ci.resumen_hechos AS rmen_de_hchos
        FROM dbo.semanal_carpeta_investigacion ci
        INNER JOIN cargas_semana cs
            ON cs.id_semanal_carga = ci.id_semanal_carga
        WHERE ci.activo = 1
        ORDER BY
            ci.identificador_carpeta_fiscalia;
    ";

        return QueryDictionaryAsync(sql, referencia);
    }

    public Task<List<IDictionary<string, object?>>> ObtenerDelitosConfirmadosSemanaAsync(SemanalEnvioReferenciaInfo referencia)
    {
        const string sql = @"
        WITH cargas_semana AS
        (
            SELECT
                sc.id_semanal_carga
            FROM dbo.semanal_carga sc
            WHERE sc.id_entidad_federativa = @IdEntidadFederativa
              AND sc.anio_semana = @AnioSemana
              AND sc.numero_semana = @NumeroSemana
              AND sc.activo = 1
              AND
              (
                  sc.tipo_carga = N'CARGA_INICIAL'
                  AND sc.estado = N'CONFIRMADO'
                  OR
                  sc.tipo_carga = N'ACTUALIZACION'
                  AND sc.estado = N'CONFIRMADO_ACTUALIZACION'
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
        FROM dbo.semanal_delito d
        INNER JOIN cargas_semana cs
            ON cs.id_semanal_carga = d.id_semanal_carga
        INNER JOIN dbo.semanal_carpeta_investigacion ci
            ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN dbo.catalogo_modalidad_delito md
            ON md.id_modalidad_delito = d.id_modalidad_delito
        INNER JOIN dbo.catalogo_forma_accion fa
            ON fa.id_forma_accion = d.id_forma_accion
        INNER JOIN dbo.catalogo_instrumento_comision ic
            ON ic.id_instrumento_comision = d.id_instrumento_comision
        INNER JOIN dbo.catalogo_grado_consumacion gc
            ON gc.id_grado_consumacion = d.id_grado_consumacion
        INNER JOIN dbo.catalogo_municipio mun
            ON mun.id_municipio = d.id_municipio
           AND mun.id_entidad_federativa = d.id_entidad_federativa
           AND mun.activo = 1
        LEFT JOIN dbo.catalogo_codigo_postal cp
            ON cp.id_codigo_postal = d.id_codigo_postal
        WHERE d.activo = 1
        ORDER BY
            ci.identificador_carpeta_fiscalia,
            d.identificador_delito_fiscalia;
    ";

        return QueryDictionaryAsync(sql, referencia);
    }

    public Task<List<IDictionary<string, object?>>> ObtenerVictimasConfirmadasSemanaAsync(SemanalEnvioReferenciaInfo referencia)
    {
        const string sql = @"
        WITH cargas_semana AS
        (
            SELECT
                sc.id_semanal_carga
            FROM dbo.semanal_carga sc
            WHERE sc.id_entidad_federativa = @IdEntidadFederativa
              AND sc.anio_semana = @AnioSemana
              AND sc.numero_semana = @NumeroSemana
              AND sc.activo = 1
              AND
              (
                  sc.tipo_carga = N'CARGA_INICIAL'
                  AND sc.estado = N'CONFIRMADO'
                  OR
                  sc.tipo_carga = N'ACTUALIZACION'
                  AND sc.estado = N'CONFIRMADO_ACTUALIZACION'
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
        INNER JOIN cargas_semana cs
            ON cs.id_semanal_carga = v.id_semanal_carga
        INNER JOIN dbo.semanal_delito d
            ON d.id_semanal_delito = v.id_semanal_delito
           AND d.activo = 1
        INNER JOIN dbo.semanal_carpeta_investigacion ci
            ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
           AND ci.activo = 1
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
        WHERE v.activo = 1
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

        var filas = await connection.QueryAsync(sql, new
        {
            referencia.IdEntidadFederativa,
            referencia.AnioSemana,
            referencia.NumeroSemana
        });

        return filas
            .Select(fila => ((IDictionary<string, object?>)fila)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase))
            .Cast<IDictionary<string, object?>>()
            .ToList();
    }
}