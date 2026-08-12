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
            sc.id_delito,
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


        const string sqlDelitos = @"
    WITH delitos_carga AS
    (
        SELECT
            delito.id_semanal_carga,
            cd.clave2,
            cd.delito
        FROM dbo.semanal_delito delito
        INNER JOIN dbo.catalogo_modalidad_delito md
            ON md.id_modalidad_delito = delito.id_modalidad_delito
           AND md.activo = 1
        INNER JOIN dbo.catalogo_subtipo_delito sd
            ON sd.id_subtipo_delito = md.id_subtipo_delito
           AND sd.activo = 1
        INNER JOIN dbo.catalogo_delito cd
            ON cd.id_delito = sd.id_delito
           AND cd.activo = 1
        WHERE delito.id_semanal_carga IN @Ids
          AND delito.activo = 1

        UNION

        SELECT
            delito.id_semanal_carga,
            cd.clave2,
            cd.delito
        FROM dbo.semanal_carga_tmp_delito delito
        INNER JOIN dbo.catalogo_modalidad_delito md
            ON LTRIM(RTRIM(md.clave4)) = LTRIM(RTRIM(delito.clasf_de_dto))
           AND md.activo = 1
        INNER JOIN dbo.catalogo_subtipo_delito sd
            ON sd.id_subtipo_delito = md.id_subtipo_delito
           AND sd.activo = 1
        INNER JOIN dbo.catalogo_delito cd
            ON cd.id_delito = sd.id_delito
           AND cd.activo = 1
        WHERE delito.id_semanal_carga IN @Ids
          AND delito.incluido = 1
          AND delito.activo = 1
    )
    SELECT
        delito.id_semanal_carga AS IdSemanalCarga,
        delito.delito AS Delito
    FROM delitos_carga delito
    ORDER BY
        delito.id_semanal_carga,
        delito.clave2,
        delito.delito;
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

        var delitos = (await connection.QueryAsync<SemanalCargaDelitoItem>(sqlDelitos, new { Ids = ids })).ToList();
        var delitosPorCarga = delitos
            .GroupBy(x => x.IdSemanalCarga)
            .ToDictionary
            (
                x => x.Key,
                x => x
                    .Select(y => y.Delito)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            );

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

        const string sqlDelitos = @"
    WITH bloques_carga AS
    (
        SELECT
            sc.id_semanal_carga,
            sc.id_entidad_federativa,
            sc.id_usuario_carga,
            bloque.anio_corte,
            bloque.mes_corte
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
            sc.mes_corte
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
    delitos_carga AS
    (
        SELECT
            delito.id_semanal_carga,
            cd.clave2,
            cd.delito
        FROM dbo.semanal_delito delito
        INNER JOIN dbo.catalogo_modalidad_delito md
            ON md.id_modalidad_delito = delito.id_modalidad_delito
           AND md.activo = 1
        INNER JOIN dbo.catalogo_subtipo_delito sd
            ON sd.id_subtipo_delito = md.id_subtipo_delito
           AND sd.activo = 1
        INNER JOIN dbo.catalogo_delito cd
            ON cd.id_delito = sd.id_delito
           AND cd.activo = 1
        WHERE delito.activo = 1

        UNION

        SELECT
            delito.id_semanal_carga,
            cd.clave2,
            cd.delito
        FROM dbo.semanal_carga_tmp_delito delito
        INNER JOIN dbo.catalogo_modalidad_delito md
            ON LTRIM(RTRIM(md.clave4)) = LTRIM(RTRIM(delito.clasf_de_dto))
           AND md.activo = 1
        INNER JOIN dbo.catalogo_subtipo_delito sd
            ON sd.id_subtipo_delito = md.id_subtipo_delito
           AND sd.activo = 1
        INNER JOIN dbo.catalogo_delito cd
            ON cd.id_delito = sd.id_delito
           AND cd.activo = 1
        WHERE delito.incluido = 1
          AND delito.activo = 1
    )
    SELECT DISTINCT
        bloque.id_entidad_federativa AS IdEntidadFederativa,
        bloque.id_usuario_carga AS IdUsuarioCarga,
        bloque.anio_corte AS AnioCorte,
        bloque.mes_corte AS MesCorte,
        delito.clave2 AS ClaveDelito,
        delito.delito AS Delito
    FROM bloques_carga bloque
    INNER JOIN delitos_carga delito
        ON delito.id_semanal_carga = bloque.id_semanal_carga
    WHERE (@IdEntidadFederativa IS NULL OR bloque.id_entidad_federativa = @IdEntidadFederativa)
      AND (@IdUsuarioCarga IS NULL OR bloque.id_usuario_carga = @IdUsuarioCarga)
      AND (@AnioCorte IS NULL OR bloque.anio_corte = @AnioCorte)
      AND (@MesCorte IS NULL OR bloque.mes_corte = @MesCorte)
    ORDER BY
        bloque.id_entidad_federativa,
        bloque.id_usuario_carga,
        bloque.anio_corte,
        bloque.mes_corte,
        delito.clave2,
        delito.delito;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var registros = (await connection.QueryAsync<SemanalReporteCargaItem>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga,
            AnioCorte = anioCorte,
            MesCorte = mesCorte
        })).ToList();

        var delitos = (await connection.QueryAsync<SemanalReporteCargaDelitoItem>(sqlDelitos, new
        {
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga,
            AnioCorte = anioCorte,
            MesCorte = mesCorte
        })).ToList();

        var delitosPorPeriodo = delitos
            .GroupBy(x => new
            {
                x.IdEntidadFederativa,
                x.IdUsuarioCarga,
                x.AnioCorte,
                x.MesCorte
            })
            .ToDictionary
            (
                x => x.Key,
                x => x
                    .Select(y => y.Delito)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            );

        var cultura = CultureInfo.GetCultureInfo("es-MX");

        foreach (var registro in registros)
        {
            var nombreMes = cultura.TextInfo.ToTitleCase(cultura.DateTimeFormat.GetMonthName(registro.MesCorte));

            registro.Periodo = $"{nombreMes} {registro.AnioCorte}";
            registro.FechaCargaActualizacionTexto = registro.FechaCargaActualizacion.HasValue ? registro.FechaCargaActualizacion.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
            registro.FechaAprobacionTexto = registro.FechaAprobacion.HasValue ? registro.FechaAprobacion.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;

            if (delitosPorPeriodo.TryGetValue(new
            {
                registro.IdEntidadFederativa,
                registro.IdUsuarioCarga,
                registro.AnioCorte,
                registro.MesCorte
            }, out var delitosRegistro))
            {
                registro.Delitos = delitosRegistro;
            }
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

    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalDelitosAsync(int anioCorte, int? idEntidadFederativa, int? idUsuarioCarga, string modoPlano, int mesUltimoCorte)
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

        return await QueryDictionarySabanaAsync(sql, anioCorte, idEntidadFederativa, idUsuarioCarga, modoPlano, mesUltimoCorte);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaEstatalVictimasAsync(int anioCorte, int? idEntidadFederativa, int? idUsuarioCarga, string modoPlano, int mesUltimoCorte)
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
            fuente_victimas AS
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
                    d.id_forma_accion,
                    tv.clave AS tipo_victima_clave,
                    sx.clave AS sexo_clave,
                    sx.descripcion AS sexo_descripcion,
                    TRY_CONVERT(int, v.edad) AS edad
                FROM dbo.semanal_victima v
                INNER JOIN dbo.semanal_delito d
                    ON d.id_semanal_delito = v.id_semanal_delito
                   AND d.activo = 1
                INNER JOIN dbo.semanal_carpeta_investigacion ci
                    ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
                   AND ci.activo = 1
                INNER JOIN dbo.semanal_carga sc
                    ON sc.id_semanal_carga = v.id_semanal_carga
                   AND sc.activo = 1
                   AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
                   AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                INNER JOIN dbo.catalogo_tipo_victima tv
                    ON tv.id_tipo_victima = v.id_tipo_victima
                   AND tv.activo = 1
                LEFT JOIN dbo.catalogo_sexo sx
                    ON sx.id_sexo = v.id_sexo
                   AND sx.activo = 1
                WHERE v.activo = 1
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
                    fa.id_forma_accion,
                    tv.clave,
                    sx.clave,
                    sx.descripcion,
                    CASE
                        WHEN TRY_CONVERT(int, NULLIF(v.edad, N'')) = 999 THEN NULL
                        ELSE TRY_CONVERT(int, NULLIF(v.edad, N''))
                    END
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
                INNER JOIN dbo.catalogo_tipo_victima tv
                    ON tv.clave = TRY_CONVERT(tinyint, v.id_tv)
                   AND tv.activo = 1
                LEFT JOIN dbo.catalogo_sexo sx
                    ON sx.clave = TRY_CONVERT(tinyint, NULLIF(v.sexo, N''))
                   AND sx.activo = 1
                WHERE @ModoPlano IN (N'PREVIO', N'MIXTO')
                  AND fecha.fecha_inicio IS NOT NULL
                  AND YEAR(fecha.fecha_inicio) = p.anio_corte
                  AND MONTH(fecha.fecha_inicio) = p.mes_corte
            ),
            sexos AS (
            SELECT 1 AS orden_sexo, 'Hombre' AS sexo
            UNION ALL SELECT 2, 'Mujer'
            UNION ALL SELECT 3, 'No identificado'
        ),
        rangos_edad AS (
            SELECT 1 AS orden_rango, '0 a 12 años' AS rango_edad
            UNION ALL SELECT 2, '13 a 17 años'
            UNION ALL SELECT 3, '18 a 29 años'
            UNION ALL SELECT 4, '30 a 60 años'
            UNION ALL SELECT 5, 'Más de 60 años'
            UNION ALL SELECT 6, 'No especificado'
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
                s.modalidad_delito_sabana,
                sx.orden_sexo,
                sx.sexo,
                re.orden_rango,
                re.rango_edad
            FROM catalogo_entidad_federativa ef
            CROSS JOIN sabana s
            CROSS JOIN sexos sx
            CROSS JOIN rangos_edad re
            WHERE ef.activo = 1
              AND TRY_CONVERT(int, ef.clave) BETWEEN 1 AND 32
              AND (@IdEntidadFederativa IS NULL OR ef.id_entidad_federativa = @IdEntidadFederativa)
        ),
            conteos AS (
                SELECT
                    fv.anio_corte,
                    fv.mes_corte,
                    TRY_CONVERT(int, efh.clave) AS clave_ent,
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana,
                    CASE
                        WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave IN (1, 2, 3) THEN fv.sexo_descripcion
                        ELSE 'No identificado'
                    END AS sexo,
                    CASE
                        WHEN fv.tipo_victima_clave <> 1 THEN 'No especificado'
                        WHEN fv.edad IS NULL THEN 'No especificado'
                        WHEN fv.edad BETWEEN 0 AND 12 THEN '0 a 12 años'
                        WHEN fv.edad BETWEEN 13 AND 17 THEN '13 a 17 años'
                        WHEN fv.edad BETWEEN 18 AND 29 THEN '18 a 29 años'
                        WHEN fv.edad BETWEEN 30 AND 60 THEN '30 a 60 años'
                        WHEN fv.edad BETWEEN 61 AND 120 THEN 'Más de 60 años'
                        ELSE 'No especificado'
                    END AS rango_edad,
                    COUNT(1) AS cantidad_victimas
                FROM fuente_victimas fv
                INNER JOIN catalogo_entidad_federativa efh
                    ON efh.id_entidad_federativa = fv.id_entidad_hechos
                   AND efh.activo = 1
                INNER JOIN catalogo_delito_sabana s
                    ON s.id_modalidad_delito = fv.id_modalidad_delito
                   AND s.id_grado_consumacion = fv.id_grado_consumacion
                   AND s.id_instrumento_comision = fv.id_instrumento_comision
                   AND s.id_forma_accion = fv.id_forma_accion
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
                    fv.anio_corte,
                    fv.mes_corte,
                    TRY_CONVERT(int, efh.clave),
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana,
                    CASE
                        WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave IN (1, 2, 3) THEN fv.sexo_descripcion
                        ELSE 'No identificado'
                    END,
                    CASE
                        WHEN fv.tipo_victima_clave <> 1 THEN 'No especificado'
                        WHEN fv.edad IS NULL THEN 'No especificado'
                        WHEN fv.edad BETWEEN 0 AND 12 THEN '0 a 12 años'
                        WHEN fv.edad BETWEEN 13 AND 17 THEN '13 a 17 años'
                        WHEN fv.edad BETWEEN 18 AND 29 THEN '18 a 29 años'
                        WHEN fv.edad BETWEEN 30 AND 60 THEN '30 a 60 años'
                        WHEN fv.edad BETWEEN 61 AND 120 THEN 'Más de 60 años'
                        ELSE 'No especificado'
                    END
            )
        SELECT
            m.anio_corte AS [Año],
            RIGHT('00' + CONVERT(varchar(2), m.clave_ent), 2) AS [Clave_Ent],
            m.entidad AS [Entidad],
            m.bien_juridico AS [Bien jurídico afectado],
            m.delito_sabana AS [Tipo de delito],
            m.subtipo_delito_sabana AS [Subtipo de delito],
            m.modalidad_delito_sabana AS [Modalidad],
            m.sexo AS [Sexo],
            m.rango_edad AS [Rango de edad],
            ISNULL(SUM(CASE WHEN c.mes_corte = 1 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Enero],
            ISNULL(SUM(CASE WHEN c.mes_corte = 2 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Febrero],
            ISNULL(SUM(CASE WHEN c.mes_corte = 3 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Marzo],
            ISNULL(SUM(CASE WHEN c.mes_corte = 4 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Abril],
            ISNULL(SUM(CASE WHEN c.mes_corte = 5 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Mayo],
            ISNULL(SUM(CASE WHEN c.mes_corte = 6 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Junio],
            ISNULL(SUM(CASE WHEN c.mes_corte = 7 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Julio],
            ISNULL(SUM(CASE WHEN c.mes_corte = 8 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Agosto],
            ISNULL(SUM(CASE WHEN c.mes_corte = 9 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Septiembre],
            ISNULL(SUM(CASE WHEN c.mes_corte = 10 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Octubre],
            ISNULL(SUM(CASE WHEN c.mes_corte = 11 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Noviembre],
            ISNULL(SUM(CASE WHEN c.mes_corte = 12 THEN c.cantidad_victimas ELSE 0 END), 0) AS [Diciembre]
        FROM matriz m
        LEFT JOIN conteos c
            ON c.anio_corte = m.anio_corte
           AND c.clave_ent = m.clave_ent
           AND c.bien_juridico = m.bien_juridico
           AND c.delito_sabana = m.delito_sabana
           AND c.subtipo_delito_sabana = m.subtipo_delito_sabana
           AND c.modalidad_delito_sabana = m.modalidad_delito_sabana
           AND c.sexo = m.sexo
           AND c.rango_edad = m.rango_edad
        GROUP BY
            m.anio_corte,
            m.clave_ent,
            m.entidad,
            m.orden_sabana,
            m.orden_delito,
            m.bien_juridico,
            m.delito_sabana,
            m.subtipo_delito_sabana,
            m.modalidad_delito_sabana,
            m.orden_sexo,
            m.sexo,
            m.orden_rango,
            m.rango_edad
        ORDER BY
            m.clave_ent,
            m.orden_sabana,
            m.orden_delito,
            m.subtipo_delito_sabana,
            m.modalidad_delito_sabana,
            m.orden_sexo,
            m.orden_rango
        OPTION (RECOMPILE);
        ";

        return await QueryDictionarySabanaAsync(sql, anioCorte, idEntidadFederativa, idUsuarioCarga, modoPlano, mesUltimoCorte);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalVictimasAsync(int anioCorte, int? idEntidadFederativa, int? idUsuarioCarga, string modoPlano, int mesUltimoCorte)
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
            fuente_victimas AS
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
                    d.id_forma_accion,
                    tv.clave AS tipo_victima_clave,
                    sx.clave AS sexo_clave,
                    sx.descripcion AS sexo_descripcion,
                    TRY_CONVERT(int, v.edad) AS edad
                FROM dbo.semanal_victima v
                INNER JOIN dbo.semanal_delito d
                    ON d.id_semanal_delito = v.id_semanal_delito
                   AND d.activo = 1
                INNER JOIN dbo.semanal_carpeta_investigacion ci
                    ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
                   AND ci.activo = 1
                INNER JOIN dbo.semanal_carga sc
                    ON sc.id_semanal_carga = v.id_semanal_carga
                   AND sc.activo = 1
                   AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
                   AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                INNER JOIN dbo.catalogo_tipo_victima tv
                    ON tv.id_tipo_victima = v.id_tipo_victima
                   AND tv.activo = 1
                LEFT JOIN dbo.catalogo_sexo sx
                    ON sx.id_sexo = v.id_sexo
                   AND sx.activo = 1
                WHERE v.activo = 1
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
                    fa.id_forma_accion,
                    tv.clave,
                    sx.clave,
                    sx.descripcion,
                    CASE
                        WHEN TRY_CONVERT(int, NULLIF(v.edad, N'')) = 999 THEN NULL
                        ELSE TRY_CONVERT(int, NULLIF(v.edad, N''))
                    END
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
                INNER JOIN dbo.catalogo_tipo_victima tv
                    ON tv.clave = TRY_CONVERT(tinyint, v.id_tv)
                   AND tv.activo = 1
                LEFT JOIN dbo.catalogo_sexo sx
                    ON sx.clave = TRY_CONVERT(tinyint, NULLIF(v.sexo, N''))
                   AND sx.activo = 1
                WHERE @ModoPlano IN (N'PREVIO', N'MIXTO')
                  AND fecha.fecha_inicio IS NOT NULL
                  AND YEAR(fecha.fecha_inicio) = p.anio_corte
                  AND MONTH(fecha.fecha_inicio) = p.mes_corte
            ),
        sabana AS (
        SELECT
            MIN(COALESCE(ol.orden_municipal_victimas, ol.orden_general, s.id_delito_sabana)) AS orden_sabana,
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
        matriz_municipal_sin_conteo AS (
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
                s.modalidad_delito_sabana,
                'No identificado' AS sexo,
                'No especificado' AS rango_edad,
                0 AS enero,
                0 AS febrero,
                0 AS marzo,
                0 AS abril,
                0 AS mayo,
                0 AS junio,
                0 AS julio,
                0 AS agosto,
                0 AS septiembre,
                0 AS octubre,
                0 AS noviembre,
                0 AS diciembre
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
                    fv.anio_corte,
                    TRY_CONVERT(int, efh.clave) AS clave_ent,
                    efh.nombre AS entidad,
                    TRY_CONVERT(int, CONCAT(
                        TRY_CONVERT(int, efh.clave),
                        RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                    )) AS clave_municipio_compuesta,
                    mun.nombre AS municipio,
                    MIN(COALESCE(ol.orden_municipal_victimas, ol.orden_general, s.id_delito_sabana)) AS orden_sabana,
                    MIN(cd.id_delito) AS orden_delito,
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana,
                    CASE
                        WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave = 1 THEN 'Hombre'
                        WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave = 2 THEN 'Mujer'
                        ELSE 'No identificado'
                    END AS sexo,
                    CASE
                        WHEN fv.tipo_victima_clave <> 1 THEN 'No especificado'
                        WHEN fv.edad IS NULL THEN 'No especificado'
                        WHEN fv.edad BETWEEN 0 AND 12 THEN '0 a 12 años'
                        WHEN fv.edad BETWEEN 13 AND 17 THEN '13 a 17 años'
                        WHEN fv.edad BETWEEN 18 AND 29 THEN '18 a 29 años'
                        WHEN fv.edad BETWEEN 30 AND 60 THEN '30 a 60 años'
                        WHEN fv.edad BETWEEN 61 AND 120 THEN 'Más de 60 años'
                        ELSE 'No especificado'
                    END AS rango_edad,
                    SUM(CASE WHEN fv.mes_corte = 1 THEN 1 ELSE 0 END) AS enero,
                    SUM(CASE WHEN fv.mes_corte = 2 THEN 1 ELSE 0 END) AS febrero,
                    SUM(CASE WHEN fv.mes_corte = 3 THEN 1 ELSE 0 END) AS marzo,
                    SUM(CASE WHEN fv.mes_corte = 4 THEN 1 ELSE 0 END) AS abril,
                    SUM(CASE WHEN fv.mes_corte = 5 THEN 1 ELSE 0 END) AS mayo,
                    SUM(CASE WHEN fv.mes_corte = 6 THEN 1 ELSE 0 END) AS junio,
                    SUM(CASE WHEN fv.mes_corte = 7 THEN 1 ELSE 0 END) AS julio,
                    SUM(CASE WHEN fv.mes_corte = 8 THEN 1 ELSE 0 END) AS agosto,
                    SUM(CASE WHEN fv.mes_corte = 9 THEN 1 ELSE 0 END) AS septiembre,
                    SUM(CASE WHEN fv.mes_corte = 10 THEN 1 ELSE 0 END) AS octubre,
                    SUM(CASE WHEN fv.mes_corte = 11 THEN 1 ELSE 0 END) AS noviembre,
                    SUM(CASE WHEN fv.mes_corte = 12 THEN 1 ELSE 0 END) AS diciembre
                FROM fuente_victimas fv
                INNER JOIN catalogo_entidad_federativa efh
                    ON efh.id_entidad_federativa = fv.id_entidad_hechos
                   AND efh.activo = 1
                INNER JOIN catalogo_municipio mun
                    ON mun.id_municipio = fv.id_municipio
                   AND mun.activo = 1
                INNER JOIN catalogo_delito_sabana s
                    ON s.id_modalidad_delito = fv.id_modalidad_delito
                   AND s.id_grado_consumacion = fv.id_grado_consumacion
                   AND s.id_instrumento_comision = fv.id_instrumento_comision
                   AND s.id_forma_accion = fv.id_forma_accion
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
                LEFT JOIN dbo.catalogo_sabana_orden_legacy ol
                    ON ol.bien_juridico = bj.bien_juridico
                   AND ol.delito_sabana = s.delito_sabana
                   AND ol.subtipo_delito_sabana = s.subtipo_delito_sabana
                   AND ol.modalidad_delito_sabana = s.modalidad_delito_sabana
                   AND ol.activo = 1
                WHERE TRY_CONVERT(int, efh.clave) BETWEEN 1 AND 32
                GROUP BY
                    fv.anio_corte,
                    TRY_CONVERT(int, efh.clave),
                    efh.nombre,
                    TRY_CONVERT(int, CONCAT(
                        TRY_CONVERT(int, efh.clave),
                        RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)
                    )),
                    mun.nombre,
                    bj.bien_juridico,
                    s.delito_sabana,
                    s.subtipo_delito_sabana,
                    s.modalidad_delito_sabana,
                    CASE
                        WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave = 1 THEN 'Hombre'
                        WHEN fv.tipo_victima_clave = 1 AND fv.sexo_clave = 2 THEN 'Mujer'
                        ELSE 'No identificado'
                    END,
                    CASE
                        WHEN fv.tipo_victima_clave <> 1 THEN 'No especificado'
                        WHEN fv.edad IS NULL THEN 'No especificado'
                        WHEN fv.edad BETWEEN 0 AND 12 THEN '0 a 12 años'
                        WHEN fv.edad BETWEEN 13 AND 17 THEN '13 a 17 años'
                        WHEN fv.edad BETWEEN 18 AND 29 THEN '18 a 29 años'
                        WHEN fv.edad BETWEEN 30 AND 60 THEN '30 a 60 años'
                        WHEN fv.edad BETWEEN 61 AND 120 THEN 'Más de 60 años'
                        ELSE 'No especificado'
                    END
         
        ),
        municipios_con_conteo AS (
            SELECT DISTINCT
                clave_municipio_compuesta
            FROM conteos
        ),
        resultado AS (
            SELECT
                1 AS bloque_resultado,
                anio_corte,
                clave_ent,
                entidad,
                clave_municipio_compuesta,
                municipio,
                orden_sabana,
                orden_delito,
                bien_juridico,
                delito_sabana,
                subtipo_delito_sabana,
                modalidad_delito_sabana,
                sexo,
                rango_edad,
                enero,
                febrero,
                marzo,
                abril,
                mayo,
                junio,
                julio,
                agosto,
                septiembre,
                octubre,
                noviembre,
                diciembre
            FROM conteos

            UNION ALL

            SELECT
                2 AS bloque_resultado,
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
                m.modalidad_delito_sabana,
                m.sexo,
                m.rango_edad,
                m.enero,
                m.febrero,
                m.marzo,
                m.abril,
                m.mayo,
                m.junio,
                m.julio,
                m.agosto,
                m.septiembre,
                m.octubre,
                m.noviembre,
                m.diciembre
            FROM matriz_municipal_sin_conteo m
            LEFT JOIN municipios_con_conteo mc
                ON mc.clave_municipio_compuesta = m.clave_municipio_compuesta
            WHERE mc.clave_municipio_compuesta IS NULL
        )
        SELECT
            anio_corte AS [Año],
            RIGHT('00' + CONVERT(varchar(2), clave_ent), 2) AS [Clave_Ent],
            entidad AS [Entidad],
            clave_municipio_compuesta AS [Cve. Municipio],
            municipio AS [Municipio],
            bien_juridico AS [Bien jurídico afectado],
            delito_sabana AS [Tipo de delito],
            subtipo_delito_sabana AS [Subtipo de delito],
            modalidad_delito_sabana AS [Modalidad],
            sexo AS [Sexo],
            rango_edad AS [Rango de edad],
            enero AS [Enero],
            febrero AS [Febrero],
            marzo AS [Marzo],
            abril AS [Abril],
            mayo AS [Mayo],
            junio AS [Junio],
            julio AS [Julio],
            agosto AS [Agosto],
            septiembre AS [Septiembre],
            octubre AS [Octubre],
            noviembre AS [Noviembre],
            diciembre AS [Diciembre]
        FROM resultado
        ORDER BY
            bloque_resultado,

            CASE WHEN bloque_resultado = 1 THEN clave_ent END,
            CASE WHEN bloque_resultado = 1 THEN clave_municipio_compuesta END,
            CASE WHEN bloque_resultado = 1 THEN orden_sabana END,

            CASE WHEN bloque_resultado = 2 THEN orden_sabana END,
            CASE WHEN bloque_resultado = 2 THEN clave_ent END,
            CASE WHEN bloque_resultado = 2 THEN clave_municipio_compuesta END,

            CASE sexo
                WHEN 'Hombre' THEN 1
                WHEN 'Mujer' THEN 2
                ELSE 3
            END,
            CASE rango_edad
                WHEN '0 a 12 años' THEN 1
                WHEN '13 a 17 años' THEN 2
                WHEN '18 a 29 años' THEN 3
                WHEN '30 a 60 años' THEN 4
                WHEN 'Más de 60 años' THEN 5
                ELSE 6
            END
        OPTION (RECOMPILE);";

        return await QueryDictionarySabanaAsync(sql, anioCorte, idEntidadFederativa, idUsuarioCarga, modoPlano, mesUltimoCorte);
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