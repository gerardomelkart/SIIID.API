using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/semanal/cargas")]
public class SemanalCargasController : ControllerBase
{
    private readonly ISemanalCargaService _semanalCargaService;
    private readonly ISemanalAcusePdfService _semanalAcusePdfService;
    private readonly ISemanalCargaRepository _semanalCargaRepository;
    private readonly IMemoryCache _cache;

    public SemanalCargasController(ISemanalCargaService semanalCargaService, ISemanalAcusePdfService semanalAcusePdfService, ISemanalCargaRepository semanalCargaRepository, IMemoryCache cache)
    {
        _semanalCargaService = semanalCargaService;
        _semanalAcusePdfService = semanalAcusePdfService;
        _semanalCargaRepository = semanalCargaRepository;
        _cache = cache;
    }

    [HttpGet("semana/disponibilidad")]
    public async Task<IActionResult> ValidarSemana([FromQuery] string tipoCarga, [FromQuery] int anioSemana, [FromQuery] int numeroSemana, [FromQuery] int? idEntidadFederativa)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _semanalCargaService.ValidarSemanaAsync(tipoCarga, anioSemana, numeroSemana, idEntidadFederativa, idUsuario);
        return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost("validar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ValidarArchivos([FromForm] SemanalCargaValidacionRequest request)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        var resultado =
            await _semanalCargaService.ValidarArchivosAsync(
                request,
                idUsuario);

        return resultado.EsValido
            ? Ok(resultado)
            : BadRequest(resultado);
    }

    [HttpGet("{codigoReferencia}/diferencias")]
    public async Task<IActionResult> ObtenerDiferencias(string codigoReferencia, [FromQuery] int limitePorSeccion = 100)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        limitePorSeccion = Math.Clamp(
            limitePorSeccion,
            1,
            200);

        var resultado =
            await _semanalCargaService.ObtenerDiferenciasAsync(
                codigoReferencia,
                idUsuario,
                limitePorSeccion);

        return resultado.EsValido
            ? Ok(resultado)
            : BadRequest(resultado);
    }

    [HttpGet("{codigoReferencia}/acuse")]
    public async Task<IActionResult> DescargarAcuse(string codigoReferencia, [FromQuery] int? anioCorte, [FromQuery] int? mesCorte)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        try
        {
            var pdf = await _semanalAcusePdfService.GenerarAcusePrevioAsync(codigoReferencia, idUsuario, anioCorte, mesCorte);

            return File(
                pdf,
                "application/pdf",
                $"INFORME_PREVIO_PRELIMINAR_{codigoReferencia}.pdf");
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    esValido = false,
                    codigo = "ACUSE_SEMANAL_SIN_PERMISO",
                    mensaje = ex.Message
                });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(
                new
                {
                    esValido = false,
                    codigo = "ACUSE_SEMANAL_NO_DISPONIBLE",
                    mensaje = ex.Message
                });
        }
    }

    [HttpPost("confirmar")]
    public async Task<IActionResult> ConfirmarCarga([FromBody] ConfirmarCargaRequest request)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        if (string.IsNullOrWhiteSpace(
                request.CodigoReferencia))
        {
            return BadRequest(
                new ConfirmarCargaResponse
                {
                    EsValido = false,
                    Estado = "SOLICITUD_INVALIDA",
                    Mensaje = "Debe enviar el código de referencia."
                });
        }

        var resultado =
            await _semanalCargaService.ConfirmarCargaAsync(request, idUsuario);

        return resultado.EsValido
            ? Ok(resultado)
            : BadRequest(resultado);
    }

    [HttpGet("{codigoReferencia}/acuse-confirmado")]
    public async Task<IActionResult> DescargarAcuseConfirmado(string codigoReferencia, [FromQuery] int? anioCorte, [FromQuery] int? mesCorte)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        try
        {
            var pdf = await _semanalAcusePdfService.GenerarAcuseConfirmadoAsync(codigoReferencia, idUsuario, anioCorte, mesCorte);

            return File(
                pdf,
                "application/pdf",
                $"ACUSE_PRELIMINAR_{codigoReferencia}.pdf");
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    esValido = false,
                    codigo = "ACUSE_SEMANAL_SIN_PERMISO",
                    mensaje = ex.Message
                });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(
                new
                {
                    esValido = false,
                    codigo = "ACUSE_SEMANAL_NO_DISPONIBLE",
                    mensaje = ex.Message
                });
        }
    }

    [HttpPost("{codigoReferencia}/acuse/ticket")]
    public async Task<IActionResult> CrearTicketAcuse(string codigoReferencia, [FromQuery] bool confirmado, [FromQuery] int? anioCorte, [FromQuery] int? mesCorte)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        if (
            anioCorte.HasValue != mesCorte.HasValue ||
            anioCorte.HasValue && (anioCorte.Value < 2000 || anioCorte.Value > 2100) ||
            mesCorte.HasValue && (mesCorte.Value < 1 || mesCorte.Value > 12)
        )
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "ACUSE_SEMANAL_PERIODO_INVALIDO",
                mensaje = "El periodo solicitado no es válido."
            });
        }

        try
        {
            var pdf = confirmado
                ? await _semanalAcusePdfService.GenerarAcuseConfirmadoAsync(codigoReferencia, idUsuario, anioCorte, mesCorte)
                : await _semanalAcusePdfService.GenerarAcusePrevioAsync(codigoReferencia, idUsuario, anioCorte, mesCorte);

            var carga = await _semanalCargaRepository.ObtenerCargaParaAcuseAsync(codigoReferencia)
                ?? throw new InvalidOperationException("No se encontró la información de la operación.");

            var nombreArchivo = ObtenerNombreArchivoAcuse(carga, confirmado, anioCorte, mesCorte);
            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var cacheKey = $"ACUSE_PRELIMINAR_DOWNLOAD_TICKET:{ticket}";

            _cache.Set(
                cacheKey,
                new SemanalAcuseTicketArchivo(pdf, nombreArchivo),
                new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(5),
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
                });

            return Ok(new
            {
                esValido = true,
                ticket,
                nombreArchivo
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "ACUSE_SEMANAL_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "ACUSE_SEMANAL_NO_DISPONIBLE",
                mensaje = ex.Message
            });
        }
    }

    [AllowAnonymous]
    [HttpGet("acuse/descargar")]
    public IActionResult DescargarAcusePorTicket([FromQuery] string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "ACUSE_SEMANAL_TICKET_REQUERIDO",
                mensaje = "Debe proporcionar un ticket válido."
            });
        }

        var cacheKey = $"ACUSE_PRELIMINAR_DOWNLOAD_TICKET:{ticket}";

        if (!_cache.TryGetValue<SemanalAcuseTicketArchivo>(cacheKey, out var archivo) || archivo == null)
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "ACUSE_SEMANAL_TICKET_INVALIDO",
                mensaje = "El ticket no existe o ya expiró."
            });
        }

        Response.Headers.CacheControl = "no-store";
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{archivo.NombreArchivo}\"";

        return File(archivo.Archivo, "application/pdf");
    }

    private static string ObtenerNombreArchivoAcuse(SemanalCargaAcuseInfo carga, bool confirmado, int? anioCorte, int? mesCorte)
    {
        var usuario = NormalizarNombreArchivo(carga.UsuarioCarga);
        var prefijo = confirmado ? "ACUSE_PRELIMINAR" : "INFORME_PREVIO";

        var periodos = anioCorte.HasValue && mesCorte.HasValue
            ? new[] { (Anio: anioCorte.Value, Mes: mesCorte.Value) }
            : carga.Bloques
                .Select(x => (Anio: x.AnioCorte, Mes: x.MesCorte))
                .Distinct()
                .OrderBy(x => x.Anio)
                .ThenBy(x => x.Mes)
                .ToArray();

        if (periodos.Length == 0)
        {
            periodos = new[] { (Anio: carga.AnioCorte, Mes: carga.MesCorte) };
        }

        var periodo = string.Join(
            "_",
            periodos.Select(x => $"{NormalizarNombreArchivo(ObtenerNombreMes(x.Mes))}_{x.Anio}")
        );

        return $"{prefijo}_{usuario}_{periodo}.pdf";
    }

    private static string ObtenerNombreMes(int mes)
    {
        return mes switch
        {
            1 => "Enero",
            2 => "Febrero",
            3 => "Marzo",
            4 => "Abril",
            5 => "Mayo",
            6 => "Junio",
            7 => "Julio",
            8 => "Agosto",
            9 => "Septiembre",
            10 => "Octubre",
            11 => "Noviembre",
            12 => "Diciembre",
            _ => mes.ToString("00")
        };
    }

    private static string NormalizarNombreArchivo(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "USUARIO";

        valor = valor.Trim().ToUpperInvariant();

        var reemplazos = new Dictionary<char, char>
        {
            { 'Á', 'A' },
            { 'É', 'E' },
            { 'Í', 'I' },
            { 'Ó', 'O' },
            { 'Ú', 'U' },
            { 'Ü', 'U' },
            { 'Ñ', 'N' }
        };

        foreach (var reemplazo in reemplazos) valor = valor.Replace(reemplazo.Key, reemplazo.Value);

        valor = new string(valor.Select(caracter => char.IsLetterOrDigit(caracter) ? caracter : '_').ToArray());

        while (valor.Contains("__")) valor = valor.Replace("__", "_");

        return valor.Trim('_');
    }

    private sealed record SemanalAcuseTicketArchivo(byte[] Archivo, string NombreArchivo);

    private bool ObtenerIdUsuario(out int idUsuario)
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),
            out idUsuario);
    }

    private IActionResult TokenSinUsuario()
    {
        return Unauthorized(
            new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
    }
}