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
    periodos AS
    (
        SELECT
            bloque.id_entidad_federativa,
            bloque.id_usuario_carga,
            bloque.anio_corte,
            bloque.mes_corte,
            COUNT(DISTINCT bloque.id_semanal_carga) AS intentos
        FROM bloques_carga bloque
        WHERE (@IdEntidadFederativa IS NULL OR bloque.id_entidad_federativa = @IdEntidadFederativa)
          AND (@IdUsuarioCarga IS NULL OR bloque.id_usuario_carga = @IdUsuarioCarga)
          AND (@AnioCorte IS NULL OR bloque.anio_corte = @AnioCorte)
          AND (@MesCorte IS NULL OR bloque.mes_corte = @MesCorte)
        GROUP BY
            bloque.id_entidad_federativa,
            bloque.id_usuario_carga,
            bloque.anio_corte,
            bloque.mes_corte
    )
    SELECT
        ef.id_entidad_federativa AS IdEntidadFederativa,
        ef.nombre AS EntidadFederativa,
        ef.clave AS ClaveEntidad,
        periodo.id_usuario_carga AS IdUsuarioCarga,
        usuario.usuario AS UsuarioCarga,
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
        ) AS NombreUsuarioCarga,
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
    INNER JOIN dbo.catalogo_entidad_federativa ef
        ON ef.id_entidad_federativa = periodo.id_entidad_federativa
       AND ef.activo = 1
    INNER JOIN dbo.usuario usuario
        ON usuario.id_usuario = periodo.id_usuario_carga
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
                WHEN bloque.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
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
        ef.nombre,
        usuario.usuario;
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

    public async Task<bool> ExisteInformacionPlanoAsync(int anioCorte, int mesCorte, int? idEntidadFederativa, string modoPlano)
    {
        const string sql = @"
        SELECT CONVERT(bit, CASE WHEN EXISTS
        (
            SELECT 1
            FROM dbo.semanal_carga sc
            WHERE sc.anio_corte = @AnioCorte
              AND sc.mes_corte = @MesCorte
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.activo = 1
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND
              (
                  @ModoPlano = N'CONFIRMADO' AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                  OR @ModoPlano = N'PREVIO' AND sc.estado = N'PENDIENTE_APROBACION'
                  OR @ModoPlano = N'MIXTO' AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION', N'PENDIENTE_APROBACION')
              )
        ) THEN 1 ELSE 0 END);
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.ExecuteScalarAsync<bool>(sql, new { AnioCorte = anioCorte, MesCorte = mesCorte, IdEntidadFederativa = idEntidadFederativa, ModoPlano = modoPlano });
    }

    public Task<List<IDictionary<string, object?>>> ObtenerPlanoEstatalDelitosAsync(int anioCorte, int mesCorte, int? idEntidadFederativa, string modoPlano) => ObtenerPlanoDelitosAsync(anioCorte, mesCorte, idEntidadFederativa, modoPlano, false);

    public Task<List<IDictionary<string, object?>>> ObtenerPlanoMunicipalDelitosAsync(int anioCorte, int mesCorte, int? idEntidadFederativa, string modoPlano) => ObtenerPlanoDelitosAsync(anioCorte, mesCorte, idEntidadFederativa, modoPlano, true);

    public Task<List<IDictionary<string, object?>>> ObtenerPlanoEstatalVictimasAsync(int anioCorte, int mesCorte, int? idEntidadFederativa, string modoPlano) => ObtenerPlanoVictimasAsync(anioCorte, mesCorte, idEntidadFederativa, modoPlano, false);

    public Task<List<IDictionary<string, object?>>> ObtenerPlanoMunicipalVictimasAsync(int anioCorte, int mesCorte, int? idEntidadFederativa, string modoPlano) => ObtenerPlanoVictimasMunicipalAsync(anioCorte, mesCorte, idEntidadFederativa, modoPlano);

    private Task<List<IDictionary<string, object?>>> ObtenerPlanoDelitosAsync(int anioCorte, int mesCorte, int? idEntidadFederativa, string modoPlano, bool municipal)
    {
        var semanas = ObtenerSemanasMes(anioCorte, mesCorte);
        var columnasSemanas = string.Join(",\n            ", semanas.Select(semana => $"ISNULL(SUM(CASE WHEN c.anio_semana = {semana.AnioSemana} AND c.numero_semana = {semana.NumeroSemana} THEN c.cantidad_delitos ELSE 0 END), 0) AS [{semana.Columna}]"));

        var columnasMatrizMunicipio = municipal
            ? @",
                TRY_CONVERT(int, CONCAT(TRY_CONVERT(int, ef.clave), RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, municipio_matriz.clave)), 3))) AS clave_municipio_compuesta,
                municipio_matriz.nombre AS municipio"
            : string.Empty;

        var joinMatrizMunicipio = municipal
            ? @"
            INNER JOIN dbo.catalogo_municipio municipio_matriz ON municipio_matriz.id_entidad_federativa = ef.id_entidad_federativa AND municipio_matriz.activo = 1"
            : string.Empty;

        var columnasFuenteMunicipio = municipal
            ? @",
                TRY_CONVERT(int, CONCAT(TRY_CONVERT(int, entidad_hechos.clave), RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, municipio.clave)), 3))) AS clave_municipio_compuesta"
            : string.Empty;

        var columnasConteoMunicipio = municipal ? ", fuente.clave_municipio_compuesta" : string.Empty;
        var agrupacionConteoMunicipio = municipal ? ", fuente.clave_municipio_compuesta" : string.Empty;

        var columnasFinalMunicipio = municipal
            ? @",
            m.clave_municipio_compuesta AS [Cve. Municipio],
            m.municipio AS [Municipio]"
            : string.Empty;

        var unionFinalMunicipio = municipal ? "AND c.clave_municipio_compuesta = m.clave_municipio_compuesta" : string.Empty;
        var agrupacionFinalMunicipio = municipal ? ", m.clave_municipio_compuesta, m.municipio" : string.Empty;
        var ordenFinalMunicipio = municipal ? ", m.clave_municipio_compuesta" : string.Empty;

        var sql = $@"
        WITH pendientes_rankeadas AS
        (
            SELECT
                sc.id_semanal_carga,
                sc.id_entidad_federativa,
                sc.anio_semana,
                sc.numero_semana,
                sc.fecha_inicio_tramo,
                sc.fecha_fin_tramo,
                ROW_NUMBER() OVER
                (
                    PARTITION BY sc.id_entidad_federativa, sc.anio_corte, sc.mes_corte, sc.anio_semana, sc.numero_semana
                    ORDER BY sc.fecha_validacion DESC, sc.id_semanal_carga DESC
                ) AS rn
            FROM dbo.semanal_carga sc
            WHERE sc.anio_corte = @AnioCorte
              AND sc.mes_corte = @MesCorte
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado = N'PENDIENTE_APROBACION'
              AND sc.activo = 1
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
        ),
        pendientes AS
        (
            SELECT id_semanal_carga, id_entidad_federativa, anio_semana, numero_semana, fecha_inicio_tramo, fecha_fin_tramo
            FROM pendientes_rankeadas
            WHERE rn = 1
        ),
        cargas_confirmadas AS
        (
            SELECT sc.id_semanal_carga, sc.id_entidad_federativa, sc.anio_semana, sc.numero_semana, sc.fecha_inicio_tramo, sc.fecha_fin_tramo
            FROM dbo.semanal_carga sc
            WHERE sc.anio_corte = @AnioCorte
              AND sc.mes_corte = @MesCorte
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
              AND sc.activo = 1
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
        ),
        confirmadas_visibles AS
        (
            SELECT confirmada.*
            FROM cargas_confirmadas confirmada
            WHERE @ModoPlano = N'CONFIRMADO'
               OR
               (
                   @ModoPlano = N'MIXTO'
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM pendientes pendiente
                       WHERE pendiente.id_entidad_federativa = confirmada.id_entidad_federativa
                         AND pendiente.anio_semana = confirmada.anio_semana
                         AND pendiente.numero_semana = confirmada.numero_semana
                   )
               )
        ),
        cargas_visibles AS
        (
            SELECT id_semanal_carga FROM confirmadas_visibles
            UNION
            SELECT id_semanal_carga FROM pendientes WHERE @ModoPlano IN (N'PREVIO', N'MIXTO')
        ),
        modalidades_configuradas AS
        (
            SELECT DISTINCT configuracion.id_modalidad_delito
            FROM dbo.semanal_carga_delito_configurado configuracion
            INNER JOIN cargas_visibles carga ON carga.id_semanal_carga = configuracion.id_semanal_carga
        ),
        sabana AS
        (
            SELECT
                MIN(COALESCE(orden_legacy.orden_general, sabana.id_delito_sabana)) AS orden_sabana,
                MIN(delito.id_delito) AS orden_delito,
                bien.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana
            FROM dbo.catalogo_delito_sabana sabana
            INNER JOIN modalidades_configuradas configuracion ON configuracion.id_modalidad_delito = sabana.id_modalidad_delito
            INNER JOIN dbo.catalogo_modalidad_delito modalidad ON modalidad.id_modalidad_delito = sabana.id_modalidad_delito AND modalidad.activo = 1
            INNER JOIN dbo.catalogo_subtipo_delito subtipo ON subtipo.id_subtipo_delito = modalidad.id_subtipo_delito AND subtipo.activo = 1
            INNER JOIN dbo.catalogo_delito delito ON delito.id_delito = subtipo.id_delito AND delito.activo = 1
            INNER JOIN dbo.catalogo_bien_juridico bien ON bien.id_bien_juridico = delito.id_bien_juridico AND bien.activo = 1
            LEFT JOIN dbo.catalogo_sabana_orden_legacy orden_legacy ON orden_legacy.bien_juridico = bien.bien_juridico AND orden_legacy.delito_sabana = sabana.delito_sabana AND orden_legacy.subtipo_delito_sabana = sabana.subtipo_delito_sabana AND orden_legacy.modalidad_delito_sabana = sabana.modalidad_delito_sabana AND orden_legacy.activo = 1
            WHERE sabana.activo = 1
            GROUP BY bien.bien_juridico, sabana.delito_sabana, sabana.subtipo_delito_sabana, sabana.modalidad_delito_sabana
        ),
        matriz AS
        (
            SELECT
                @AnioCorte AS anio_corte,
                TRY_CONVERT(int, ef.clave) AS clave_ent,
                ef.nombre AS entidad
                {columnasMatrizMunicipio},
                sabana.orden_sabana,
                sabana.orden_delito,
                sabana.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana
            FROM dbo.catalogo_entidad_federativa ef
            {joinMatrizMunicipio}
            CROSS JOIN sabana
            WHERE ef.activo = 1
              AND TRY_CONVERT(int, ef.clave) BETWEEN 1 AND 32
              AND (@IdEntidadFederativa IS NULL OR ef.id_entidad_federativa = @IdEntidadFederativa)
              AND
              (
                  @ModoPlano <> N'PREVIO'
                  OR EXISTS
                  (
                      SELECT 1
                      FROM pendientes pendiente
                      WHERE pendiente.id_entidad_federativa = ef.id_entidad_federativa
                  )
              )
        ),
        fuente_delitos AS
        (
            SELECT
                carga.anio_semana,
                carga.numero_semana,
                delito_semanal.id_entidad_federativa AS id_entidad_hechos,
                delito_semanal.id_municipio,
                delito_semanal.id_modalidad_delito,
                delito_semanal.id_grado_consumacion,
                delito_semanal.id_instrumento_comision,
                delito_semanal.id_forma_accion
            FROM dbo.semanal_delito delito_semanal
            INNER JOIN confirmadas_visibles carga ON carga.id_semanal_carga = delito_semanal.id_semanal_carga
            INNER JOIN dbo.semanal_carpeta_investigacion carpeta ON carpeta.id_semanal_carpeta_investigacion = delito_semanal.id_semanal_carpeta_investigacion AND carpeta.activo = 1
            WHERE delito_semanal.activo = 1
              AND carpeta.fecha_inicio >= carga.fecha_inicio_tramo
              AND carpeta.fecha_inicio < DATEADD(DAY, 1, carga.fecha_fin_tramo)

            UNION ALL

            SELECT
                carga.anio_semana,
                carga.numero_semana,
                entidad_hechos.id_entidad_federativa,
                municipio.id_municipio,
                modalidad.id_modalidad_delito,
                grado.id_grado_consumacion,
                instrumento.id_instrumento_comision,
                forma.id_forma_accion
            FROM pendientes carga
            INNER JOIN dbo.semanal_carga_tmp_delito delito_tmp ON delito_tmp.id_semanal_carga = carga.id_semanal_carga AND delito_tmp.activo = 1
            INNER JOIN dbo.semanal_carga_tmp_carpeta carpeta_tmp ON carpeta_tmp.id_semanal_carga = delito_tmp.id_semanal_carga AND carpeta_tmp.id_ci = delito_tmp.id_ci AND carpeta_tmp.activo = 1
            INNER JOIN dbo.catalogo_modalidad_delito modalidad ON modalidad.clave4 = delito_tmp.clasf_de_dto AND modalidad.activo = 1
            INNER JOIN dbo.catalogo_forma_accion forma ON forma.clave = TRY_CONVERT(tinyint, delito_tmp.forma_acc) AND forma.activo = 1
            INNER JOIN dbo.catalogo_instrumento_comision instrumento ON instrumento.clave = TRY_CONVERT(tinyint, delito_tmp.emto_com_dto) AND instrumento.activo = 1
            INNER JOIN dbo.catalogo_grado_consumacion grado ON grado.clave = TRY_CONVERT(tinyint, delito_tmp.grdo_cons) AND grado.activo = 1
            INNER JOIN dbo.catalogo_entidad_federativa entidad_hechos ON entidad_hechos.id_entidad_federativa = TRY_CONVERT(tinyint, delito_tmp.id_ent_hchos) AND entidad_hechos.activo = 1
            INNER JOIN dbo.catalogo_municipio municipio ON municipio.id_entidad_federativa = entidad_hechos.id_entidad_federativa AND TRY_CONVERT(int, municipio.clave) = TRY_CONVERT(int, delito_tmp.id_mun_hchos) AND municipio.activo = 1
            WHERE @ModoPlano IN (N'PREVIO', N'MIXTO')
              AND COALESCE(TRY_CONVERT(date, carpeta_tmp.fha_de_ini, 103), TRY_CONVERT(date, carpeta_tmp.fha_de_ini, 23), TRY_CONVERT(date, carpeta_tmp.fha_de_ini)) >= carga.fecha_inicio_tramo
              AND COALESCE(TRY_CONVERT(date, carpeta_tmp.fha_de_ini, 103), TRY_CONVERT(date, carpeta_tmp.fha_de_ini, 23), TRY_CONVERT(date, carpeta_tmp.fha_de_ini)) < DATEADD(DAY, 1, carga.fecha_fin_tramo)
        ),
        fuente AS
        (
            SELECT
                fuente_delitos.anio_semana,
                fuente_delitos.numero_semana,
                TRY_CONVERT(int, entidad_hechos.clave) AS clave_ent
                {columnasFuenteMunicipio},
                bien.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana
            FROM fuente_delitos
            INNER JOIN dbo.catalogo_entidad_federativa entidad_hechos ON entidad_hechos.id_entidad_federativa = fuente_delitos.id_entidad_hechos AND entidad_hechos.activo = 1
            INNER JOIN dbo.catalogo_municipio municipio ON municipio.id_municipio = fuente_delitos.id_municipio AND municipio.id_entidad_federativa = fuente_delitos.id_entidad_hechos AND municipio.activo = 1
            INNER JOIN dbo.catalogo_delito_sabana sabana ON sabana.id_modalidad_delito = fuente_delitos.id_modalidad_delito AND sabana.id_grado_consumacion = fuente_delitos.id_grado_consumacion AND sabana.id_instrumento_comision = fuente_delitos.id_instrumento_comision AND sabana.id_forma_accion = fuente_delitos.id_forma_accion AND sabana.activo = 1
            INNER JOIN dbo.catalogo_modalidad_delito modalidad ON modalidad.id_modalidad_delito = sabana.id_modalidad_delito AND modalidad.activo = 1
            INNER JOIN dbo.catalogo_subtipo_delito subtipo ON subtipo.id_subtipo_delito = modalidad.id_subtipo_delito AND subtipo.activo = 1
            INNER JOIN dbo.catalogo_delito delito ON delito.id_delito = subtipo.id_delito AND delito.activo = 1
            INNER JOIN dbo.catalogo_bien_juridico bien ON bien.id_bien_juridico = delito.id_bien_juridico AND bien.activo = 1
            WHERE TRY_CONVERT(int, entidad_hechos.clave) BETWEEN 1 AND 32
        ),
        conteos AS
        (
            SELECT
                fuente.anio_semana,
                fuente.numero_semana,
                fuente.clave_ent
                {columnasConteoMunicipio},
                fuente.bien_juridico,
                fuente.delito_sabana,
                fuente.subtipo_delito_sabana,
                fuente.modalidad_delito_sabana,
                COUNT_BIG(1) AS cantidad_delitos
            FROM fuente
            GROUP BY fuente.anio_semana, fuente.numero_semana, fuente.clave_ent
                {agrupacionConteoMunicipio},
                fuente.bien_juridico,
                fuente.delito_sabana,
                fuente.subtipo_delito_sabana,
                fuente.modalidad_delito_sabana
        )
        SELECT
            m.anio_corte AS [Año],
            RIGHT('00' + CONVERT(varchar(2), m.clave_ent), 2) AS [Clave_Ent],
            m.entidad AS [Entidad]
            {columnasFinalMunicipio},
            m.bien_juridico AS [Bien jurídico afectado],
            m.delito_sabana AS [Tipo de delito],
            m.subtipo_delito_sabana AS [Subtipo de delito],
            m.modalidad_delito_sabana AS [Modalidad],
            {columnasSemanas}
        FROM matriz m
        LEFT JOIN conteos c ON c.clave_ent = m.clave_ent
            {unionFinalMunicipio}
           AND c.bien_juridico = m.bien_juridico
           AND c.delito_sabana = m.delito_sabana
           AND c.subtipo_delito_sabana = m.subtipo_delito_sabana
           AND c.modalidad_delito_sabana = m.modalidad_delito_sabana
        GROUP BY m.anio_corte, m.clave_ent, m.entidad
            {agrupacionFinalMunicipio},
            m.orden_sabana,
            m.orden_delito,
            m.bien_juridico,
            m.delito_sabana,
            m.subtipo_delito_sabana,
            m.modalidad_delito_sabana
        ORDER BY m.clave_ent, m.orden_sabana, m.orden_delito, m.subtipo_delito_sabana, m.modalidad_delito_sabana {ordenFinalMunicipio}
        OPTION (RECOMPILE);
        ";

        return QueryDictionaryPlanoAsync(sql, anioCorte, mesCorte, idEntidadFederativa, modoPlano);
    }

    private Task<List<IDictionary<string, object?>>> ObtenerPlanoVictimasAsync(int anioCorte, int mesCorte, int? idEntidadFederativa, string modoPlano, bool municipal)
    {
        var semanas = ObtenerSemanasMes(anioCorte, mesCorte);
        var columnasSemanas = string.Join(",\n            ", semanas.Select(semana => $"ISNULL(SUM(CASE WHEN c.anio_semana = {semana.AnioSemana} AND c.numero_semana = {semana.NumeroSemana} THEN c.cantidad_victimas ELSE 0 END), 0) AS [{semana.Columna}]"));

        var columnasMatrizMunicipio = municipal
            ? @",
                TRY_CONVERT(int, CONCAT(TRY_CONVERT(int, ef.clave), RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, municipio_matriz.clave)), 3))) AS clave_municipio_compuesta,
                municipio_matriz.nombre AS municipio"
            : string.Empty;

        var joinMatrizMunicipio = municipal
            ? @"
            INNER JOIN dbo.catalogo_municipio municipio_matriz ON municipio_matriz.id_entidad_federativa = ef.id_entidad_federativa AND municipio_matriz.activo = 1"
            : string.Empty;

        var columnasFuenteMunicipio = municipal
            ? @",
                TRY_CONVERT(int, CONCAT(TRY_CONVERT(int, entidad_hechos.clave), RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, municipio.clave)), 3))) AS clave_municipio_compuesta"
            : string.Empty;

        var columnasConteoMunicipio = municipal ? ", clasificada.clave_municipio_compuesta" : string.Empty;
        var agrupacionConteoMunicipio = municipal ? ", clasificada.clave_municipio_compuesta" : string.Empty;

        var columnasFinalMunicipio = municipal
            ? @",
            m.clave_municipio_compuesta AS [Cve. Municipio],
            m.municipio AS [Municipio]"
            : string.Empty;

        var unionFinalMunicipio = municipal ? "AND c.clave_municipio_compuesta = m.clave_municipio_compuesta" : string.Empty;
        var agrupacionFinalMunicipio = municipal ? ", m.clave_municipio_compuesta, m.municipio" : string.Empty;
        var ordenFinalMunicipio = municipal ? ", m.clave_municipio_compuesta" : string.Empty;

        var sql = $@"
        WITH pendientes_rankeadas AS
        (
            SELECT
                sc.id_semanal_carga,
                sc.id_entidad_federativa,
                sc.anio_semana,
                sc.numero_semana,
                sc.fecha_inicio_tramo,
                sc.fecha_fin_tramo,
                ROW_NUMBER() OVER
                (
                    PARTITION BY sc.id_entidad_federativa, sc.anio_corte, sc.mes_corte, sc.anio_semana, sc.numero_semana
                    ORDER BY sc.fecha_validacion DESC, sc.id_semanal_carga DESC
                ) AS rn
            FROM dbo.semanal_carga sc
            WHERE sc.anio_corte = @AnioCorte
              AND sc.mes_corte = @MesCorte
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado = N'PENDIENTE_APROBACION'
              AND sc.activo = 1
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
        ),
        pendientes AS
        (
            SELECT id_semanal_carga, id_entidad_federativa, anio_semana, numero_semana, fecha_inicio_tramo, fecha_fin_tramo
            FROM pendientes_rankeadas
            WHERE rn = 1
        ),
        cargas_confirmadas AS
        (
            SELECT sc.id_semanal_carga, sc.id_entidad_federativa, sc.anio_semana, sc.numero_semana, sc.fecha_inicio_tramo, sc.fecha_fin_tramo
            FROM dbo.semanal_carga sc
            WHERE sc.anio_corte = @AnioCorte
              AND sc.mes_corte = @MesCorte
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
              AND sc.activo = 1
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
        ),
        confirmadas_visibles AS
        (
            SELECT confirmada.*
            FROM cargas_confirmadas confirmada
            WHERE @ModoPlano = N'CONFIRMADO'
               OR
               (
                   @ModoPlano = N'MIXTO'
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM pendientes pendiente
                       WHERE pendiente.id_entidad_federativa = confirmada.id_entidad_federativa
                         AND pendiente.anio_semana = confirmada.anio_semana
                         AND pendiente.numero_semana = confirmada.numero_semana
                   )
               )
        ),
        cargas_visibles AS
        (
            SELECT id_semanal_carga FROM confirmadas_visibles
            UNION
            SELECT id_semanal_carga FROM pendientes WHERE @ModoPlano IN (N'PREVIO', N'MIXTO')
        ),
        modalidades_configuradas AS
        (
            SELECT DISTINCT configuracion.id_modalidad_delito
            FROM dbo.semanal_carga_delito_configurado configuracion
            INNER JOIN cargas_visibles carga ON carga.id_semanal_carga = configuracion.id_semanal_carga
        ),
        sabana AS
        (
            SELECT
                MIN(COALESCE(orden_legacy.orden_general, sabana.id_delito_sabana)) AS orden_sabana,
                MIN(delito.id_delito) AS orden_delito,
                bien.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana
            FROM dbo.catalogo_delito_sabana sabana
            INNER JOIN modalidades_configuradas configuracion ON configuracion.id_modalidad_delito = sabana.id_modalidad_delito
            INNER JOIN dbo.catalogo_modalidad_delito modalidad ON modalidad.id_modalidad_delito = sabana.id_modalidad_delito AND modalidad.activo = 1
            INNER JOIN dbo.catalogo_subtipo_delito subtipo ON subtipo.id_subtipo_delito = modalidad.id_subtipo_delito AND subtipo.activo = 1
            INNER JOIN dbo.catalogo_delito delito ON delito.id_delito = subtipo.id_delito AND delito.activo = 1
            INNER JOIN dbo.catalogo_bien_juridico bien ON bien.id_bien_juridico = delito.id_bien_juridico AND bien.activo = 1
            LEFT JOIN dbo.catalogo_sabana_orden_legacy orden_legacy ON orden_legacy.bien_juridico = bien.bien_juridico AND orden_legacy.delito_sabana = sabana.delito_sabana AND orden_legacy.subtipo_delito_sabana = sabana.subtipo_delito_sabana AND orden_legacy.modalidad_delito_sabana = sabana.modalidad_delito_sabana AND orden_legacy.activo = 1
            WHERE sabana.activo = 1
            GROUP BY bien.bien_juridico, sabana.delito_sabana, sabana.subtipo_delito_sabana, sabana.modalidad_delito_sabana
        ),
        sexos AS
        (
            SELECT 1 AS orden_sexo, N'Hombre' AS sexo
            UNION ALL SELECT 2, N'Mujer'
            UNION ALL SELECT 3, N'No identificado'
        ),
        rangos_edad AS
        (
            SELECT 1 AS orden_rango, N'0 a 12 años' AS rango_edad
            UNION ALL SELECT 2, N'13 a 17 años'
            UNION ALL SELECT 3, N'18 a 29 años'
            UNION ALL SELECT 4, N'30 a 60 años'
            UNION ALL SELECT 5, N'Más de 60 años'
            UNION ALL SELECT 6, N'No especificado'
        ),
        matriz AS
        (
            SELECT
                @AnioCorte AS anio_corte,
                TRY_CONVERT(int, ef.clave) AS clave_ent,
                ef.nombre AS entidad
                {columnasMatrizMunicipio},
                sabana.orden_sabana,
                sabana.orden_delito,
                sabana.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana,
                sexo.orden_sexo,
                sexo.sexo,
                rango.orden_rango,
                rango.rango_edad
            FROM dbo.catalogo_entidad_federativa ef
            {joinMatrizMunicipio}
            CROSS JOIN sabana
            CROSS JOIN sexos sexo
            CROSS JOIN rangos_edad rango
            WHERE ef.activo = 1
              AND TRY_CONVERT(int, ef.clave) BETWEEN 1 AND 32
              AND (@IdEntidadFederativa IS NULL OR ef.id_entidad_federativa = @IdEntidadFederativa)
              AND
              (
                  @ModoPlano <> N'PREVIO'
                  OR EXISTS
                  (
                      SELECT 1
                      FROM pendientes pendiente
                      WHERE pendiente.id_entidad_federativa = ef.id_entidad_federativa
                  )
              )
        ),
        fuente_victimas AS
        (
            SELECT
                carga.anio_semana,
                carga.numero_semana,
                delito_semanal.id_entidad_federativa AS id_entidad_hechos,
                delito_semanal.id_municipio,
                delito_semanal.id_modalidad_delito,
                delito_semanal.id_grado_consumacion,
                delito_semanal.id_instrumento_comision,
                delito_semanal.id_forma_accion,
                tipo_victima.clave AS tipo_victima_clave,
                sexo.clave AS sexo_clave,
                sexo.descripcion AS sexo_descripcion,
                CASE WHEN TRY_CONVERT(int, victima.edad) = 999 THEN NULL ELSE TRY_CONVERT(int, victima.edad) END AS edad
            FROM dbo.semanal_victima victima
            INNER JOIN confirmadas_visibles carga ON carga.id_semanal_carga = victima.id_semanal_carga
            INNER JOIN dbo.semanal_delito delito_semanal ON delito_semanal.id_semanal_delito = victima.id_semanal_delito AND delito_semanal.activo = 1
            INNER JOIN dbo.semanal_carpeta_investigacion carpeta ON carpeta.id_semanal_carpeta_investigacion = delito_semanal.id_semanal_carpeta_investigacion AND carpeta.activo = 1
            INNER JOIN dbo.catalogo_tipo_victima tipo_victima ON tipo_victima.id_tipo_victima = victima.id_tipo_victima AND tipo_victima.activo = 1
            LEFT JOIN dbo.catalogo_sexo sexo ON sexo.id_sexo = victima.id_sexo AND sexo.activo = 1
            WHERE victima.activo = 1
              AND carpeta.fecha_inicio >= carga.fecha_inicio_tramo
              AND carpeta.fecha_inicio < DATEADD(DAY, 1, carga.fecha_fin_tramo)

            UNION ALL

            SELECT
                carga.anio_semana,
                carga.numero_semana,
                entidad_hechos.id_entidad_federativa,
                municipio.id_municipio,
                modalidad.id_modalidad_delito,
                grado.id_grado_consumacion,
                instrumento.id_instrumento_comision,
                forma.id_forma_accion,
                tipo_victima.clave,
                sexo.clave,
                sexo.descripcion,
                CASE WHEN TRY_CONVERT(int, NULLIF(victima_tmp.edad, N'')) = 999 THEN NULL ELSE TRY_CONVERT(int, NULLIF(victima_tmp.edad, N'')) END
            FROM pendientes carga
            INNER JOIN dbo.semanal_carga_tmp_victima victima_tmp ON victima_tmp.id_semanal_carga = carga.id_semanal_carga AND victima_tmp.activo = 1
            INNER JOIN dbo.semanal_carga_tmp_delito delito_tmp ON delito_tmp.id_semanal_carga = victima_tmp.id_semanal_carga AND delito_tmp.id_ci = victima_tmp.id_ci AND delito_tmp.id_delito = victima_tmp.id_delito AND delito_tmp.activo = 1
            INNER JOIN dbo.semanal_carga_tmp_carpeta carpeta_tmp ON carpeta_tmp.id_semanal_carga = victima_tmp.id_semanal_carga AND carpeta_tmp.id_ci = victima_tmp.id_ci AND carpeta_tmp.activo = 1
            INNER JOIN dbo.catalogo_modalidad_delito modalidad ON modalidad.clave4 = delito_tmp.clasf_de_dto AND modalidad.activo = 1
            INNER JOIN dbo.catalogo_forma_accion forma ON forma.clave = TRY_CONVERT(tinyint, delito_tmp.forma_acc) AND forma.activo = 1
            INNER JOIN dbo.catalogo_instrumento_comision instrumento ON instrumento.clave = TRY_CONVERT(tinyint, delito_tmp.emto_com_dto) AND instrumento.activo = 1
            INNER JOIN dbo.catalogo_grado_consumacion grado ON grado.clave = TRY_CONVERT(tinyint, delito_tmp.grdo_cons) AND grado.activo = 1
            INNER JOIN dbo.catalogo_entidad_federativa entidad_hechos ON entidad_hechos.id_entidad_federativa = TRY_CONVERT(tinyint, delito_tmp.id_ent_hchos) AND entidad_hechos.activo = 1
            INNER JOIN dbo.catalogo_municipio municipio ON municipio.id_entidad_federativa = entidad_hechos.id_entidad_federativa AND TRY_CONVERT(int, municipio.clave) = TRY_CONVERT(int, delito_tmp.id_mun_hchos) AND municipio.activo = 1
            INNER JOIN dbo.catalogo_tipo_victima tipo_victima ON tipo_victima.clave = TRY_CONVERT(tinyint, victima_tmp.id_tv) AND tipo_victima.activo = 1
            LEFT JOIN dbo.catalogo_sexo sexo ON sexo.clave = TRY_CONVERT(tinyint, NULLIF(victima_tmp.sexo, N'')) AND sexo.activo = 1
            WHERE @ModoPlano IN (N'PREVIO', N'MIXTO')
              AND COALESCE(TRY_CONVERT(date, carpeta_tmp.fha_de_ini, 103), TRY_CONVERT(date, carpeta_tmp.fha_de_ini, 23), TRY_CONVERT(date, carpeta_tmp.fha_de_ini)) >= carga.fecha_inicio_tramo
              AND COALESCE(TRY_CONVERT(date, carpeta_tmp.fha_de_ini, 103), TRY_CONVERT(date, carpeta_tmp.fha_de_ini, 23), TRY_CONVERT(date, carpeta_tmp.fha_de_ini)) < DATEADD(DAY, 1, carga.fecha_fin_tramo)
        ),
        fuente AS
        (
            SELECT
                fuente_victimas.anio_semana,
                fuente_victimas.numero_semana,
                TRY_CONVERT(int, entidad_hechos.clave) AS clave_ent
                {columnasFuenteMunicipio},
                bien.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana,
                fuente_victimas.tipo_victima_clave,
                fuente_victimas.sexo_clave,
                fuente_victimas.sexo_descripcion,
                fuente_victimas.edad
            FROM fuente_victimas
            INNER JOIN dbo.catalogo_entidad_federativa entidad_hechos ON entidad_hechos.id_entidad_federativa = fuente_victimas.id_entidad_hechos AND entidad_hechos.activo = 1
            INNER JOIN dbo.catalogo_municipio municipio ON municipio.id_municipio = fuente_victimas.id_municipio AND municipio.id_entidad_federativa = fuente_victimas.id_entidad_hechos AND municipio.activo = 1
            INNER JOIN dbo.catalogo_delito_sabana sabana ON sabana.id_modalidad_delito = fuente_victimas.id_modalidad_delito AND sabana.id_grado_consumacion = fuente_victimas.id_grado_consumacion AND sabana.id_instrumento_comision = fuente_victimas.id_instrumento_comision AND sabana.id_forma_accion = fuente_victimas.id_forma_accion AND sabana.activo = 1
            INNER JOIN dbo.catalogo_modalidad_delito modalidad ON modalidad.id_modalidad_delito = sabana.id_modalidad_delito AND modalidad.activo = 1
            INNER JOIN dbo.catalogo_subtipo_delito subtipo ON subtipo.id_subtipo_delito = modalidad.id_subtipo_delito AND subtipo.activo = 1
            INNER JOIN dbo.catalogo_delito delito ON delito.id_delito = subtipo.id_delito AND delito.activo = 1
            INNER JOIN dbo.catalogo_bien_juridico bien ON bien.id_bien_juridico = delito.id_bien_juridico AND bien.activo = 1
            WHERE TRY_CONVERT(int, entidad_hechos.clave) BETWEEN 1 AND 32
        ),
        victimas_clasificadas AS
        (
            SELECT
                fuente.*,
                CASE WHEN fuente.tipo_victima_clave = 1 AND fuente.sexo_clave IN (1, 2, 3) THEN ISNULL(fuente.sexo_descripcion, N'No identificado') ELSE N'No identificado' END AS sexo,
                CASE
                    WHEN fuente.tipo_victima_clave <> 1 THEN N'No especificado'
                    WHEN fuente.edad IS NULL THEN N'No especificado'
                    WHEN fuente.edad BETWEEN 0 AND 12 THEN N'0 a 12 años'
                    WHEN fuente.edad BETWEEN 13 AND 17 THEN N'13 a 17 años'
                    WHEN fuente.edad BETWEEN 18 AND 29 THEN N'18 a 29 años'
                    WHEN fuente.edad BETWEEN 30 AND 60 THEN N'30 a 60 años'
                    WHEN fuente.edad BETWEEN 61 AND 120 THEN N'Más de 60 años'
                    ELSE N'No especificado'
                END AS rango_edad
            FROM fuente
        ),
        conteos AS
        (
            SELECT
                clasificada.anio_semana,
                clasificada.numero_semana,
                clasificada.clave_ent
                {columnasConteoMunicipio},
                clasificada.bien_juridico,
                clasificada.delito_sabana,
                clasificada.subtipo_delito_sabana,
                clasificada.modalidad_delito_sabana,
                clasificada.sexo,
                clasificada.rango_edad,
                COUNT_BIG(1) AS cantidad_victimas
            FROM victimas_clasificadas clasificada
            GROUP BY clasificada.anio_semana, clasificada.numero_semana, clasificada.clave_ent
                {agrupacionConteoMunicipio},
                clasificada.bien_juridico,
                clasificada.delito_sabana,
                clasificada.subtipo_delito_sabana,
                clasificada.modalidad_delito_sabana,
                clasificada.sexo,
                clasificada.rango_edad
        )
        SELECT
            m.anio_corte AS [Año],
            RIGHT('00' + CONVERT(varchar(2), m.clave_ent), 2) AS [Clave_Ent],
            m.entidad AS [Entidad]
            {columnasFinalMunicipio},
            m.bien_juridico AS [Bien jurídico afectado],
            m.delito_sabana AS [Tipo de delito],
            m.subtipo_delito_sabana AS [Subtipo de delito],
            m.modalidad_delito_sabana AS [Modalidad],
            m.sexo AS [Sexo],
            m.rango_edad AS [Rango de edad],
            {columnasSemanas}
        FROM matriz m
        LEFT JOIN conteos c ON c.clave_ent = m.clave_ent
            {unionFinalMunicipio}
           AND c.bien_juridico = m.bien_juridico
           AND c.delito_sabana = m.delito_sabana
           AND c.subtipo_delito_sabana = m.subtipo_delito_sabana
           AND c.modalidad_delito_sabana = m.modalidad_delito_sabana
           AND c.sexo = m.sexo
           AND c.rango_edad = m.rango_edad
        GROUP BY m.anio_corte, m.clave_ent, m.entidad
            {agrupacionFinalMunicipio},
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
        ORDER BY m.clave_ent, m.orden_sabana, m.orden_delito, m.subtipo_delito_sabana, m.modalidad_delito_sabana, m.orden_sexo, m.orden_rango {ordenFinalMunicipio}
        OPTION (RECOMPILE);
        ";

        return QueryDictionaryPlanoAsync(sql, anioCorte, mesCorte, idEntidadFederativa, modoPlano);
    }

    private Task<List<IDictionary<string, object?>>> ObtenerPlanoVictimasMunicipalAsync(int anioCorte, int mesCorte, int? idEntidadFederativa, string modoPlano)
    {
        var semanas = ObtenerSemanasMes(anioCorte, mesCorte);
        var columnasSemanasCero = string.Join(",\n                ", semanas.Select((_, indice) => $"CAST(0 AS bigint) AS semana_{indice + 1}"));
        var columnasSemanasConteo = string.Join(",\n                ", semanas.Select((semana, indice) => $"SUM(CASE WHEN clasificada.anio_semana = {semana.AnioSemana} AND clasificada.numero_semana = {semana.NumeroSemana} THEN CONVERT(bigint, 1) ELSE CONVERT(bigint, 0) END) AS semana_{indice + 1}"));
        var columnasSemanasConteoResultado = string.Join(",\n                ", semanas.Select((_, indice) => $"semana_{indice + 1}"));
        var columnasSemanasMatrizResultado = string.Join(",\n                ", semanas.Select((_, indice) => $"matriz.semana_{indice + 1}"));
        var columnasSemanasFinal = string.Join(",\n            ", semanas.Select((_, indice) => $"semana_{indice + 1} AS [Semana {indice + 1}]"));

        var sql = $@"
        WITH pendientes_rankeadas AS
        (
            SELECT
                sc.id_semanal_carga,
                sc.id_entidad_federativa,
                sc.anio_semana,
                sc.numero_semana,
                sc.fecha_inicio_tramo,
                sc.fecha_fin_tramo,
                ROW_NUMBER() OVER
                (
                    PARTITION BY sc.id_entidad_federativa, sc.anio_corte, sc.mes_corte, sc.anio_semana, sc.numero_semana
                    ORDER BY sc.fecha_validacion DESC, sc.id_semanal_carga DESC
                ) AS rn
            FROM dbo.semanal_carga sc
            WHERE sc.anio_corte = @AnioCorte
              AND sc.mes_corte = @MesCorte
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado = N'PENDIENTE_APROBACION'
              AND sc.activo = 1
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
        ),
        pendientes AS
        (
            SELECT id_semanal_carga, id_entidad_federativa, anio_semana, numero_semana, fecha_inicio_tramo, fecha_fin_tramo
            FROM pendientes_rankeadas
            WHERE rn = 1
        ),
        cargas_confirmadas AS
        (
            SELECT sc.id_semanal_carga, sc.id_entidad_federativa, sc.anio_semana, sc.numero_semana, sc.fecha_inicio_tramo, sc.fecha_fin_tramo
            FROM dbo.semanal_carga sc
            WHERE sc.anio_corte = @AnioCorte
              AND sc.mes_corte = @MesCorte
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
              AND sc.activo = 1
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
        ),
        confirmadas_visibles AS
        (
            SELECT confirmada.*
            FROM cargas_confirmadas confirmada
            WHERE @ModoPlano = N'CONFIRMADO'
               OR
               (
                   @ModoPlano = N'MIXTO'
                   AND NOT EXISTS
                   (
                       SELECT 1
                       FROM pendientes pendiente
                       WHERE pendiente.id_entidad_federativa = confirmada.id_entidad_federativa
                         AND pendiente.anio_semana = confirmada.anio_semana
                         AND pendiente.numero_semana = confirmada.numero_semana
                   )
               )
        ),
        cargas_visibles AS
        (
            SELECT id_semanal_carga
            FROM confirmadas_visibles

            UNION

            SELECT id_semanal_carga
            FROM pendientes
            WHERE @ModoPlano IN (N'PREVIO', N'MIXTO')
        ),
        modalidades_configuradas AS
        (
            SELECT DISTINCT configuracion.id_modalidad_delito
            FROM dbo.semanal_carga_delito_configurado configuracion
            INNER JOIN cargas_visibles carga ON carga.id_semanal_carga = configuracion.id_semanal_carga
        ),
        sabana AS
        (
            SELECT
                MIN(COALESCE(orden_legacy.orden_municipal_victimas, orden_legacy.orden_general, sabana.id_delito_sabana)) AS orden_sabana,
                MIN(delito.id_delito) AS orden_delito,
                bien.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana
            FROM dbo.catalogo_delito_sabana sabana
            INNER JOIN modalidades_configuradas configuracion ON configuracion.id_modalidad_delito = sabana.id_modalidad_delito
            INNER JOIN dbo.catalogo_modalidad_delito modalidad ON modalidad.id_modalidad_delito = sabana.id_modalidad_delito AND modalidad.activo = 1
            INNER JOIN dbo.catalogo_subtipo_delito subtipo ON subtipo.id_subtipo_delito = modalidad.id_subtipo_delito AND subtipo.activo = 1
            INNER JOIN dbo.catalogo_delito delito ON delito.id_delito = subtipo.id_delito AND delito.activo = 1
            INNER JOIN dbo.catalogo_bien_juridico bien ON bien.id_bien_juridico = delito.id_bien_juridico AND bien.activo = 1
            LEFT JOIN dbo.catalogo_sabana_orden_legacy orden_legacy ON orden_legacy.bien_juridico = bien.bien_juridico AND orden_legacy.delito_sabana = sabana.delito_sabana AND orden_legacy.subtipo_delito_sabana = sabana.subtipo_delito_sabana AND orden_legacy.modalidad_delito_sabana = sabana.modalidad_delito_sabana AND orden_legacy.activo = 1
            WHERE sabana.activo = 1
            GROUP BY bien.bien_juridico, sabana.delito_sabana, sabana.subtipo_delito_sabana, sabana.modalidad_delito_sabana
        ),
        fuente_victimas AS
        (
            SELECT
                carga.anio_semana,
                carga.numero_semana,
                delito_semanal.id_entidad_federativa AS id_entidad_hechos,
                delito_semanal.id_municipio,
                delito_semanal.id_modalidad_delito,
                delito_semanal.id_grado_consumacion,
                delito_semanal.id_instrumento_comision,
                delito_semanal.id_forma_accion,
                tipo_victima.clave AS tipo_victima_clave,
                sexo.clave AS sexo_clave,
                CASE WHEN TRY_CONVERT(int, victima.edad) = 999 THEN NULL ELSE TRY_CONVERT(int, victima.edad) END AS edad
            FROM dbo.semanal_victima victima
            INNER JOIN confirmadas_visibles carga ON carga.id_semanal_carga = victima.id_semanal_carga
            INNER JOIN dbo.semanal_delito delito_semanal ON delito_semanal.id_semanal_delito = victima.id_semanal_delito AND delito_semanal.activo = 1
            INNER JOIN dbo.semanal_carpeta_investigacion carpeta ON carpeta.id_semanal_carpeta_investigacion = delito_semanal.id_semanal_carpeta_investigacion AND carpeta.activo = 1
            INNER JOIN dbo.catalogo_tipo_victima tipo_victima ON tipo_victima.id_tipo_victima = victima.id_tipo_victima AND tipo_victima.activo = 1
            LEFT JOIN dbo.catalogo_sexo sexo ON sexo.id_sexo = victima.id_sexo AND sexo.activo = 1
            WHERE victima.activo = 1
              AND carpeta.fecha_inicio >= carga.fecha_inicio_tramo
              AND carpeta.fecha_inicio < DATEADD(DAY, 1, carga.fecha_fin_tramo)

            UNION ALL

            SELECT
                carga.anio_semana,
                carga.numero_semana,
                entidad_hechos.id_entidad_federativa,
                municipio.id_municipio,
                modalidad.id_modalidad_delito,
                grado.id_grado_consumacion,
                instrumento.id_instrumento_comision,
                forma.id_forma_accion,
                tipo_victima.clave,
                sexo.clave,
                CASE WHEN TRY_CONVERT(int, NULLIF(victima_tmp.edad, N'')) = 999 THEN NULL ELSE TRY_CONVERT(int, NULLIF(victima_tmp.edad, N'')) END
            FROM pendientes carga
            INNER JOIN dbo.semanal_carga_tmp_victima victima_tmp ON victima_tmp.id_semanal_carga = carga.id_semanal_carga AND victima_tmp.activo = 1
            INNER JOIN dbo.semanal_carga_tmp_delito delito_tmp ON delito_tmp.id_semanal_carga = victima_tmp.id_semanal_carga AND delito_tmp.id_ci = victima_tmp.id_ci AND delito_tmp.id_delito = victima_tmp.id_delito AND delito_tmp.activo = 1
            INNER JOIN dbo.semanal_carga_tmp_carpeta carpeta_tmp ON carpeta_tmp.id_semanal_carga = victima_tmp.id_semanal_carga AND carpeta_tmp.id_ci = victima_tmp.id_ci AND carpeta_tmp.activo = 1
            INNER JOIN dbo.catalogo_modalidad_delito modalidad ON modalidad.clave4 = delito_tmp.clasf_de_dto AND modalidad.activo = 1
            INNER JOIN dbo.catalogo_forma_accion forma ON forma.clave = TRY_CONVERT(tinyint, delito_tmp.forma_acc) AND forma.activo = 1
            INNER JOIN dbo.catalogo_instrumento_comision instrumento ON instrumento.clave = TRY_CONVERT(tinyint, delito_tmp.emto_com_dto) AND instrumento.activo = 1
            INNER JOIN dbo.catalogo_grado_consumacion grado ON grado.clave = TRY_CONVERT(tinyint, delito_tmp.grdo_cons) AND grado.activo = 1
            INNER JOIN dbo.catalogo_entidad_federativa entidad_hechos ON entidad_hechos.id_entidad_federativa = TRY_CONVERT(tinyint, delito_tmp.id_ent_hchos) AND entidad_hechos.activo = 1
            INNER JOIN dbo.catalogo_municipio municipio ON municipio.id_entidad_federativa = entidad_hechos.id_entidad_federativa AND TRY_CONVERT(int, municipio.clave) = TRY_CONVERT(int, delito_tmp.id_mun_hchos) AND municipio.activo = 1
            INNER JOIN dbo.catalogo_tipo_victima tipo_victima ON tipo_victima.clave = TRY_CONVERT(tinyint, victima_tmp.id_tv) AND tipo_victima.activo = 1
            LEFT JOIN dbo.catalogo_sexo sexo ON sexo.clave = TRY_CONVERT(tinyint, NULLIF(victima_tmp.sexo, N'')) AND sexo.activo = 1
            WHERE @ModoPlano IN (N'PREVIO', N'MIXTO')
              AND COALESCE(TRY_CONVERT(date, carpeta_tmp.fha_de_ini, 103), TRY_CONVERT(date, carpeta_tmp.fha_de_ini, 23), TRY_CONVERT(date, carpeta_tmp.fha_de_ini)) >= carga.fecha_inicio_tramo
              AND COALESCE(TRY_CONVERT(date, carpeta_tmp.fha_de_ini, 103), TRY_CONVERT(date, carpeta_tmp.fha_de_ini, 23), TRY_CONVERT(date, carpeta_tmp.fha_de_ini)) < DATEADD(DAY, 1, carga.fecha_fin_tramo)
        ),
        victimas_clasificadas AS
        (
            SELECT
                fuente.*,
                CASE
                    WHEN fuente.tipo_victima_clave = 1 AND fuente.sexo_clave = 1 THEN N'Hombre'
                    WHEN fuente.tipo_victima_clave = 1 AND fuente.sexo_clave = 2 THEN N'Mujer'
                    ELSE N'No identificado'
                END AS sexo,
                CASE
                    WHEN fuente.tipo_victima_clave <> 1 THEN N'No especificado'
                    WHEN fuente.edad IS NULL THEN N'No especificado'
                    WHEN fuente.edad BETWEEN 0 AND 12 THEN N'0 a 12 años'
                    WHEN fuente.edad BETWEEN 13 AND 17 THEN N'13 a 17 años'
                    WHEN fuente.edad BETWEEN 18 AND 29 THEN N'18 a 29 años'
                    WHEN fuente.edad BETWEEN 30 AND 60 THEN N'30 a 60 años'
                    WHEN fuente.edad BETWEEN 61 AND 120 THEN N'Más de 60 años'
                    ELSE N'No especificado'
                END AS rango_edad
            FROM fuente_victimas fuente
        ),
        matriz_municipal_sin_conteo AS
        (
            SELECT
                @AnioCorte AS anio_corte,
                TRY_CONVERT(int, entidad.clave) AS clave_ent,
                entidad.nombre AS entidad,
                TRY_CONVERT(int, CONCAT(TRY_CONVERT(int, entidad.clave), RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, municipio.clave)), 3))) AS clave_municipio_compuesta,
                municipio.nombre AS municipio,
                sabana.orden_sabana,
                sabana.orden_delito,
                sabana.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana,
                N'No identificado' AS sexo,
                N'No especificado' AS rango_edad,
                {columnasSemanasCero}
            FROM dbo.catalogo_entidad_federativa entidad
            INNER JOIN dbo.catalogo_municipio municipio ON municipio.id_entidad_federativa = entidad.id_entidad_federativa AND municipio.activo = 1
            CROSS JOIN sabana
            WHERE entidad.activo = 1
              AND TRY_CONVERT(int, entidad.clave) BETWEEN 1 AND 32
              AND (@IdEntidadFederativa IS NULL OR entidad.id_entidad_federativa = @IdEntidadFederativa)
              AND
              (
                  @ModoPlano <> N'PREVIO'
                  OR EXISTS
                  (
                      SELECT 1
                      FROM pendientes pendiente
                      WHERE pendiente.id_entidad_federativa = entidad.id_entidad_federativa
                  )
              )
        ),
        conteos AS
        (
            SELECT
                @AnioCorte AS anio_corte,
                TRY_CONVERT(int, entidad.clave) AS clave_ent,
                entidad.nombre AS entidad,
                TRY_CONVERT(int, CONCAT(TRY_CONVERT(int, entidad.clave), RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, municipio.clave)), 3))) AS clave_municipio_compuesta,
                municipio.nombre AS municipio,
                MIN(COALESCE(orden_legacy.orden_municipal_victimas, orden_legacy.orden_general, sabana.id_delito_sabana)) AS orden_sabana,
                MIN(delito.id_delito) AS orden_delito,
                bien.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana,
                clasificada.sexo,
                clasificada.rango_edad,
                {columnasSemanasConteo}
            FROM victimas_clasificadas clasificada
            INNER JOIN dbo.catalogo_entidad_federativa entidad ON entidad.id_entidad_federativa = clasificada.id_entidad_hechos AND entidad.activo = 1
            INNER JOIN dbo.catalogo_municipio municipio ON municipio.id_municipio = clasificada.id_municipio AND municipio.id_entidad_federativa = clasificada.id_entidad_hechos AND municipio.activo = 1
            INNER JOIN dbo.catalogo_delito_sabana sabana ON sabana.id_modalidad_delito = clasificada.id_modalidad_delito AND sabana.id_grado_consumacion = clasificada.id_grado_consumacion AND sabana.id_instrumento_comision = clasificada.id_instrumento_comision AND sabana.id_forma_accion = clasificada.id_forma_accion AND sabana.activo = 1
            INNER JOIN dbo.catalogo_modalidad_delito modalidad ON modalidad.id_modalidad_delito = sabana.id_modalidad_delito AND modalidad.activo = 1
            INNER JOIN dbo.catalogo_subtipo_delito subtipo ON subtipo.id_subtipo_delito = modalidad.id_subtipo_delito AND subtipo.activo = 1
            INNER JOIN dbo.catalogo_delito delito ON delito.id_delito = subtipo.id_delito AND delito.activo = 1
            INNER JOIN dbo.catalogo_bien_juridico bien ON bien.id_bien_juridico = delito.id_bien_juridico AND bien.activo = 1
            LEFT JOIN dbo.catalogo_sabana_orden_legacy orden_legacy ON orden_legacy.bien_juridico = bien.bien_juridico AND orden_legacy.delito_sabana = sabana.delito_sabana AND orden_legacy.subtipo_delito_sabana = sabana.subtipo_delito_sabana AND orden_legacy.modalidad_delito_sabana = sabana.modalidad_delito_sabana AND orden_legacy.activo = 1
            WHERE TRY_CONVERT(int, entidad.clave) BETWEEN 1 AND 32
            GROUP BY
                TRY_CONVERT(int, entidad.clave),
                entidad.nombre,
                TRY_CONVERT(int, CONCAT(TRY_CONVERT(int, entidad.clave), RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, municipio.clave)), 3))),
                municipio.nombre,
                bien.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana,
                clasificada.sexo,
                clasificada.rango_edad
        ),
        municipios_con_conteo AS
        (
            SELECT DISTINCT clave_municipio_compuesta
            FROM conteos
        ),
        resultado AS
        (
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
                {columnasSemanasConteoResultado}
            FROM conteos

            UNION ALL

            SELECT
                2 AS bloque_resultado,
                matriz.anio_corte,
                matriz.clave_ent,
                matriz.entidad,
                matriz.clave_municipio_compuesta,
                matriz.municipio,
                matriz.orden_sabana,
                matriz.orden_delito,
                matriz.bien_juridico,
                matriz.delito_sabana,
                matriz.subtipo_delito_sabana,
                matriz.modalidad_delito_sabana,
                matriz.sexo,
                matriz.rango_edad,
                {columnasSemanasMatrizResultado}
            FROM matriz_municipal_sin_conteo matriz
            LEFT JOIN municipios_con_conteo municipio_conteo ON municipio_conteo.clave_municipio_compuesta = matriz.clave_municipio_compuesta
            WHERE municipio_conteo.clave_municipio_compuesta IS NULL
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
            {columnasSemanasFinal}
        FROM resultado
        ORDER BY
            bloque_resultado,
            CASE WHEN bloque_resultado = 1 THEN clave_ent END,
            CASE WHEN bloque_resultado = 1 THEN clave_municipio_compuesta END,
            CASE WHEN bloque_resultado = 1 THEN orden_sabana END,
            CASE WHEN bloque_resultado = 2 THEN orden_sabana END,
            CASE WHEN bloque_resultado = 2 THEN clave_ent END,
            CASE WHEN bloque_resultado = 2 THEN clave_municipio_compuesta END,
            CASE sexo WHEN N'Hombre' THEN 1 WHEN N'Mujer' THEN 2 ELSE 3 END,
            CASE rango_edad
                WHEN N'0 a 12 años' THEN 1
                WHEN N'13 a 17 años' THEN 2
                WHEN N'18 a 29 años' THEN 3
                WHEN N'30 a 60 años' THEN 4
                WHEN N'Más de 60 años' THEN 5
                ELSE 6
            END
        OPTION (RECOMPILE);
        ";

        return QueryDictionaryPlanoAsync(sql, anioCorte, mesCorte, idEntidadFederativa, modoPlano);
    }

    private static List<(int AnioSemana, int NumeroSemana, string Columna)> ObtenerSemanasMes(int anioCorte, int mesCorte)
    {
        var fechaInicio = new DateTime(anioCorte, mesCorte, 1);
        var fechaFin = fechaInicio.AddMonths(1).AddDays(-1);
        var semanas = new List<(int AnioSemana, int NumeroSemana, string Columna)>();

        for (var fecha = fechaInicio; fecha <= fechaFin; fecha = fecha.AddDays(1))
        {
            var anioSemana = ISOWeek.GetYear(fecha);
            var numeroSemana = ISOWeek.GetWeekOfYear(fecha);

            if (semanas.Any(semana => semana.AnioSemana == anioSemana && semana.NumeroSemana == numeroSemana)) continue;

            semanas.Add((anioSemana, numeroSemana, $"Semana {semanas.Count + 1}"));
        }

        return semanas;
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryPlanoAsync(string sql, int anioCorte, int mesCorte, int? idEntidadFederativa, string modoPlano)
    {
        using var connection = _dbConnectionFactory.CrearConexion();

        var filas = await connection.QueryAsync(sql, new
        {
            AnioCorte = anioCorte,
            MesCorte = mesCorte,
            IdEntidadFederativa = idEntidadFederativa,
            ModoPlano = modoPlano
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