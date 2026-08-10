using System.Globalization;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Data;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/recordatorios/carga")]
public class RecordatoriosCargaController : ControllerBase
{
    private const string RolEnlaceEstatal = "ENLACE_ESTATAL";
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public RecordatoriosCargaController(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    [Authorize(Policy = "MODULO_MENSUAL")]
    [HttpGet("mensual")]
    public async Task<IActionResult> ObtenerMensual()
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var usuario = await ObtenerUsuarioAsync(idUsuario, "MENSUAL");

        if (!DebeMostrarRecordatorio(usuario)) return Ok(SinPendiente());

        var fechaActual = ObtenerFechaActual();
        var corte = new DateTime(fechaActual.Year, fechaActual.Month, 1).AddMonths(-1);

        const string sql = @"
        SELECT CONVERT
        (
            bit,
            CASE
                WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.carga carga
                    WHERE carga.id_entidad_federativa = @IdEntidadFederativa
                      AND carga.anio_corte = @AnioCorte
                      AND carga.mes_corte = @MesCorte
                      AND carga.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
                      AND carga.estado IN
                      (
                          N'PENDIENTE_APROBACION',
                          N'CONFIRMADO',
                          N'CONFIRMADO_ACTUALIZACION'
                      )
                      AND carga.activo = 1
                )
                THEN 1
                ELSE 0
            END
        );";

        using var connection = _dbConnectionFactory.CrearConexion();

        var cargaEnviada = await connection.QuerySingleAsync<bool>(sql, new
        {
            usuario!.IdEntidadFederativa,
            AnioCorte = corte.Year,
            MesCorte = corte.Month
        });

        if (cargaEnviada) return Ok(SinPendiente());

        var periodo = $"{ObtenerNombreMes(corte.Month)} de {corte.Year}";

        return Ok(new RecordatorioCargaResponse
        {
            HayPendiente = true,
            Titulo = "Carga consolidada pendiente",
            Mensaje = $"Falta cargar la información correspondiente al corte de {periodo}. Favor de realizar la carga que corresponda.",
            Periodo = periodo
        });
    }

    [Authorize(Policy = "MODULO_SEMANAL")]
    [HttpGet("semanal")]
    public async Task<IActionResult> ObtenerSemanal()
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var usuario = await ObtenerUsuarioAsync(idUsuario, "SEMANAL");

        if (!DebeMostrarRecordatorio(usuario)) return Ok(SinPendiente());

        var fechaActual = ObtenerFechaActual();
        var diasDesdeLunes = ((int)fechaActual.DayOfWeek + 6) % 7;
        var lunesActual = fechaActual.AddDays(-diasDesdeLunes);
        var fechaInicio = lunesActual.AddDays(-7);
        var fechaFin = fechaInicio.AddDays(6);

        const string sql = @"
        WITH delitos_habilitados AS
        (
            SELECT
                cd.id_delito,
                cd.clave2,
                cd.delito,
                MIN(configuracion.orden) AS orden
            FROM dbo.semanal_configuracion_delito configuracion
            INNER JOIN dbo.catalogo_modalidad_delito md
                ON md.id_modalidad_delito = configuracion.id_modalidad_delito
               AND md.activo = 1
            INNER JOIN dbo.catalogo_subtipo_delito sd
                ON sd.id_subtipo_delito = md.id_subtipo_delito
               AND sd.activo = 1
            INNER JOIN dbo.catalogo_delito cd
                ON cd.id_delito = sd.id_delito
               AND cd.activo = 1
            WHERE configuracion.activo = 1
            GROUP BY
                cd.id_delito,
                cd.clave2,
                cd.delito
        ),
        cargas_periodo AS
        (
            SELECT DISTINCT
                sc.id_semanal_carga,
                sc.estado,
                COALESCE
                (
                    sc.fecha_confirmacion,
                    sc.fecha_validacion,
                    sc.fecha_carga
                ) AS fecha_movimiento
            FROM dbo.semanal_carga sc
            INNER JOIN dbo.semanal_carga_bloque bloque
                ON bloque.id_semanal_carga = sc.id_semanal_carga
               AND bloque.activo = 1
            WHERE sc.id_entidad_federativa = @IdEntidadFederativa
              AND bloque.fecha_inicio_semana = @FechaInicioSemana
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.activo = 1
        ),
        delitos_operacion AS
        (
            SELECT DISTINCT
                carga.id_semanal_carga,
                carga.estado,
                carga.fecha_movimiento,
                sd.id_delito
            FROM cargas_periodo carga
            INNER JOIN dbo.semanal_carga_tmp_delito delito
                ON delito.id_semanal_carga = carga.id_semanal_carga
               AND delito.incluido = 1
               AND delito.activo = 1
            INNER JOIN dbo.semanal_carga_tmp_carpeta carpeta
                ON carpeta.id_semanal_carga = delito.id_semanal_carga
               AND carpeta.id_ci = delito.id_ci
               AND carpeta.incluido = 1
               AND carpeta.activo = 1
            INNER JOIN dbo.catalogo_modalidad_delito md
                ON LTRIM(RTRIM(md.clave4)) = LTRIM(RTRIM(delito.clasf_de_dto))
               AND md.activo = 1
            INNER JOIN dbo.catalogo_subtipo_delito sd
                ON sd.id_subtipo_delito = md.id_subtipo_delito
               AND sd.activo = 1
            WHERE COALESCE
            (
                TRY_CONVERT(date, carpeta.fha_de_ini, 103),
                TRY_CONVERT(date, carpeta.fha_de_ini)
            ) >= @FechaInicioSemana
              AND COALESCE
              (
                  TRY_CONVERT(date, carpeta.fha_de_ini, 103),
                  TRY_CONVERT(date, carpeta.fha_de_ini)
              ) < DATEADD(DAY, 1, @FechaFinSemana)

            UNION

            SELECT DISTINCT
                carga.id_semanal_carga,
                carga.estado,
                carga.fecha_movimiento,
                sd.id_delito
            FROM cargas_periodo carga
            INNER JOIN dbo.semanal_delito delito
                ON delito.id_semanal_carga = carga.id_semanal_carga
               AND delito.activo = 1
            INNER JOIN dbo.semanal_carpeta_investigacion carpeta
                ON carpeta.id_semanal_carpeta_investigacion = delito.id_semanal_carpeta_investigacion
               AND carpeta.id_semanal_carga = delito.id_semanal_carga
               AND carpeta.activo = 1
            INNER JOIN dbo.catalogo_modalidad_delito md
                ON md.id_modalidad_delito = delito.id_modalidad_delito
               AND md.activo = 1
            INNER JOIN dbo.catalogo_subtipo_delito sd
                ON sd.id_subtipo_delito = md.id_subtipo_delito
               AND sd.activo = 1
            WHERE carpeta.fecha_inicio >= @FechaInicioSemana
              AND carpeta.fecha_inicio < DATEADD(DAY, 1, @FechaFinSemana)
        ),
        ultimo_estado_delito AS
        (
            SELECT
                operacion.id_delito,
                operacion.estado,
                ROW_NUMBER() OVER
                (
                    PARTITION BY operacion.id_delito
                    ORDER BY
                        operacion.fecha_movimiento DESC,
                        operacion.id_semanal_carga DESC
                ) AS numero
            FROM delitos_operacion operacion
        )
        SELECT
            habilitado.delito
        FROM delitos_habilitados habilitado
        LEFT JOIN ultimo_estado_delito ultimo
            ON ultimo.id_delito = habilitado.id_delito
           AND ultimo.numero = 1
           AND ultimo.estado IN
           (
               N'PENDIENTE_APROBACION',
               N'CONFIRMADO',
               N'CONFIRMADO_ACTUALIZACION'
           )
        WHERE ultimo.id_delito IS NULL
        ORDER BY
            habilitado.orden,
            habilitado.clave2;";

        using var connection = _dbConnectionFactory.CrearConexion();

        var delitos = (await connection.QueryAsync<string>(sql, new
        {
            usuario!.IdEntidadFederativa,
            FechaInicioSemana = fechaInicio,
            FechaFinSemana = fechaFin
        })).ToList();

        if (delitos.Count == 0) return Ok(SinPendiente());

        var periodo = FormatearRango(fechaInicio, fechaFin);
        var detalleDelitos = $" para: {string.Join(", ", delitos)}";

        return Ok(new RecordatorioCargaResponse
        {
            HayPendiente = true,
            Titulo = "Carga preliminar pendiente",
            Mensaje = $"Falta cargar la información correspondiente a la semana {periodo}{detalleDelitos}. Favor de realizar la carga correspondiente a la entidad.",
            Periodo = periodo,
            Delitos = delitos
        });
    }

    private async Task<RecordatorioCargaUsuarioInfo?> ObtenerUsuarioAsync(int idUsuario, string claveModulo)
    {
        const string sql = @"
        SELECT
            u.id_usuario AS IdUsuario,
            u.id_entidad_federativa AS IdEntidadFederativa,
            r.rol AS Rol,
            CONVERT(bit, um.habilita_carga) AS HabilitaCarga
        FROM dbo.usuario u
        INNER JOIN dbo.roles r
            ON r.id_rol = u.id_rol
           AND r.activo = 1
        INNER JOIN dbo.usuario_modulo um
            ON um.id_usuario = u.id_usuario
           AND um.habilitado = 1
           AND um.activo = 1
        INNER JOIN dbo.catalogo_modulo modulo
            ON modulo.id_modulo = um.id_modulo
           AND modulo.clave = @ClaveModulo
           AND modulo.activo = 1
        WHERE u.id_usuario = @IdUsuario
          AND u.activo = 1;";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<RecordatorioCargaUsuarioInfo>(sql, new
        {
            IdUsuario = idUsuario,
            ClaveModulo = claveModulo
        });
    }

    private static bool DebeMostrarRecordatorio(RecordatorioCargaUsuarioInfo? usuario)
    {
        return usuario != null &&
               usuario.HabilitaCarga &&
               usuario.IdEntidadFederativa.HasValue &&
               usuario.IdEntidadFederativa.Value > 0 &&
               string.Equals(usuario.Rol, RolEnlaceEstatal, StringComparison.OrdinalIgnoreCase);
    }

    private static RecordatorioCargaResponse SinPendiente() => new();

    private static string FormatearRango(DateTime fechaInicio, DateTime fechaFin)
    {
        if (fechaInicio.Year == fechaFin.Year && fechaInicio.Month == fechaFin.Month)
        {
            return $"del {fechaInicio.Day} al {fechaFin.Day} de {ObtenerNombreMes(fechaFin.Month)} de {fechaFin.Year}";
        }

        if (fechaInicio.Year == fechaFin.Year)
        {
            return $"del {fechaInicio.Day} de {ObtenerNombreMes(fechaInicio.Month)} al {fechaFin.Day} de {ObtenerNombreMes(fechaFin.Month)} de {fechaFin.Year}";
        }

        return $"del {fechaInicio.Day} de {ObtenerNombreMes(fechaInicio.Month)} de {fechaInicio.Year} al {fechaFin.Day} de {ObtenerNombreMes(fechaFin.Month)} de {fechaFin.Year}";
    }

    private static string ObtenerNombreMes(int mes) => CultureInfo.GetCultureInfo("es-MX").DateTimeFormat.GetMonthName(mes);

    private static DateTime ObtenerFechaActual()
    {
        var zonaHoraria = ObtenerZonaHoraria();

        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zonaHoraria).Date;
    }

    private static TimeZoneInfo ObtenerZonaHoraria()
    {
        foreach (var idZonaHoraria in new[] { "America/Mexico_City", "Central Standard Time (Mexico)" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(idZonaHoraria);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    private bool ObtenerIdUsuario(out int idUsuario) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idUsuario);

    private IActionResult TokenSinUsuario()
    {
        return Unauthorized(new
        {
            esValido = false,
            codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
            mensaje = "El token no contiene un id de usuario válido.",
            traceId = HttpContext.TraceIdentifier
        });
    }

    private sealed class RecordatorioCargaUsuarioInfo
    {
        public int IdUsuario { get; set; }
        public int? IdEntidadFederativa { get; set; }
        public string Rol { get; set; } = string.Empty;
        public bool HabilitaCarga { get; set; }
    }
}

public class RecordatorioCargaResponse
{
    public bool HayPendiente { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string Periodo { get; set; } = string.Empty;
    public List<string> Delitos { get; set; } = new();
}