using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Globalization;

namespace SIIID2.Api.Repositories;

public class SemanalEnviosRepository : ISemanalEnviosRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SemanalEnviosRepository(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    public async Task<List<SemanalEnvioItem>> ObtenerEnviosAsync(bool esSuperUsuario, int idUsuarioConsulta, int? idEntidadFederativa, int? idUsuarioCarga, int? anioCorte, int? mesCorte, string? tipoCarga, string? estado)
    {
        const string sql = @"
    WITH bloques_operacion AS
    (
        SELECT
            sc.id_semanal_carga,
            sc.id_entidad_federativa,
            sc.id_usuario_carga,
            bloque.anio_corte,
            bloque.mes_corte,
            COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) AS fecha_movimiento
        FROM dbo.semanal_carga sc
        INNER JOIN dbo.semanal_carga_bloque bloque
            ON bloque.id_semanal_carga = sc.id_semanal_carga
           AND bloque.activo = 1
        WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND
          (
              sc.estado NOT LIKE N'RECHAZADO%'
              OR sc.estado = N'RECHAZADO_ADMIN'
          )
          AND sc.activo = 1

        UNION ALL

        SELECT
            sc.id_semanal_carga,
            sc.id_entidad_federativa,
            sc.id_usuario_carga,
            sc.anio_corte,
            sc.mes_corte,
            COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga)
        FROM dbo.semanal_carga sc
        WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND
          (
              sc.estado NOT LIKE N'RECHAZADO%'
              OR sc.estado = N'RECHAZADO_ADMIN'
          )
          AND sc.activo = 1
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
                    bloque.anio_corte,
                    bloque.mes_corte
                ORDER BY
                    bloque.fecha_movimiento DESC,
                    bloque.id_semanal_carga DESC
            ) AS rn
        FROM bloques_operacion bloque
    ),
    cargas_visibles AS
    (
        SELECT DISTINCT visible.id_semanal_carga
        FROM ultimo_visible visible
        WHERE visible.rn = 1
          AND (@AnioCorte IS NULL OR visible.anio_corte = @AnioCorte)
          AND (@MesCorte IS NULL OR visible.mes_corte = @MesCorte)
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
    WHERE (@EsSuperUsuario = 1 OR sc.id_usuario_carga = @IdUsuarioConsulta)
      AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
      AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
      AND (@TipoCarga IS NULL OR sc.tipo_carga = @TipoCarga)
      AND (@Estado IS NULL OR sc.estado = @Estado)
    ORDER BY
        ef.nombre,
        u.usuario,
        COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) DESC,
        sc.id_semanal_carga DESC;
    ";

        const string sqlBloques = @"
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
    WHERE bloque.id_semanal_carga IN @Ids
      AND bloque.activo = 1
    ORDER BY
        bloque.id_semanal_carga,
        bloque.fecha_inicio_semana,
        bloque.anio_corte,
        bloque.mes_corte;
    ";

        const string sqlPeriodos = @"
    WITH bloques_operacion AS
    (
        SELECT
            sc.id_semanal_carga,
            sc.id_entidad_federativa,
            sc.id_usuario_carga,
            bloque.anio_corte,
            bloque.mes_corte,
            COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) AS fecha_movimiento
        FROM dbo.semanal_carga sc
        INNER JOIN dbo.semanal_carga_bloque bloque
            ON bloque.id_semanal_carga = sc.id_semanal_carga
           AND bloque.activo = 1
        WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND
          (
              sc.estado NOT LIKE N'RECHAZADO%'
              OR sc.estado = N'RECHAZADO_ADMIN'
          )
          AND sc.activo = 1

        UNION ALL

        SELECT
            sc.id_semanal_carga,
            sc.id_entidad_federativa,
            sc.id_usuario_carga,
            sc.anio_corte,
            sc.mes_corte,
            COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga)
        FROM dbo.semanal_carga sc
        WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND
          (
              sc.estado NOT LIKE N'RECHAZADO%'
              OR sc.estado = N'RECHAZADO_ADMIN'
          )
          AND sc.activo = 1
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
                    bloque.anio_corte,
                    bloque.mes_corte
                ORDER BY
                    bloque.fecha_movimiento DESC,
                    bloque.id_semanal_carga DESC
            ) AS rn
        FROM bloques_operacion bloque
    )
    SELECT
        visible.id_semanal_carga AS IdSemanalCarga,
        visible.anio_corte AS AnioCorte,
        visible.mes_corte AS MesCorte
    FROM ultimo_visible visible
    WHERE visible.rn = 1
      AND visible.id_semanal_carga IN @Ids
    ORDER BY
        visible.id_semanal_carga,
        visible.anio_corte,
        visible.mes_corte;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var cargas = (await connection.QueryAsync<SemanalEnvioItem>(sql, new
        {
            EsSuperUsuario = esSuperUsuario,
            IdUsuarioConsulta = idUsuarioConsulta,
            IdEntidadFederativa = esSuperUsuario ? idEntidadFederativa : null,
            IdUsuarioCarga = esSuperUsuario ? idUsuarioCarga : null,
            AnioCorte = anioCorte,
            MesCorte = mesCorte,
            TipoCarga = tipoCarga,
            Estado = estado
        })).ToList();

        if (cargas.Count == 0) return cargas;

        var ids = cargas.Select(x => x.IdSemanalCarga).Distinct().ToArray();

        var bloques = (await connection.QueryAsync<SemanalEnvioBloqueItem>(sqlBloques, new { Ids = ids })).ToList();
        var bloquesPorCarga = bloques.GroupBy(x => x.IdSemanalCarga).ToDictionary(x => x.Key, x => x.ToList());

        var periodos = (await connection.QueryAsync<SemanalEnvioPeriodoItem>(sqlPeriodos, new { Ids = ids })).ToList();
        var periodosPorCarga = periodos.GroupBy(x => x.IdSemanalCarga).ToDictionary(x => x.Key, x => x.ToList());

        foreach (var carga in cargas)
        {
            if (bloquesPorCarga.TryGetValue(carga.IdSemanalCarga, out var bloquesCarga)) carga.Bloques = bloquesCarga;
            if (periodosPorCarga.TryGetValue(carga.IdSemanalCarga, out var periodosCarga)) carga.Periodos = periodosCarga;
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

    public async Task<List<SemanalReporteCargaItem>> ObtenerReporteCargasAsync(int? idEntidadFederativa, int? idUsuarioCarga, int? anioCorte, int? mesCorte)
    {
        const string sql = @"
    WITH bloques_carga AS
    (
        SELECT
            sc.id_semanal_carga,
            sc.id_entidad_federativa,
            sc.id_usuario_carga,
            bloque.anio_corte,
            bloque.mes_corte,
            sc.codigo_referencia,
            sc.tipo_carga,
            sc.estado,
            sc.fecha_carga,
            sc.fecha_validacion,
            sc.fecha_confirmacion
        FROM dbo.semanal_carga sc
        INNER JOIN dbo.semanal_carga_bloque bloque
            ON bloque.id_semanal_carga = sc.id_semanal_carga
           AND bloque.activo = 1
        WHERE sc.activo = 1
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.id_entidad_federativa IS NOT NULL

        UNION ALL

        SELECT
            sc.id_semanal_carga,
            sc.id_entidad_federativa,
            sc.id_usuario_carga,
            sc.anio_corte,
            sc.mes_corte,
            sc.codigo_referencia,
            sc.tipo_carga,
            sc.estado,
            sc.fecha_carga,
            sc.fecha_validacion,
            sc.fecha_confirmacion
        FROM dbo.semanal_carga sc
        WHERE sc.activo = 1
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.id_entidad_federativa IS NOT NULL
          AND NOT EXISTS
          (
              SELECT 1
              FROM dbo.semanal_carga_bloque bloque
              WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                AND bloque.activo = 1
          )
    ),
    periodos_disponibles AS
    (
        SELECT DISTINCT
            bloque.anio_corte,
            bloque.mes_corte
        FROM bloques_carga bloque
        WHERE (@AnioCorte IS NULL OR bloque.anio_corte = @AnioCorte)
          AND (@MesCorte IS NULL OR bloque.mes_corte = @MesCorte)
    ),
    usuarios_esperados AS
    (
        SELECT DISTINCT
            usuario.id_entidad_federativa,
            entidad.nombre AS entidad_federativa,
            entidad.clave AS clave_entidad,
            usuario.id_usuario AS id_usuario_carga,
            usuario.usuario AS usuario_carga,
            COALESCE
            (
                NULLIF
                (
                    LTRIM(RTRIM(CONCAT
                    (
                        usuario.nombre,
                        N' ',
                        usuario.primer_apellido,
                        CASE
                            WHEN NULLIF(usuario.segundo_apellido, N'') IS NULL THEN N''
                            ELSE CONCAT(N' ', usuario.segundo_apellido)
                        END
                    ))),
                    N''
                ),
                usuario.usuario
            ) AS nombre_usuario_carga
        FROM dbo.usuario usuario
        INNER JOIN dbo.roles rol
            ON rol.id_rol = usuario.id_rol
           AND rol.activo = 1
           AND rol.rol = N'ENLACE_ESTATAL'
        INNER JOIN dbo.usuario_modulo usuario_modulo
            ON usuario_modulo.id_usuario = usuario.id_usuario
           AND usuario_modulo.habilitado = 1
           AND usuario_modulo.activo = 1
        INNER JOIN dbo.catalogo_modulo modulo
            ON modulo.id_modulo = usuario_modulo.id_modulo
           AND modulo.clave = N'SEMANAL'
           AND modulo.activo = 1
        INNER JOIN dbo.catalogo_entidad_federativa entidad
            ON entidad.id_entidad_federativa = usuario.id_entidad_federativa
           AND entidad.activo = 1
        WHERE usuario.activo = 1
          AND (@IdEntidadFederativa IS NULL OR usuario.id_entidad_federativa = @IdEntidadFederativa)
          AND (@IdUsuarioCarga IS NULL OR usuario.id_usuario = @IdUsuarioCarga)
    ),
    base AS
    (
        SELECT
            usuario.id_entidad_federativa,
            usuario.entidad_federativa,
            usuario.clave_entidad,
            usuario.id_usuario_carga,
            usuario.usuario_carga,
            usuario.nombre_usuario_carga,
            periodo.anio_corte,
            periodo.mes_corte
        FROM usuarios_esperados usuario
        CROSS JOIN periodos_disponibles periodo

        UNION

        SELECT DISTINCT
            bloque.id_entidad_federativa,
            entidad.nombre,
            entidad.clave,
            bloque.id_usuario_carga,
            usuario.usuario,
            COALESCE
            (
                NULLIF
                (
                    LTRIM(RTRIM(CONCAT
                    (
                        usuario.nombre,
                        N' ',
                        usuario.primer_apellido,
                        CASE
                            WHEN NULLIF(usuario.segundo_apellido, N'') IS NULL THEN N''
                            ELSE CONCAT(N' ', usuario.segundo_apellido)
                        END
                    ))),
                    N''
                ),
                usuario.usuario
            ),
            bloque.anio_corte,
            bloque.mes_corte
        FROM bloques_carga bloque
        INNER JOIN dbo.usuario usuario
            ON usuario.id_usuario = bloque.id_usuario_carga
        INNER JOIN dbo.catalogo_entidad_federativa entidad
            ON entidad.id_entidad_federativa = bloque.id_entidad_federativa
           AND entidad.activo = 1
        WHERE (@IdEntidadFederativa IS NULL OR bloque.id_entidad_federativa = @IdEntidadFederativa)
          AND (@IdUsuarioCarga IS NULL OR bloque.id_usuario_carga = @IdUsuarioCarga)
          AND (@AnioCorte IS NULL OR bloque.anio_corte = @AnioCorte)
          AND (@MesCorte IS NULL OR bloque.mes_corte = @MesCorte)
    ),
    periodos AS
    (
        SELECT
            base.id_entidad_federativa,
            base.entidad_federativa,
            base.clave_entidad,
            base.id_usuario_carga,
            base.usuario_carga,
            base.nombre_usuario_carga,
            base.anio_corte,
            base.mes_corte,
            COUNT(DISTINCT bloque.id_semanal_carga) AS intentos
        FROM base
        LEFT JOIN bloques_carga bloque
            ON bloque.id_entidad_federativa = base.id_entidad_federativa
           AND bloque.id_usuario_carga = base.id_usuario_carga
           AND bloque.anio_corte = base.anio_corte
           AND bloque.mes_corte = base.mes_corte
        GROUP BY
            base.id_entidad_federativa,
            base.entidad_federativa,
            base.clave_entidad,
            base.id_usuario_carga,
            base.usuario_carga,
            base.nombre_usuario_carga,
            base.anio_corte,
            base.mes_corte
    )
    SELECT
        periodo.id_entidad_federativa AS IdEntidadFederativa,
        periodo.entidad_federativa AS EntidadFederativa,
        periodo.clave_entidad AS ClaveEntidad,
        periodo.id_usuario_carga AS IdUsuarioCarga,
        periodo.usuario_carga AS UsuarioCarga,
        periodo.nombre_usuario_carga AS NombreUsuarioCarga,
        periodo.anio_corte AS AnioCorte,
        periodo.mes_corte AS MesCorte,
        periodo.intentos AS Intentos,
        ultimo.codigo_referencia AS UltimoIntento,
        ultimo.tipo_carga AS TipoCargaUltimoIntento,
        ultimo.estado AS EstatusUltimoIntento,
        ultimo.fecha_carga_actualizacion AS FechaCargaActualizacion,
        ultimo.fecha_aprobacion AS FechaAprobacion,
        exitosa.fecha_carga_exitosa AS FechaCargaExitosa
    FROM periodos periodo
    OUTER APPLY
    (
        SELECT TOP (1)
            bloque.codigo_referencia,
            bloque.tipo_carga,
            bloque.estado,
            CASE
                WHEN bloque.estado IN
                (
                    N'VALIDADO_PENDIENTE',
                    N'VALIDADO_PENDIENTE_ACTUALIZACION',
                    N'PENDIENTE_APROBACION',
                    N'CONFIRMADO',
                    N'CONFIRMADO_ACTUALIZACION',
                    N'RECHAZADO_ADMIN'
                )
                THEN bloque.fecha_validacion
                ELSE NULL
            END AS fecha_carga_actualizacion,
            CASE
                WHEN bloque.estado IN
                (
                    N'CONFIRMADO',
                    N'CONFIRMADO_ACTUALIZACION'
                )
                THEN bloque.fecha_confirmacion
                ELSE NULL
            END AS fecha_aprobacion
        FROM bloques_carga bloque
        WHERE bloque.id_entidad_federativa = periodo.id_entidad_federativa
          AND bloque.id_usuario_carga = periodo.id_usuario_carga
          AND bloque.anio_corte = periodo.anio_corte
          AND bloque.mes_corte = periodo.mes_corte
        ORDER BY
            COALESCE(bloque.fecha_confirmacion, bloque.fecha_validacion, bloque.fecha_carga) DESC,
            bloque.id_semanal_carga DESC
    ) ultimo
    OUTER APPLY
    (
        SELECT MIN(bloque.fecha_validacion) AS fecha_carga_exitosa
        FROM bloques_carga bloque
        WHERE bloque.id_entidad_federativa = periodo.id_entidad_federativa
          AND bloque.id_usuario_carga = periodo.id_usuario_carga
          AND bloque.anio_corte = periodo.anio_corte
          AND bloque.mes_corte = periodo.mes_corte
          AND bloque.estado IN
          (
              N'VALIDADO_PENDIENTE',
              N'VALIDADO_PENDIENTE_ACTUALIZACION',
              N'PENDIENTE_APROBACION',
              N'CONFIRMADO',
              N'CONFIRMADO_ACTUALIZACION'
          )
    ) exitosa
    ORDER BY
        periodo.anio_corte DESC,
        periodo.mes_corte DESC,
        periodo.entidad_federativa,
        periodo.usuario_carga;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var registros = (await connection.QueryAsync<SemanalReporteCargaItem>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga,
            AnioCorte = anioCorte,
            MesCorte = mesCorte
        })).ToList();

        var cultura = CultureInfo.GetCultureInfo("es-MX");

        foreach (var registro in registros)
        {
            var nombreMes = cultura.TextInfo.ToTitleCase(cultura.DateTimeFormat.GetMonthName(registro.MesCorte));

            registro.Periodo = $"{nombreMes} {registro.AnioCorte}";
            registro.FechaCargaActualizacionTexto = registro.FechaCargaActualizacion.HasValue ? registro.FechaCargaActualizacion.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
            registro.FechaAprobacionTexto = registro.FechaAprobacion.HasValue ? registro.FechaAprobacion.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
        }

        return registros;
    }

    public async Task<List<SemanalReportePreliminarEntidadItem>> ObtenerEntidadesReportePreliminarAsync(int anioCorte, int mesCorte, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        const string sql = @"
        SELECT DISTINCT
            sc.id_entidad_federativa AS IdEntidadFederativa,
            ef.nombre AS EntidadFederativa
        FROM dbo.semanal_delito d
        INNER JOIN dbo.semanal_carpeta_investigacion ci
            ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = d.id_semanal_carga
        INNER JOIN dbo.catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = sc.id_entidad_federativa
           AND ef.activo = 1
        WHERE ci.fecha_inicio >= @FechaInicio
          AND ci.fecha_inicio < @FechaFinExclusiva
          AND d.activo = 1
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
          AND sc.activo = 1
          AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
          AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
        ORDER BY
            ef.nombre;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        var fechaInicio = new DateTime(anioCorte, mesCorte, 1);

        return (await connection.QueryAsync<SemanalReportePreliminarEntidadItem>(sql, new
        {
            FechaInicio = fechaInicio,
            FechaFinExclusiva = fechaInicio.AddMonths(1),
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga
        })).ToList();
    }

    public async Task<List<SemanalReportePreliminarDelitoItem>> ObtenerDelitosReportePreliminarAsync(int anioCorte, int mesCorte, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        const string sql = @"
        SELECT DISTINCT
            cd.id_delito AS IdDelito,
            cd.clave2 AS ClaveDelito,
            cd.delito AS Delito
        FROM dbo.semanal_delito d
        INNER JOIN dbo.semanal_carpeta_investigacion ci
            ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = d.id_semanal_carga
        INNER JOIN dbo.catalogo_delito cd
            ON cd.id_delito = d.id_catalogo_delito
           AND cd.activo = 1
        WHERE ci.fecha_inicio >= @FechaInicio
          AND ci.fecha_inicio < @FechaFinExclusiva
          AND d.activo = 1
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
          AND sc.activo = 1
          AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
          AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
        ORDER BY
            cd.clave2,
            cd.delito;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        var fechaInicio = new DateTime(anioCorte, mesCorte, 1);

        return (await connection.QueryAsync<SemanalReportePreliminarDelitoItem>(sql, new
        {
            FechaInicio = fechaInicio,
            FechaFinExclusiva = fechaInicio.AddMonths(1),
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga
        })).ToList();
    }

    public Task<List<IDictionary<string, object?>>> ObtenerCarpetasReportePreliminarAsync(int anioCorte, int mesCorte, int idDelito, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        const string sql = @"
        SELECT
            ef.nombre AS [Nombre entidad],
            ci.identificador_carpeta_fiscalia AS id_ci,
            ci.nomenclatura_carpeta_fiscalia AS ntra_ci,
            ISNULL(CONVERT(varchar(10), ci.fecha_inicio, 103), '') AS fha_de_ini,
            CASE
                WHEN CONVERT(time, ci.fecha_inicio) = '00:00:00' THEN ''
                ELSE CONVERT(varchar(8), ci.fecha_inicio, 108)
            END AS hra_de_ini,
            ci.resumen_hechos AS rmen_de_hchos
        FROM dbo.semanal_carpeta_investigacion ci
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = ci.id_semanal_carga
        INNER JOIN dbo.catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = sc.id_entidad_federativa
           AND ef.activo = 1
        WHERE ci.fecha_inicio >= @FechaInicio
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
        ORDER BY
            ef.nombre,
            ci.identificador_carpeta_fiscalia;
        ";

        return QueryDictionaryReportePreliminarAsync(sql, anioCorte, mesCorte, idDelito, idEntidadFederativa, idUsuarioCarga);
    }

    public Task<List<IDictionary<string, object?>>> ObtenerDelitosReportePreliminarAsync(int anioCorte, int mesCorte, int idDelito, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        const string sql = @"
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
            CONVERT(varchar(10), d.id_entidad_federativa) AS id_ent_hchos,
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
        INNER JOIN dbo.catalogo_municipio mun
            ON mun.id_municipio = d.id_municipio
           AND mun.id_entidad_federativa = d.id_entidad_federativa
           AND mun.activo = 1
        LEFT JOIN dbo.catalogo_codigo_postal cp
            ON cp.id_codigo_postal = d.id_codigo_postal
        WHERE ci.fecha_inicio >= @FechaInicio
          AND ci.fecha_inicio < @FechaFinExclusiva
          AND d.id_catalogo_delito = @IdDelito
          AND d.activo = 1
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
          AND sc.activo = 1
          AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
          AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
        ORDER BY
            ef.nombre,
            ci.identificador_carpeta_fiscalia,
            d.identificador_delito_fiscalia;
        ";

        return QueryDictionaryReportePreliminarAsync(sql, anioCorte, mesCorte, idDelito, idEntidadFederativa, idUsuarioCarga);
    }

    public Task<List<IDictionary<string, object?>>> ObtenerVictimasReportePreliminarAsync(int anioCorte, int mesCorte, int idDelito, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        const string sql = @"
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
        WHERE ci.fecha_inicio >= @FechaInicio
          AND ci.fecha_inicio < @FechaFinExclusiva
          AND d.id_catalogo_delito = @IdDelito
          AND v.activo = 1
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
          AND sc.activo = 1
          AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
          AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
        ORDER BY
            ef.nombre,
            ci.identificador_carpeta_fiscalia,
            d.identificador_delito_fiscalia,
            v.identificador_victima_fiscalia;
        ";

        return QueryDictionaryReportePreliminarAsync(sql, anioCorte, mesCorte, idDelito, idEntidadFederativa, idUsuarioCarga);
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryReportePreliminarAsync(string sql, int anioCorte, int mesCorte, int idDelito, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        using var connection = _dbConnectionFactory.CrearConexion();
        var fechaInicio = new DateTime(anioCorte, mesCorte, 1);

        var filas = await connection.QueryAsync(sql, new
        {
            FechaInicio = fechaInicio,
            FechaFinExclusiva = fechaInicio.AddMonths(1),
            IdDelito = idDelito,
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga
        }, commandTimeout: 300);

        return filas.Select(fila => ((IDictionary<string, object?>)fila).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)).Cast<IDictionary<string, object?>>().ToList();
    }

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
            ci.resumen_hechos AS rmen_de_hchos
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

        var filas = await connection.QueryAsync(sql, new
        {
            referencia.IdSemanalCarga,
            referencia.IdEntidadFederativa
        });

        return filas.Select(fila => ((IDictionary<string, object?>)fila).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)).Cast<IDictionary<string, object?>>().ToList();
    }
}