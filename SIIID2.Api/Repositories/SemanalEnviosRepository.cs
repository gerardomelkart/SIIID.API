using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Globalization;

namespace SIIID2.Api.Repositories;

// Coordinación y dependencias compartidas; las operaciones están separadas en archivos parciales.
public partial class SemanalEnviosRepository : ISemanalEnviosRepository
{
    private const string SqlPendientesReportePreliminar = @"
        periodos_carga AS
        (
            SELECT
                sc.id_semanal_carga,
                sc.id_entidad_federativa,
                sc.id_usuario_carga,
                COALESCE(bloque.id_delito, sc.id_delito) AS id_delito,
                bloque.anio_corte,
                bloque.mes_corte,
                bloque.fecha_inicio_tramo,
                bloque.fecha_fin_tramo,
                sc.fecha_validacion
            FROM dbo.semanal_carga sc
            INNER JOIN dbo.semanal_carga_bloque bloque
                ON bloque.id_semanal_carga = sc.id_semanal_carga
               AND bloque.activo = 1
            WHERE sc.activo = 1
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado = N'PENDIENTE_APROBACION'
              AND bloque.anio_corte = @AnioCorte
              AND bloque.mes_corte = @MesCorte
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)

            UNION ALL

            SELECT
                sc.id_semanal_carga,
                sc.id_entidad_federativa,
                sc.id_usuario_carga,
                sc.id_delito,
                sc.anio_corte,
                sc.mes_corte,
                sc.fecha_inicio_tramo,
                sc.fecha_fin_tramo,
                sc.fecha_validacion
            FROM dbo.semanal_carga sc
            WHERE sc.activo = 1
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado = N'PENDIENTE_APROBACION'
              AND sc.anio_corte = @AnioCorte
              AND sc.mes_corte = @MesCorte
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
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
                periodo.id_delito,
                periodo.anio_corte,
                periodo.mes_corte,
                periodo.fecha_inicio_tramo,
                periodo.fecha_fin_tramo,
                ROW_NUMBER() OVER
                (
                    PARTITION BY
                        periodo.id_entidad_federativa,
                        periodo.id_usuario_carga,
                        periodo.id_delito,
                        periodo.anio_corte,
                        periodo.mes_corte,
                        periodo.fecha_inicio_tramo,
                        periodo.fecha_fin_tramo
                    ORDER BY
                        periodo.fecha_validacion DESC,
                        periodo.id_semanal_carga DESC
                ) AS rn
            FROM periodos_carga periodo
            WHERE periodo.id_delito IS NOT NULL
        ),
        pendientes AS
        (
            SELECT
                id_semanal_carga,
                id_entidad_federativa,
                id_usuario_carga,
                id_delito,
                anio_corte,
                mes_corte,
                fecha_inicio_tramo,
                fecha_fin_tramo
            FROM pendientes_rankeadas
            WHERE rn = 1
        )";

    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SemanalEnviosRepository(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    public async Task<SemanalEnviosOpcionesResponse> ObtenerOpcionesEnviosAsync(bool esSuperUsuario, int idUsuarioConsulta, int? idEntidadFederativa = null)
    {
        const string sql = @"
        SET NOCOUNT ON;

        SELECT DISTINCT
            fuente.anio_corte,
            fuente.mes_corte,
            fuente.anio_semana,
            fuente.numero_semana,
            fuente.fecha_inicio_semana,
            fuente.fecha_fin_semana
        INTO #Opciones
        FROM
        (
            SELECT
                bloque.anio_corte,
                bloque.mes_corte,
                bloque.anio_semana,
                bloque.numero_semana,
                bloque.fecha_inicio_semana,
                bloque.fecha_fin_semana
            FROM dbo.semanal_carga sc
            INNER JOIN dbo.semanal_carga_bloque bloque
                ON bloque.id_semanal_carga = sc.id_semanal_carga
               AND bloque.activo = 1
            WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND (sc.estado NOT LIKE N'RECHAZADO%' OR sc.estado = N'RECHAZADO_ADMIN')
              AND sc.activo = 1
              AND (@EsSuperUsuario = 1 OR sc.id_usuario_carga = @IdUsuarioConsulta)
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)

            UNION ALL

            SELECT
                sc.anio_corte,
                sc.mes_corte,
                sc.anio_semana,
                sc.numero_semana,
                sc.fecha_inicio_semana,
                sc.fecha_fin_semana
            FROM dbo.semanal_carga sc
            WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND (sc.estado NOT LIKE N'RECHAZADO%' OR sc.estado = N'RECHAZADO_ADMIN')
              AND sc.activo = 1
              AND (@EsSuperUsuario = 1 OR sc.id_usuario_carga = @IdUsuarioConsulta)
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.semanal_carga_bloque bloque
                  WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                    AND bloque.activo = 1
              )
        ) fuente;

        CREATE CLUSTERED INDEX IX_Opciones
        ON #Opciones(anio_corte, mes_corte, fecha_inicio_semana);

        SELECT DISTINCT
            anio_corte AS AnioCorte,
            mes_corte AS MesCorte
        FROM #Opciones
        WHERE mes_corte BETWEEN 1 AND 12
          AND anio_corte BETWEEN 2000 AND 2100
        ORDER BY anio_corte DESC, mes_corte DESC;

        SELECT DISTINCT
            anio_corte AS AnioCorte,
            mes_corte AS MesCorte,
            anio_semana AS AnioSemana,
            numero_semana AS NumeroSemana,
            fecha_inicio_semana AS FechaInicioSemana,
            fecha_fin_semana AS FechaFinSemana
        FROM #Opciones
        ORDER BY anio_corte DESC, mes_corte DESC, fecha_inicio_semana DESC;

        DROP TABLE #Opciones;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();
        using var resultados = await connection.QueryMultipleAsync(sql, new
        {
            EsSuperUsuario = esSuperUsuario,
            IdUsuarioConsulta = idUsuarioConsulta,
            IdEntidadFederativa = idEntidadFederativa
        });

        return new SemanalEnviosOpcionesResponse
        {
            Periodos = (await resultados.ReadAsync<SemanalEnvioPeriodoOpcionItem>()).ToList(),
            Semanas = (await resultados.ReadAsync<SemanalEnvioSemanaOpcionItem>()).ToList()
        };
    }

    public async Task<List<SemanalEnvioItem>> ObtenerEnviosAsync(bool esSuperUsuario, int idUsuarioConsulta, int? idEntidadFederativa, int? idUsuarioCarga, int? anioCorte, int? mesCorte, int? anioSemana, int? numeroSemana, string? tipoCarga, string? estado)
    {
        const string sql = @"
    SET NOCOUNT ON;

    WITH bloques_operacion AS
    (
        SELECT
            sc.id_semanal_carga,
            sc.id_entidad_federativa,
            sc.id_usuario_carga,
            sc.id_delito,
            bloque.fecha_inicio_semana,
            bloque.anio_corte,
            bloque.mes_corte,
            COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) AS fecha_movimiento
        FROM dbo.semanal_carga sc
        INNER JOIN dbo.semanal_carga_bloque bloque
            ON bloque.id_semanal_carga = sc.id_semanal_carga
           AND bloque.activo = 1
        WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND (sc.estado NOT LIKE N'RECHAZADO%' OR sc.estado = N'RECHAZADO_ADMIN')
          AND sc.activo = 1
          AND (@EsSuperUsuario = 1 OR sc.id_usuario_carga = @IdUsuarioConsulta)
          AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
          AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
            AND (@AnioCorte IS NULL OR bloque.anio_corte = @AnioCorte)
            AND (@MesCorte IS NULL OR bloque.mes_corte = @MesCorte)
            AND (@AnioSemana IS NULL OR bloque.anio_semana = @AnioSemana)
            AND (@NumeroSemana IS NULL OR bloque.numero_semana = @NumeroSemana)

        UNION ALL

        SELECT
            sc.id_semanal_carga,
            sc.id_entidad_federativa,
            sc.id_usuario_carga,
            sc.id_delito,
            sc.fecha_inicio_semana,
            sc.anio_corte,
            sc.mes_corte,
            COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga)
        FROM dbo.semanal_carga sc
        WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND (sc.estado NOT LIKE N'RECHAZADO%' OR sc.estado = N'RECHAZADO_ADMIN')
          AND sc.activo = 1
          AND (@EsSuperUsuario = 1 OR sc.id_usuario_carga = @IdUsuarioConsulta)
          AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
          AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
            AND (@AnioCorte IS NULL OR sc.anio_corte = @AnioCorte)
            AND (@MesCorte IS NULL OR sc.mes_corte = @MesCorte)
            AND (@AnioSemana IS NULL OR sc.anio_semana = @AnioSemana)
            AND (@NumeroSemana IS NULL OR sc.numero_semana = @NumeroSemana)
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.semanal_carga_bloque bloque
              WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                AND bloque.activo = 1
          )
    ),
    ultimo_visible AS
    (
        SELECT
            bloque.id_semanal_carga,
            bloque.anio_corte,
            bloque.mes_corte,
            ROW_NUMBER() OVER
            (
                PARTITION BY
                    bloque.id_entidad_federativa,
                    bloque.id_usuario_carga,
                    bloque.id_delito,
                    bloque.fecha_inicio_semana,
                    bloque.anio_corte,
                    bloque.mes_corte
                ORDER BY bloque.fecha_movimiento DESC, bloque.id_semanal_carga DESC
            ) AS rn
        FROM bloques_operacion bloque
    )
    SELECT
        visible.id_semanal_carga,
        visible.anio_corte,
        visible.mes_corte
    INTO #PeriodosVisibles
    FROM ultimo_visible visible
    WHERE visible.rn = 1
    OPTION (RECOMPILE);

    CREATE CLUSTERED INDEX IX_PeriodosVisibles ON #PeriodosVisibles(id_semanal_carga, anio_corte, mes_corte);

    SELECT DISTINCT periodo.id_semanal_carga
    INTO #CargasVisibles
    FROM #PeriodosVisibles periodo
    INNER JOIN dbo.semanal_carga sc ON sc.id_semanal_carga = periodo.id_semanal_carga
    WHERE (@AnioCorte IS NULL OR periodo.anio_corte = @AnioCorte)
      AND (@MesCorte IS NULL OR periodo.mes_corte = @MesCorte)
      AND (@TipoCarga IS NULL OR sc.tipo_carga = @TipoCarga)
      AND (@Estado IS NULL OR sc.estado = @Estado)
    OPTION (RECOMPILE);

    CREATE UNIQUE CLUSTERED INDEX IX_CargasVisibles ON #CargasVisibles(id_semanal_carga);

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
                    CASE WHEN NULLIF(u.segundo_apellido, N'') IS NULL THEN N'' ELSE CONCAT(N' ', u.segundo_apellido) END
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
        CASE WHEN sc.estado = N'RECHAZADO_ADMIN' THEN sc.mensaje_error ELSE NULL END AS MotivoRechazo,
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
    FROM #CargasVisibles visible
    INNER JOIN dbo.semanal_carga sc ON sc.id_semanal_carga = visible.id_semanal_carga
    INNER JOIN dbo.usuario u ON u.id_usuario = sc.id_usuario_carga
    LEFT JOIN dbo.catalogo_entidad_federativa ef ON ef.id_entidad_federativa = sc.id_entidad_federativa
    LEFT JOIN dbo.usuario usuario_resolucion ON usuario_resolucion.id_usuario = sc.id_usuario_confirmacion
    ORDER BY ef.nombre, u.usuario, COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) DESC, sc.id_semanal_carga DESC;

    SELECT
        bloque.id_semanal_carga AS IdSemanalCarga,
        bloque.anio_semana AS AnioSemana,
        bloque.numero_semana AS NumeroSemana,
        bloque.fecha_inicio_semana AS FechaInicioSemana,
        bloque.fecha_fin_semana AS FechaFinSemana,
        bloque.anio_corte AS AnioCorte,
        bloque.mes_corte AS MesCorte,
        bloque.fecha_inicio_tramo AS FechaInicioTramo,
        bloque.fecha_fin_tramo AS FechaFinTramo,
        bloque.total_carpetas AS TotalCarpetas,
        bloque.total_delitos AS TotalDelitos,
        bloque.total_victimas AS TotalVictimas,
        CONVERT(bit, bloque.reemplaza_informacion) AS ReemplazaInformacion
    FROM dbo.semanal_carga_bloque bloque
    INNER JOIN #CargasVisibles visible ON visible.id_semanal_carga = bloque.id_semanal_carga
    WHERE bloque.activo = 1
  AND (@AnioCorte IS NULL OR bloque.anio_corte = @AnioCorte)
  AND (@MesCorte IS NULL OR bloque.mes_corte = @MesCorte)
  AND (@AnioSemana IS NULL OR bloque.anio_semana = @AnioSemana)
  AND (@NumeroSemana IS NULL OR bloque.numero_semana = @NumeroSemana)
    ORDER BY bloque.id_semanal_carga, bloque.fecha_inicio_semana, bloque.anio_corte, bloque.mes_corte;

    SELECT DISTINCT
        periodo.id_semanal_carga AS IdSemanalCarga,
        periodo.anio_corte AS AnioCorte,
        periodo.mes_corte AS MesCorte
    FROM #PeriodosVisibles periodo
    INNER JOIN #CargasVisibles visible ON visible.id_semanal_carga = periodo.id_semanal_carga
    ORDER BY periodo.id_semanal_carga, periodo.anio_corte, periodo.mes_corte;

    WITH delitos_carga AS
    (
        SELECT
            delito.id_semanal_carga,
            cd.clave2,
            cd.delito
        FROM dbo.semanal_delito delito
        INNER JOIN #CargasVisibles visible ON visible.id_semanal_carga = delito.id_semanal_carga
        INNER JOIN dbo.catalogo_modalidad_delito md ON md.id_modalidad_delito = delito.id_modalidad_delito AND md.activo = 1
        INNER JOIN dbo.catalogo_subtipo_delito sd ON sd.id_subtipo_delito = md.id_subtipo_delito AND sd.activo = 1
        INNER JOIN dbo.catalogo_delito cd ON cd.id_delito = sd.id_delito AND cd.activo = 1
        WHERE delito.activo = 1

        UNION

        SELECT
            delito.id_semanal_carga,
            cd.clave2,
            cd.delito
        FROM dbo.semanal_carga_tmp_delito delito
        INNER JOIN #CargasVisibles visible ON visible.id_semanal_carga = delito.id_semanal_carga
        INNER JOIN dbo.catalogo_modalidad_delito md ON LTRIM(RTRIM(md.clave4)) = LTRIM(RTRIM(delito.clasf_de_dto)) AND md.activo = 1
        INNER JOIN dbo.catalogo_subtipo_delito sd ON sd.id_subtipo_delito = md.id_subtipo_delito AND sd.activo = 1
        INNER JOIN dbo.catalogo_delito cd ON cd.id_delito = sd.id_delito AND cd.activo = 1
        WHERE delito.incluido = 1
          AND delito.activo = 1

        UNION

        SELECT
            carga.id_semanal_carga,
            cd.clave2,
            cd.delito
        FROM dbo.semanal_carga carga
        INNER JOIN #CargasVisibles visible ON visible.id_semanal_carga = carga.id_semanal_carga
        INNER JOIN dbo.catalogo_delito cd ON cd.id_delito = carga.id_delito AND cd.activo = 1
        WHERE carga.total_carpetas_incluidas = 0
          AND carga.total_delitos_incluidos = 0
          AND carga.total_victimas_incluidas = 0
          AND carga.activo = 1
    )
    SELECT
        delito.id_semanal_carga AS IdSemanalCarga,
        delito.delito AS Delito
    FROM delitos_carga delito
    ORDER BY delito.id_semanal_carga, delito.clave2, delito.delito;

    DROP TABLE #CargasVisibles;
    DROP TABLE #PeriodosVisibles;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();
        using var resultados = await connection.QueryMultipleAsync(sql, new
        {
            EsSuperUsuario = esSuperUsuario,
            IdUsuarioConsulta = idUsuarioConsulta,
            IdEntidadFederativa = esSuperUsuario ? idEntidadFederativa : null,
            IdUsuarioCarga = esSuperUsuario ? idUsuarioCarga : null,
            AnioCorte = anioCorte,
            MesCorte = mesCorte,
            AnioSemana = anioSemana,
            NumeroSemana = numeroSemana,
            TipoCarga = tipoCarga,
            Estado = estado
        });

        var cargas = (await resultados.ReadAsync<SemanalEnvioItem>()).ToList();
        var bloques = (await resultados.ReadAsync<SemanalEnvioBloqueItem>()).ToList();
        var periodos = (await resultados.ReadAsync<SemanalEnvioPeriodoItem>()).ToList();
        var delitos = (await resultados.ReadAsync<SemanalCargaDelitoItem>()).ToList();

        if (cargas.Count == 0) return cargas;

        var bloquesPorCarga = bloques.GroupBy(x => x.IdSemanalCarga).ToDictionary(x => x.Key, x => x.ToList());
        var periodosPorCarga = periodos.GroupBy(x => x.IdSemanalCarga).ToDictionary(x => x.Key, x => x.ToList());
        var delitosPorCarga = delitos.GroupBy(x => x.IdSemanalCarga).ToDictionary(x => x.Key, x => x.Select(y => y.Delito).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        foreach (var carga in cargas)
        {
            if (bloquesPorCarga.TryGetValue(carga.IdSemanalCarga, out var bloquesCarga)) carga.Bloques = bloquesCarga;
            if (periodosPorCarga.TryGetValue(carga.IdSemanalCarga, out var periodosCarga)) carga.Periodos = periodosCarga;
            if (delitosPorCarga.TryGetValue(carga.IdSemanalCarga, out var delitosCarga)) carga.Delitos = delitosCarga;
        }

        return cargas;
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
                sc.id_usuario_carga AS IdUsuarioCarga,
                ISNULL(ef.nombre, N'') AS EntidadFederativa,
                sc.anio_semana AS AnioSemana,
                sc.numero_semana AS NumeroSemana,
                sc.anio_corte AS AnioCorte,
                sc.mes_corte AS MesCorte
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

    public async Task<InformeSabanaFirma> ObtenerFirmaSabanaAsync(int anioCorte, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        const string sql = @"
            WITH periodos_carga AS
            (
                SELECT
                    sc.id_semanal_carga,
                    sc.tipo_carga,
                    sc.estado,
                    bloque.anio_corte,
                    bloque.mes_corte,
                    sc.fecha_confirmacion,
                    sc.fecha_validacion
                FROM dbo.semanal_carga sc
                INNER JOIN dbo.semanal_carga_bloque bloque
                    ON bloque.id_semanal_carga = sc.id_semanal_carga
                   AND bloque.activo = 1
                WHERE sc.activo = 1
                  AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
                  AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)

                UNION ALL

                SELECT
                    sc.id_semanal_carga,
                    sc.tipo_carga,
                    sc.estado,
                    sc.anio_corte,
                    sc.mes_corte,
                    sc.fecha_confirmacion,
                    sc.fecha_validacion
                FROM dbo.semanal_carga sc
                WHERE sc.activo = 1
                  AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
                  AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM dbo.semanal_carga_bloque bloque
                      WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                        AND bloque.activo = 1
                  )
            ),
            cargas_relevantes AS
            (
                SELECT DISTINCT
                    periodo.id_semanal_carga,
                    periodo.tipo_carga,
                    periodo.estado,
                    periodo.mes_corte,
                    periodo.fecha_confirmacion,
                    periodo.fecha_validacion
                FROM periodos_carga periodo
                WHERE periodo.anio_corte = @AnioCorte
                  AND
                  (
                      periodo.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                      OR periodo.estado = N'PENDIENTE_APROBACION'
                  )
            )
            SELECT
                ISNULL(MAX(id_semanal_carga), 0) AS UltimoIdCarga,
                CONVERT(bigint, COUNT(DISTINCT CASE
                    WHEN estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                    THEN id_semanal_carga
                END)) AS TotalCargasConfirmadas,
                CONVERT(bigint, COUNT(DISTINCT CASE
                    WHEN estado = N'PENDIENTE_APROBACION'
                    THEN id_semanal_carga
                END)) AS TotalCargasPendientes,
                MAX(mes_corte) AS MesUltimoCorte,
                MAX(COALESCE(fecha_confirmacion, fecha_validacion)) AS UltimaFechaMovimiento
            FROM cargas_relevantes;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QuerySingleAsync<InformeSabanaFirma>(sql, new
        {
            AnioCorte = anioCorte,
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga
        });
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionarySabanaAsync(string sql, int anioCorte, int? idEntidadFederativa, int? idUsuarioCarga, string modoPlano, int mesUltimoCorte)
    {
        using var connection = _dbConnectionFactory.CrearConexion();

        var filas = await connection.QueryAsync(sql, new
        {
            AnioCorte = anioCorte,
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga,
            ModoPlano = modoPlano,
            MesUltimoCorte = mesUltimoCorte
        }, commandTimeout: 300);

        return filas
            .Select(fila => ((IDictionary<string, object?>)fila).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase))
            .Cast<IDictionary<string, object?>>()
            .ToList();
    }
}
