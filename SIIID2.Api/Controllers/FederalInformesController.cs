using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_FEDERAL")]
[Route("api/federal/informes")]
public class FederalInformesController : ControllerBase
{
    private readonly IFederalEnviosService _federalEnviosService;
    private readonly IMemoryCache _cache;

    public FederalInformesController(IFederalEnviosService federalEnviosService, IMemoryCache cache)
    {
        _federalEnviosService = federalEnviosService;
        _cache = cache;
    }

    [HttpGet("envios/periodos")]
    public async Task<IActionResult> ObtenerPeriodosEnvios()
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();
        return Ok(await _federalEnviosService.ObtenerPeriodosAsync(idUsuario));
    }

    [HttpGet("envios")]
    public async Task<IActionResult> ObtenerEnvios([FromQuery] int? mesCorte = null, [FromQuery] int? anioCorte = null)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();
        return Ok(await _federalEnviosService.ObtenerEnviosAsync(idUsuario, mesCorte, anioCorte));
    }

    [Authorize(Roles = "SUPER_USUARIO")]
    [HttpGet("reporte-cargas")]
    public async Task<IActionResult> ObtenerReporteCargas([FromQuery] int? mesCorte = null, [FromQuery] int? anioCorte = null)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        try
        {
            var reporte = await _federalEnviosService.ObtenerReporteCargasAsync(idUsuario, mesCorte, anioCorte);
            return Ok(new { esValido = true, mesCorte, anioCorte, total = reporte.Count, registros = reporte });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { esValido = false, codigo = "FEDERAL_REPORTE_CARGAS_SIN_PERMISO", mensaje = ex.Message });
        }
    }

    [HttpGet("envios/{codigoReferencia}/archivos")]
    public async Task<IActionResult> DescargarArchivos(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        try
        {
            var zip = await _federalEnviosService.GenerarZipArchivosAsync(idUsuario, codigoReferencia);
            return File(zip.Archivo, "application/zip", zip.NombreArchivo);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { esValido = false, codigo = "FEDERAL_ENVIOS_ARCHIVOS_SIN_PERMISO", mensaje = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { esValido = false, codigo = "FEDERAL_ENVIO_NO_ENCONTRADO", mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { esValido = false, codigo = "FEDERAL_ENVIOS_ARCHIVOS_NO_DISPONIBLES", mensaje = ex.Message });
        }
    }

    [HttpPost("envios/acuses/ticket")]
    public async Task<IActionResult> CrearTicketDescargaAcuses([FromQuery] int anioCorte, [FromQuery] int? mesCorte = null)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        try
        {
            var zip = await _federalEnviosService.GenerarZipAcusesAsync(idUsuario, mesCorte, anioCorte);
            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var cacheKey = $"FEDERAL_ACUSES_ENVIO_DOWNLOAD_TICKET:{ticket}";

            _cache.Set(cacheKey, zip, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(5),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });

            return Ok(new { esValido = true, ticket });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { esValido = false, codigo = "FEDERAL_ACUSES_SIN_PERMISO", mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { esValido = false, codigo = "FEDERAL_ACUSES_NO_DISPONIBLES", mensaje = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpGet("envios/acuses/descargar")]
    public IActionResult DescargarAcusesPorTicket([FromQuery] string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
            return Unauthorized(new { esValido = false, codigo = "FEDERAL_ACUSES_TICKET_REQUERIDO", mensaje = "Debe proporcionar un ticket de descarga válido." });

        var cacheKey = $"FEDERAL_ACUSES_ENVIO_DOWNLOAD_TICKET:{ticket}";

        if (!_cache.TryGetValue<InformeArchivoZipResponse>(cacheKey, out var zip) || zip == null)
            return Unauthorized(new { esValido = false, codigo = "FEDERAL_ACUSES_TICKET_INVALIDO", mensaje = "El ticket de descarga no existe o ya expiró." });

        _cache.Remove(cacheKey);
        Response.Headers.CacheControl = "no-store";

        return File(zip.Archivo, "application/zip", zip.NombreArchivo);
    }

    [HttpGet("sabanas")]
    public async Task<IActionResult> DescargarSabanas(
    [FromQuery] int anioCorte,
    [FromQuery] string tipo = "COMPLETA",
    [FromQuery] string modo = "CONFIRMADO")
    {
        if (!ObtenerIdUsuario(out var idUsuario))
            return TokenSinUsuario();

        try
        {
            var zip = await _federalEnviosService.GenerarZipSabanasAsync(
                idUsuario,
                anioCorte,
                tipo,
                modo);

            return File(zip.Archivo, "application/zip", zip.NombreArchivo);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    esValido = false,
                    codigo = "FEDERAL_SABANAS_SIN_PERMISO",
                    mensaje = ex.Message
                });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "FEDERAL_SABANAS_NO_DISPONIBLES",
                mensaje = ex.Message
            });
        }
    }

    [HttpPost("sabanas/ticket")]
    public async Task<IActionResult> CrearTicketDescargaSabanas(
        [FromQuery] int anioCorte,
        [FromQuery] string tipo = "COMPLETA",
        [FromQuery] string modo = "CONFIRMADO")
    {
        if (!ObtenerIdUsuario(out var idUsuario))
            return TokenSinUsuario();

        try
        {
            var zip = await _federalEnviosService.GenerarZipSabanasAsync(
                idUsuario,
                anioCorte,
                tipo,
                modo);

            var ticket = Convert
                .ToHexString(RandomNumberGenerator.GetBytes(32))
                .ToLowerInvariant();

            var cacheKey = $"FEDERAL_SABANAS_DOWNLOAD_TICKET:{ticket}";

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
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    esValido = false,
                    codigo = "FEDERAL_SABANAS_SIN_PERMISO",
                    mensaje = ex.Message
                });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "FEDERAL_SABANAS_NO_DISPONIBLES",
                mensaje = ex.Message
            });
        }
    }

    [AllowAnonymous]
    [HttpGet("sabanas/descargar")]
    public IActionResult DescargarSabanasPorTicket([FromQuery] string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "FEDERAL_SABANAS_TICKET_REQUERIDO",
                mensaje = "Debe proporcionar un ticket de descarga válido."
            });
        }

        var cacheKey = $"FEDERAL_SABANAS_DOWNLOAD_TICKET:{ticket}";

        if (!_cache.TryGetValue<InformeArchivoZipResponse>(cacheKey, out var zip) || zip == null)
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "FEDERAL_SABANAS_TICKET_INVALIDO",
                mensaje = "El ticket de descarga no existe o ya expiró."
            });
        }

        _cache.Remove(cacheKey);

        Response.Headers.CacheControl = "no-store";

        return File(zip.Archivo, "application/zip", zip.NombreArchivo);
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
}
