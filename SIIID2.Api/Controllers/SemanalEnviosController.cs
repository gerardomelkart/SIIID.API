using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using SIIID2.Api.Models;
using System.Security.Cryptography;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/semanal/envios")]
public class SemanalEnviosController : ControllerBase
{
    private readonly ISemanalEnviosService _semanalEnviosService;
    private readonly IMemoryCache _cache;

    public SemanalEnviosController(ISemanalEnviosService semanalEnviosService, IMemoryCache cache)
    {
        _semanalEnviosService = semanalEnviosService;
        _cache = cache;
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerEnvios([FromQuery] int? idEntidadFederativa = null, [FromQuery] int? idUsuarioCarga = null, [FromQuery] int? anioCorte = null, [FromQuery] int? mesCorte = null, [FromQuery] string? tipoCarga = null, [FromQuery] string? estado = null)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        try
        {
            var registros = await _semanalEnviosService.ObtenerEnviosAsync(
                idUsuario,
                idEntidadFederativa,
                idUsuarioCarga,
                anioCorte,
                mesCorte,
                tipoCarga,
                estado);

            return Ok(new
            {
                esValido = true,
                idEntidadFederativa,
                idUsuarioCarga,
                anioCorte,
                mesCorte,
                total = registros.Count,
                registros
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "SEMANAL_ENVIOS_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
    }

    [HttpGet("reporte-cargas")]
    public async Task<IActionResult> ObtenerReporteCargas([FromQuery] int? idEntidadFederativa = null, [FromQuery] int? idUsuarioCarga = null, [FromQuery] int? anioCorte = null, [FromQuery] int? mesCorte = null)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        try
        {
            var registros = await _semanalEnviosService.ObtenerReporteCargasAsync(
                idUsuario,
                idEntidadFederativa,
                idUsuarioCarga,
                anioCorte,
                mesCorte);

            return Ok(new
            {
                esValido = true,
                idEntidadFederativa,
                idUsuarioCarga,
                anioCorte,
                mesCorte,
                total = registros.Count,
                registros
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "SEMANAL_REPORTE_CARGAS_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
    }

    [HttpGet("{codigoReferencia}/archivos")]
    public async Task<IActionResult> DescargarArchivos(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        try
        {
            var zip = await _semanalEnviosService.GenerarZipArchivosAsync(idUsuario, codigoReferencia);
            return File(zip.Archivo, "application/zip", zip.NombreArchivo);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "SEMANAL_ENVIOS_ARCHIVOS_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                esValido = false,
                codigo = "SEMANAL_ENVIO_NO_ENCONTRADO",
                mensaje = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "SEMANAL_ENVIOS_ARCHIVOS_NO_DISPONIBLES",
                mensaje = ex.Message
            });
        }
    }

    [HttpPost("acuses/ticket")]
    public async Task<IActionResult> CrearTicketDescargaAcuses([FromQuery] int anioCorte, [FromQuery] int mesCorte, [FromQuery] int? idEntidadFederativa = null, [FromQuery] int? idUsuarioCarga = null)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        try
        {
            var zip = await _semanalEnviosService.GenerarZipAcusesAsync(
                idUsuario,
                anioCorte,
                mesCorte,
                idEntidadFederativa,
                idUsuarioCarga);

            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var cacheKey = $"ACUSES_PRELIMINARES_DOWNLOAD_TICKET:{ticket}";

            _cache.Set(
                cacheKey,
                zip,
                new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(5),
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
                });

            return Ok(new
            {
                esValido = true,
                ticket
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "SEMANAL_ACUSES_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "SEMANAL_ACUSES_NO_DISPONIBLES",
                mensaje = ex.Message
            });
        }
    }

    [AllowAnonymous]
    [HttpGet("acuses/descargar")]
    public IActionResult DescargarAcusesPorTicket([FromQuery] string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "SEMANAL_ACUSES_TICKET_REQUERIDO",
                mensaje = "Debe proporcionar un ticket de descarga válido."
            });
        }

        var cacheKey = $"ACUSES_PRELIMINARES_DOWNLOAD_TICKET:{ticket}";

        if (!_cache.TryGetValue<InformeArchivoZipResponse>(cacheKey, out var zip) || zip == null)
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "SEMANAL_ACUSES_TICKET_INVALIDO",
                mensaje = "El ticket de descarga no existe o ya expiró."
            });
        }

        _cache.Remove(cacheKey);
        Response.Headers.CacheControl = "no-store";

        return File(zip.Archivo, "application/zip", zip.NombreArchivo);
    }

    [HttpPost("planos/ticket")]
    public async Task<IActionResult> CrearTicketDescargaPlanos([FromQuery] int anioCorte, [FromQuery] int mesCorte, [FromQuery] string tipo = "COMPLETA", [FromQuery] string modo = "CONFIRMADO")
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        try
        {
            var zip = await _semanalEnviosService.GenerarZipPlanosAsync(idUsuario, anioCorte, mesCorte, tipo, modo);
            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var cacheKey = $"PLANOS_SEMANALES_DOWNLOAD_TICKET:{ticket}";

            _cache.Set(cacheKey, zip, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(5),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });

            return Ok(new { esValido = true, ticket });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "SEMANAL_PLANOS_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "SEMANAL_PLANOS_NO_DISPONIBLES",
                mensaje = ex.Message
            });
        }
    }

    [AllowAnonymous]
    [HttpGet("planos/descargar")]
    public IActionResult DescargarPlanosPorTicket([FromQuery] string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "SEMANAL_PLANOS_TICKET_REQUERIDO",
                mensaje = "Debe proporcionar un ticket de descarga válido."
            });
        }

        var cacheKey = $"PLANOS_SEMANALES_DOWNLOAD_TICKET:{ticket}";

        if (!_cache.TryGetValue<InformeArchivoZipResponse>(cacheKey, out var zip) || zip == null)
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "SEMANAL_PLANOS_TICKET_INVALIDO",
                mensaje = "El ticket de descarga no existe o ya expiró."
            });
        }

        _cache.Remove(cacheKey);
        Response.Headers.CacheControl = "no-store";

        return File(zip.Archivo, "application/zip", zip.NombreArchivo);
    }

    private bool ObtenerIdUsuario(out int idUsuario) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idUsuario);

    private IActionResult TokenSinUsuario() => Unauthorized(new
    {
        esValido = false,
        codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
        mensaje = "El token no contiene un id de usuario válido.",
        traceId = HttpContext.TraceIdentifier
    });
}