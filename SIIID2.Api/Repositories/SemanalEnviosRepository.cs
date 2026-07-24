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

    public async Task<List<SemanalReporteCargaItem>> ObtenerReporteCargasAsync(int? idEntidadFederativa, int? anioSemana, int? numeroSemana)
    {
        const string sql = @"
        WITH semanas AS
        (
            SELECT
                sc.id_entidad_federativa,
                sc.anio_semana,
                sc.numero_semana,
                COUNT(1) AS intentos
            FROM dbo.semanal_carga sc
            WHERE sc.activo = 1
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.id_entidad_federativa IS NOT NULL
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND (@AnioSemana IS NULL OR sc.anio_semana = @AnioSemana)
              AND (@NumeroSemana IS NULL OR sc.numero_semana = @NumeroSemana)
            GROUP BY
                sc.id_entidad_federativa,
                sc.anio_semana,
                sc.numero_semana
        )
        SELECT
            ef.id_entidad_federativa AS IdEntidadFederativa,
            ef.nombre AS EntidadFederativa,
            ef.clave AS ClaveEntidad,
            s.anio_semana AS AnioSemana,
            s.numero_semana AS NumeroSemana,
            s.intentos AS Intentos,
            ultimo.codigo_referencia AS UltimoIntento,
            ultimo.tipo_carga AS TipoCargaUltimoIntento,
            ultimo.estado AS EstatusUltimoIntento,
            ultimo.fecha_carga_actualizacion AS FechaCargaActualizacion,
            ultimo.fecha_aprobacion AS FechaAprobacion,
            exitosa.fecha_carga_exitosa AS FechaCargaExitosa
        FROM semanas s
        INNER JOIN dbo.catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = s.id_entidad_federativa
           AND ef.activo = 1
        OUTER APPLY
        (
            SELECT TOP (1)
                sc.codigo_referencia,
                sc.tipo_carga,
                sc.estado,
                CASE
                    WHEN sc.estado IN
                    (
                        N'VALIDADO_PENDIENTE',
                        N'VALIDADO_PENDIENTE_ACTUALIZACION',
                        N'PENDIENTE_APROBACION',
                        N'CONFIRMADO',
                        N'CONFIRMADO_ACTUALIZACION',
                        N'RECHAZADO_ADMIN'
                    )
                    THEN sc.fecha_validacion
                    ELSE NULL
                END AS fecha_carga_actualizacion,
                CASE
                    WHEN sc.estado IN
                    (
                        N'CONFIRMADO',
                        N'CONFIRMADO_ACTUALIZACION'
                    )
                    THEN sc.fecha_confirmacion
                    ELSE NULL
                END AS fecha_aprobacion
            FROM dbo.semanal_carga sc
            WHERE sc.id_entidad_federativa = s.id_entidad_federativa
              AND sc.anio_semana = s.anio_semana
              AND sc.numero_semana = s.numero_semana
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.activo = 1
            ORDER BY
                COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) DESC,
                sc.id_semanal_carga DESC
        ) ultimo
        OUTER APPLY
        (
            SELECT
                MIN(sc.fecha_validacion) AS fecha_carga_exitosa
            FROM dbo.semanal_carga sc
            WHERE sc.id_entidad_federativa = s.id_entidad_federativa
              AND sc.anio_semana = s.anio_semana
              AND sc.numero_semana = s.numero_semana
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado IN
              (
                  N'VALIDADO_PENDIENTE',
                  N'VALIDADO_PENDIENTE_ACTUALIZACION',
                  N'PENDIENTE_APROBACION',
                  N'CONFIRMADO',
                  N'CONFIRMADO_ACTUALIZACION'
              )
              AND sc.activo = 1
        ) exitosa
        ORDER BY
            s.anio_semana DESC,
            s.numero_semana DESC,
            ef.nombre;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var registros = (await connection.QueryAsync<SemanalReporteCargaItem>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa,
            AnioSemana = anioSemana,
            NumeroSemana = numeroSemana
        })).ToList();

        foreach (var registro in registros)
        {
            registro.Semana = $"Semana {registro.NumeroSemana}/{registro.AnioSemana}";

            registro.FechaCargaActualizacionTexto = registro.FechaCargaActualizacion.HasValue
                ? registro.FechaCargaActualizacion.Value.ToString("yyyy-MM-dd HH:mm:ss")
                : string.Empty;

            registro.FechaAprobacionTexto = registro.FechaAprobacion.HasValue
                ? registro.FechaAprobacion.Value.ToString("yyyy-MM-dd HH:mm:ss")
                : string.Empty;
        }

        return registros;
    }

    public async Task<bool> ExisteInformacionConfirmadaPlanoAsync(int anioCorte, int mesCorte, int? idEntidadFederativa)
    {
        const string sql = @"
        SELECT CONVERT(bit, CASE WHEN EXISTS
        (
            SELECT 1
            FROM dbo.semanal_carga sc
            WHERE sc.anio_corte = @AnioCorte
              AND sc.mes_corte = @MesCorte
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND sc.activo = 1
              AND
              (
                  sc.tipo_carga = N'CARGA_INICIAL' AND sc.estado = N'CONFIRMADO'
                  OR sc.tipo_carga = N'ACTUALIZACION' AND sc.estado = N'CONFIRMADO_ACTUALIZACION'
              )
        ) THEN 1 ELSE 0 END);
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.ExecuteScalarAsync<bool>(sql, new { AnioCorte = anioCorte, MesCorte = mesCorte, IdEntidadFederativa = idEntidadFederativa });
    }

    public Task<List<IDictionary<string, object?>>> ObtenerPlanoEstatalDelitosAsync(int anioCorte, int mesCorte, int? idEntidadFederativa) => ObtenerPlanoDelitosAsync(anioCorte, mesCorte, idEntidadFederativa, false);

    public Task<List<IDictionary<string, object?>>> ObtenerPlanoMunicipalDelitosAsync(int anioCorte, int mesCorte, int? idEntidadFederativa) => ObtenerPlanoDelitosAsync(anioCorte, mesCorte, idEntidadFederativa, true);

    public Task<List<IDictionary<string, object?>>> ObtenerPlanoEstatalVictimasAsync(int anioCorte, int mesCorte, int? idEntidadFederativa) => ObtenerPlanoVictimasAsync(anioCorte, mesCorte, idEntidadFederativa, false);

    public Task<List<IDictionary<string, object?>>> ObtenerPlanoMunicipalVictimasAsync(int anioCorte, int mesCorte, int? idEntidadFederativa) => ObtenerPlanoVictimasAsync(anioCorte, mesCorte, idEntidadFederativa, true);

    private Task<List<IDictionary<string, object?>>> ObtenerPlanoDelitosAsync(int anioCorte, int mesCorte, int? idEntidadFederativa, bool municipal)
    {
        var columnasMatrizMunicipio = municipal
            ? @",
                TRY_CONVERT(int, CONCAT(TRY_CONVERT(int, ef.clave), RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun_matriz.clave)), 3))) AS clave_municipio_compuesta,
                mun_matriz.nombre AS municipio"
            : string.Empty;

        var joinMatrizMunicipio = municipal
            ? @"
            INNER JOIN dbo.catalogo_municipio mun_matriz ON mun_matriz.id_entidad_federativa = ef.id_entidad_federativa AND mun_matriz.activo = 1"
            : string.Empty;

        var columnasConteoMunicipio = municipal
            ? @",
                TRY_CONVERT(int, CONCAT(TRY_CONVERT(int, efh.clave), RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3))) AS clave_municipio_compuesta"
            : string.Empty;

        var agrupacionConteoMunicipio = municipal
            ? @",
                TRY_CONVERT(int, CONCAT(TRY_CONVERT(int, efh.clave), RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3)))"
            : string.Empty;

        var columnasFinalMunicipio = municipal
            ? @",
            m.clave_municipio_compuesta AS [Cve. Municipio],
            m.municipio AS [Municipio]"
            : string.Empty;

        var unionFinalMunicipio = municipal ? "AND c.clave_municipio_compuesta = m.clave_municipio_compuesta" : string.Empty;
        var ordenFinalMunicipio = municipal ? ", m.clave_municipio_compuesta" : string.Empty;

        var sql = $@"
        WITH cargas_confirmadas AS
        (
            SELECT sc.id_semanal_carga, sc.id_entidad_federativa, sc.fecha_inicio_tramo, sc.fecha_fin_tramo
            FROM dbo.semanal_carga sc
            WHERE sc.anio_corte = @AnioCorte
              AND sc.mes_corte = @MesCorte
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND sc.activo = 1
              AND
              (
                  sc.tipo_carga = N'CARGA_INICIAL' AND sc.estado = N'CONFIRMADO'
                  OR sc.tipo_carga = N'ACTUALIZACION' AND sc.estado = N'CONFIRMADO_ACTUALIZACION'
              )
        ),
        modalidades_configuradas AS
        (
            SELECT DISTINCT configuracion.id_modalidad_delito
            FROM dbo.semanal_carga_delito_configurado configuracion
            INNER JOIN cargas_confirmadas carga ON carga.id_semanal_carga = configuracion.id_semanal_carga
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
        ),
        conteos AS
        (
            SELECT
                TRY_CONVERT(int, efh.clave) AS clave_ent
                {columnasConteoMunicipio},
                bien.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana,
                COUNT_BIG(1) AS cantidad_delitos
            FROM dbo.semanal_delito delito_semanal
            INNER JOIN cargas_confirmadas carga ON carga.id_semanal_carga = delito_semanal.id_semanal_carga
            INNER JOIN dbo.semanal_carpeta_investigacion carpeta ON carpeta.id_semanal_carpeta_investigacion = delito_semanal.id_semanal_carpeta_investigacion AND carpeta.activo = 1
            INNER JOIN dbo.catalogo_entidad_federativa efh ON efh.id_entidad_federativa = delito_semanal.id_entidad_federativa AND efh.activo = 1
            INNER JOIN dbo.catalogo_municipio mun ON mun.id_municipio = delito_semanal.id_municipio AND mun.id_entidad_federativa = delito_semanal.id_entidad_federativa AND mun.activo = 1
            INNER JOIN dbo.catalogo_delito_sabana sabana ON sabana.id_modalidad_delito = delito_semanal.id_modalidad_delito AND sabana.id_grado_consumacion = delito_semanal.id_grado_consumacion AND sabana.id_instrumento_comision = delito_semanal.id_instrumento_comision AND sabana.id_forma_accion = delito_semanal.id_forma_accion AND sabana.activo = 1
            INNER JOIN dbo.catalogo_modalidad_delito modalidad ON modalidad.id_modalidad_delito = sabana.id_modalidad_delito AND modalidad.activo = 1
            INNER JOIN dbo.catalogo_subtipo_delito subtipo ON subtipo.id_subtipo_delito = modalidad.id_subtipo_delito AND subtipo.activo = 1
            INNER JOIN dbo.catalogo_delito delito ON delito.id_delito = subtipo.id_delito AND delito.activo = 1
            INNER JOIN dbo.catalogo_bien_juridico bien ON bien.id_bien_juridico = delito.id_bien_juridico AND bien.activo = 1
            WHERE delito_semanal.activo = 1
              AND carpeta.fecha_inicio >= carga.fecha_inicio_tramo
              AND carpeta.fecha_inicio < DATEADD(DAY, 1, carga.fecha_fin_tramo)
            GROUP BY
                TRY_CONVERT(int, efh.clave)
                {agrupacionConteoMunicipio},
                bien.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana
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
            CASE WHEN @MesCorte = 1 THEN ISNULL(c.cantidad_delitos, 0) ELSE 0 END AS [Enero],
            CASE WHEN @MesCorte = 2 THEN ISNULL(c.cantidad_delitos, 0) ELSE 0 END AS [Febrero],
            CASE WHEN @MesCorte = 3 THEN ISNULL(c.cantidad_delitos, 0) ELSE 0 END AS [Marzo],
            CASE WHEN @MesCorte = 4 THEN ISNULL(c.cantidad_delitos, 0) ELSE 0 END AS [Abril],
            CASE WHEN @MesCorte = 5 THEN ISNULL(c.cantidad_delitos, 0) ELSE 0 END AS [Mayo],
            CASE WHEN @MesCorte = 6 THEN ISNULL(c.cantidad_delitos, 0) ELSE 0 END AS [Junio],
            CASE WHEN @MesCorte = 7 THEN ISNULL(c.cantidad_delitos, 0) ELSE 0 END AS [Julio],
            CASE WHEN @MesCorte = 8 THEN ISNULL(c.cantidad_delitos, 0) ELSE 0 END AS [Agosto],
            CASE WHEN @MesCorte = 9 THEN ISNULL(c.cantidad_delitos, 0) ELSE 0 END AS [Septiembre],
            CASE WHEN @MesCorte = 10 THEN ISNULL(c.cantidad_delitos, 0) ELSE 0 END AS [Octubre],
            CASE WHEN @MesCorte = 11 THEN ISNULL(c.cantidad_delitos, 0) ELSE 0 END AS [Noviembre],
            CASE WHEN @MesCorte = 12 THEN ISNULL(c.cantidad_delitos, 0) ELSE 0 END AS [Diciembre]
        FROM matriz m
        LEFT JOIN conteos c ON c.clave_ent = m.clave_ent
            {unionFinalMunicipio}
           AND c.bien_juridico = m.bien_juridico
           AND c.delito_sabana = m.delito_sabana
           AND c.subtipo_delito_sabana = m.subtipo_delito_sabana
           AND c.modalidad_delito_sabana = m.modalidad_delito_sabana
        ORDER BY m.clave_ent, m.orden_sabana, m.orden_delito, m.subtipo_delito_sabana, m.modalidad_delito_sabana {ordenFinalMunicipio}
        OPTION (RECOMPILE);
        ";

        return QueryDictionaryPlanoAsync(sql, anioCorte, mesCorte, idEntidadFederativa);
    }

    private Task<List<IDictionary<string, object?>>> ObtenerPlanoVictimasAsync(int anioCorte, int mesCorte, int? idEntidadFederativa, bool municipal)
    {
        var columnasMatrizMunicipio = municipal
            ? @",
                TRY_CONVERT(int, CONCAT(TRY_CONVERT(int, ef.clave), RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun_matriz.clave)), 3))) AS clave_municipio_compuesta,
                mun_matriz.nombre AS municipio"
            : string.Empty;

        var joinMatrizMunicipio = municipal
            ? @"
            INNER JOIN dbo.catalogo_municipio mun_matriz ON mun_matriz.id_entidad_federativa = ef.id_entidad_federativa AND mun_matriz.activo = 1"
            : string.Empty;

        var columnasFuenteMunicipio = municipal
            ? @",
                TRY_CONVERT(int, CONCAT(TRY_CONVERT(int, efh.clave), RIGHT('000' + CONVERT(varchar(3), TRY_CONVERT(int, mun.clave)), 3))) AS clave_municipio_compuesta"
            : string.Empty;

        var columnasConteoMunicipio = municipal ? ", clasificada.clave_municipio_compuesta" : string.Empty;
        var agrupacionConteoMunicipio = municipal ? ", clasificada.clave_municipio_compuesta" : string.Empty;

        var columnasFinalMunicipio = municipal
            ? @",
            m.clave_municipio_compuesta AS [Cve. Municipio],
            m.municipio AS [Municipio]"
            : string.Empty;

        var unionFinalMunicipio = municipal ? "AND c.clave_municipio_compuesta = m.clave_municipio_compuesta" : string.Empty;
        var ordenFinalMunicipio = municipal ? ", m.clave_municipio_compuesta" : string.Empty;

        var sql = $@"
        WITH cargas_confirmadas AS
        (
            SELECT sc.id_semanal_carga, sc.id_entidad_federativa, sc.fecha_inicio_tramo, sc.fecha_fin_tramo
            FROM dbo.semanal_carga sc
            WHERE sc.anio_corte = @AnioCorte
              AND sc.mes_corte = @MesCorte
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND sc.activo = 1
              AND
              (
                  sc.tipo_carga = N'CARGA_INICIAL' AND sc.estado = N'CONFIRMADO'
                  OR sc.tipo_carga = N'ACTUALIZACION' AND sc.estado = N'CONFIRMADO_ACTUALIZACION'
              )
        ),
        modalidades_configuradas AS
        (
            SELECT DISTINCT configuracion.id_modalidad_delito
            FROM dbo.semanal_carga_delito_configurado configuracion
            INNER JOIN cargas_confirmadas carga ON carga.id_semanal_carga = configuracion.id_semanal_carga
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
        ),
        fuente_victimas AS
        (
            SELECT
                TRY_CONVERT(int, efh.clave) AS clave_ent
                {columnasFuenteMunicipio},
                bien.bien_juridico,
                sabana.delito_sabana,
                sabana.subtipo_delito_sabana,
                sabana.modalidad_delito_sabana,
                tipo_victima.clave AS tipo_victima_clave,
                sexo.clave AS sexo_clave,
                sexo.descripcion AS sexo_descripcion,
                TRY_CONVERT(int, victima.edad) AS edad
            FROM dbo.semanal_victima victima
            INNER JOIN cargas_confirmadas carga ON carga.id_semanal_carga = victima.id_semanal_carga
            INNER JOIN dbo.semanal_delito delito_semanal ON delito_semanal.id_semanal_delito = victima.id_semanal_delito AND delito_semanal.activo = 1
            INNER JOIN dbo.semanal_carpeta_investigacion carpeta ON carpeta.id_semanal_carpeta_investigacion = delito_semanal.id_semanal_carpeta_investigacion AND carpeta.activo = 1
            INNER JOIN dbo.catalogo_entidad_federativa efh ON efh.id_entidad_federativa = delito_semanal.id_entidad_federativa AND efh.activo = 1
            INNER JOIN dbo.catalogo_municipio mun ON mun.id_municipio = delito_semanal.id_municipio AND mun.id_entidad_federativa = delito_semanal.id_entidad_federativa AND mun.activo = 1
            INNER JOIN dbo.catalogo_delito_sabana sabana ON sabana.id_modalidad_delito = delito_semanal.id_modalidad_delito AND sabana.id_grado_consumacion = delito_semanal.id_grado_consumacion AND sabana.id_instrumento_comision = delito_semanal.id_instrumento_comision AND sabana.id_forma_accion = delito_semanal.id_forma_accion AND sabana.activo = 1
            INNER JOIN dbo.catalogo_modalidad_delito modalidad ON modalidad.id_modalidad_delito = sabana.id_modalidad_delito AND modalidad.activo = 1
            INNER JOIN dbo.catalogo_subtipo_delito subtipo ON subtipo.id_subtipo_delito = modalidad.id_subtipo_delito AND subtipo.activo = 1
            INNER JOIN dbo.catalogo_delito delito ON delito.id_delito = subtipo.id_delito AND delito.activo = 1
            INNER JOIN dbo.catalogo_bien_juridico bien ON bien.id_bien_juridico = delito.id_bien_juridico AND bien.activo = 1
            INNER JOIN dbo.catalogo_tipo_victima tipo_victima ON tipo_victima.id_tipo_victima = victima.id_tipo_victima AND tipo_victima.activo = 1
            LEFT JOIN dbo.catalogo_sexo sexo ON sexo.id_sexo = victima.id_sexo AND sexo.activo = 1
            WHERE victima.activo = 1
              AND carpeta.fecha_inicio >= carga.fecha_inicio_tramo
              AND carpeta.fecha_inicio < DATEADD(DAY, 1, carga.fecha_fin_tramo)
        ),
        victimas_clasificadas AS
        (
            SELECT
                fuente.*,
                CASE WHEN fuente.tipo_victima_clave = 1 AND fuente.sexo_clave IN (1, 2, 3) THEN fuente.sexo_descripcion ELSE N'No identificado' END AS sexo,
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
        conteos AS
        (
            SELECT
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
            GROUP BY
                clasificada.clave_ent
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
            CASE WHEN @MesCorte = 1 THEN ISNULL(c.cantidad_victimas, 0) ELSE 0 END AS [Enero],
            CASE WHEN @MesCorte = 2 THEN ISNULL(c.cantidad_victimas, 0) ELSE 0 END AS [Febrero],
            CASE WHEN @MesCorte = 3 THEN ISNULL(c.cantidad_victimas, 0) ELSE 0 END AS [Marzo],
            CASE WHEN @MesCorte = 4 THEN ISNULL(c.cantidad_victimas, 0) ELSE 0 END AS [Abril],
            CASE WHEN @MesCorte = 5 THEN ISNULL(c.cantidad_victimas, 0) ELSE 0 END AS [Mayo],
            CASE WHEN @MesCorte = 6 THEN ISNULL(c.cantidad_victimas, 0) ELSE 0 END AS [Junio],
            CASE WHEN @MesCorte = 7 THEN ISNULL(c.cantidad_victimas, 0) ELSE 0 END AS [Julio],
            CASE WHEN @MesCorte = 8 THEN ISNULL(c.cantidad_victimas, 0) ELSE 0 END AS [Agosto],
            CASE WHEN @MesCorte = 9 THEN ISNULL(c.cantidad_victimas, 0) ELSE 0 END AS [Septiembre],
            CASE WHEN @MesCorte = 10 THEN ISNULL(c.cantidad_victimas, 0) ELSE 0 END AS [Octubre],
            CASE WHEN @MesCorte = 11 THEN ISNULL(c.cantidad_victimas, 0) ELSE 0 END AS [Noviembre],
            CASE WHEN @MesCorte = 12 THEN ISNULL(c.cantidad_victimas, 0) ELSE 0 END AS [Diciembre]
        FROM matriz m
        LEFT JOIN conteos c ON c.clave_ent = m.clave_ent
            {unionFinalMunicipio}
           AND c.bien_juridico = m.bien_juridico
           AND c.delito_sabana = m.delito_sabana
           AND c.subtipo_delito_sabana = m.subtipo_delito_sabana
           AND c.modalidad_delito_sabana = m.modalidad_delito_sabana
           AND c.sexo = m.sexo
           AND c.rango_edad = m.rango_edad
        ORDER BY m.clave_ent, m.orden_sabana, m.orden_delito, m.subtipo_delito_sabana, m.modalidad_delito_sabana, m.orden_sexo, m.orden_rango {ordenFinalMunicipio}
        OPTION (RECOMPILE);
        ";

        return QueryDictionaryPlanoAsync(sql, anioCorte, mesCorte, idEntidadFederativa);
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryPlanoAsync(string sql, int anioCorte, int mesCorte, int? idEntidadFederativa)
    {
        using var connection = _dbConnectionFactory.CrearConexion();

        var filas = await connection.QueryAsync(sql, new
        {
            AnioCorte = anioCorte,
            MesCorte = mesCorte,
            IdEntidadFederativa = idEntidadFederativa
        }, commandTimeout: 300);

        return filas.Select(fila => ((IDictionary<string, object?>)fila).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)).Cast<IDictionary<string, object?>>().ToList();
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