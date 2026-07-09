using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using SIIID2.Api.Services;
using System.Security.Claims;
using System.Security.Cryptography;

namespace SIIID2.Api.Controllers;

[ApiController]
[Route("api/informes")]
public class InformesController : ControllerBase
{
    private readonly IInformeService _informeService;
    private readonly IUltimosArchivosEntidadService _ultimosArchivosEntidadService;
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IMemoryCache _cache;

    public InformesController(IInformeService informeService, IUltimosArchivosEntidadService ultimosArchivosEntidadService, IUsuarioRepository usuarioRepository, IMemoryCache cache)
    {
        _informeService = informeService;
        _ultimosArchivosEntidadService = ultimosArchivosEntidadService;
        _usuarioRepository = usuarioRepository;
        _cache = cache;
    }

    // Consulta el último envío confirmado por entidad y periodo.
    // Ejemplo: GET /api/informes/envios
    [Authorize]
    [HttpGet("envios")]
    public async Task<IActionResult> ObtenerEnvios([FromQuery] int? idEntidadFederativa = null, [FromQuery] int? mesCorte = null, [FromQuery] int? anioCorte = null)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioConsulta))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var envios = await _informeService.ObtenerEnviosAsync(
            idUsuarioConsulta,
            idEntidadFederativa,
            mesCorte,
            anioCorte);

        return Ok(envios);
    }

    // Descarga ZIP con los archivos reconstruidos desde información confirmada.
    // Ejemplo: GET /api/informes/envios/abc123/archivos
    [Authorize]
    [HttpGet("envios/{codigoReferencia}/archivos")]
    public async Task<IActionResult> DescargarArchivosEnvio(string codigoReferencia)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioConsulta))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        try
        {
            var zip = await _informeService.GenerarZipArchivosEnvioAsync(
                codigoReferencia,
                idUsuarioConsulta);

            return File(
                zip.Archivo,
                "application/zip",
                zip.NombreArchivo);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "INFORMES_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "INFORMES_ARCHIVOS_NO_DISPONIBLES",
                mensaje = ex.Message
            });
        }
    }

    [Authorize]
    [HttpPost("envios/acuses/ticket")]
    public async Task<IActionResult> CrearTicketDescargaAcuses([FromQuery] int mesCorte, [FromQuery] int anioCorte)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioConsulta))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        try
        {
            var zip = await _informeService.GenerarZipAcusesEnviosAsync(idUsuarioConsulta, mesCorte, anioCorte);
            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var cacheKey = $"ACUSES_ENVIO_DOWNLOAD_TICKET:{ticket}";

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
                codigo = "INFORMES_ACUSES_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "INFORMES_ACUSES_NO_DISPONIBLES",
                mensaje = ex.Message
            });
        }
    }

    [AllowAnonymous]
    [HttpGet("envios/acuses/descargar")]
    public IActionResult DescargarAcusesPorTicket([FromQuery] string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "INFORMES_ACUSES_TICKET_REQUERIDO",
                mensaje = "Debe proporcionar un ticket de descarga válido."
            });
        }

        var cacheKey = $"ACUSES_ENVIO_DOWNLOAD_TICKET:{ticket}";

        if (!_cache.TryGetValue<InformeArchivoZipResponse>(cacheKey, out var zip) || zip == null)
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "INFORMES_ACUSES_TICKET_INVALIDO",
                mensaje = "El ticket de descarga no existe o ya expiró."
            });
        }

        _cache.Remove(cacheKey);
        Response.Headers.CacheControl = "no-store";

        return File(zip.Archivo, "application/zip", zip.NombreArchivo);
    }


    // Reporte de intentos y cargas por entidad y corte.
    // Solo SUPER_USUARIO.
    // Ejemplo: GET /api/informes/reporte-cargas
    // Ejemplo: GET /api/informes/reporte-cargas?mesCorte=5&anioCorte=2026
    [Authorize]
    [HttpGet("reporte-cargas")]
    public async Task<IActionResult> ObtenerReporteCargas([FromQuery] int? mesCorte = null, [FromQuery] int? anioCorte = null, [FromQuery] int? idEntidadFederativa = null)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioConsulta))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        try
        {
            var reporte = await _informeService.ObtenerReporteCargasAsync(
                idUsuarioConsulta,
                idEntidadFederativa,
                mesCorte,
                anioCorte);

            return Ok(new
            {
                esValido = true,
                mesCorte,
                anioCorte,
                total = reporte.Count,
                registros = reporte
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "INFORMES_REPORTE_CARGAS_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
    }

    // Descarga ZIP con sábanas estadísticas anuales.
    // SUPER_USUARIO descarga información nacional.
    // ENLACE_ESTATAL y CONSULTA descargan únicamente información de su entidad.
    // Ejemplo: GET /api/informes/sabanas?anioCorte=2026&tipo=COMPLETA
    [Authorize]
    [HttpGet("sabanas")]
    public async Task<IActionResult> DescargarSabanas([FromQuery] int anioCorte, [FromQuery] string tipo = "COMPLETA", [FromQuery] string modo = "CONFIRMADO")
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioConsulta))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        try
        {
            var zip = await _informeService.GenerarZipSabanasAsync(idUsuarioConsulta, anioCorte, tipo, modo);

            return File(zip.Archivo, "application/zip", zip.NombreArchivo);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "INFORMES_SABANAS_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "INFORMES_SABANAS_NO_DISPONIBLES",
                mensaje = ex.Message
            });
        }
    }


    [Authorize]
    [HttpPost("sabanas/ticket")]
    public async Task<IActionResult> CrearTicketDescargaSabanas([FromQuery] int anioCorte, [FromQuery] string tipo = "COMPLETA", [FromQuery] string modo = "CONFIRMADO")
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioConsulta))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        try
        {
            // Aquí se genera la sábana.
            // El front se queda con loading hasta que este proceso termine.
            var zip = await _informeService.GenerarZipSabanasAsync(idUsuarioConsulta, anioCorte, tipo, modo);

            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var cacheKey = $"SABANAS_DOWNLOAD_TICKET:{ticket}";

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
                codigo = "INFORMES_SABANAS_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "INFORMES_SABANAS_NO_DISPONIBLES",
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
                codigo = "INFORMES_SABANAS_TICKET_REQUERIDO",
                mensaje = "Debe proporcionar un ticket de descarga válido."
            });
        }

        var cacheKey = $"SABANAS_DOWNLOAD_TICKET:{ticket}";

        if (!_cache.TryGetValue<InformeArchivoZipResponse>(cacheKey, out var zip) || zip == null)
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "INFORMES_SABANAS_TICKET_INVALIDO",
                mensaje = "El ticket de descarga no existe o ya expiró."
            });
        }

        // Ticket de un solo uso.
        _cache.Remove(cacheKey);

        Response.Headers.CacheControl = "no-store";

        return File(zip.Archivo, "application/zip", zip.NombreArchivo);
    }

    // Consulta metadata de los últimos archivos originales recibidos por entidad.
    // Solo SUPER_USUARIO.
    // Ejemplo: GET /api/informes/archivos-originales
    [Authorize]
    [HttpGet("archivos-originales")]
    public async Task<IActionResult> ObtenerArchivosOriginales()
    {
        var autorizacion = await ValidarSuperUsuarioAsync();

        if (autorizacion != null)
        {
            return autorizacion;
        }

        var registros = await _ultimosArchivosEntidadService.ObtenerResumenAsync();

        return Ok(new
        {
            esValido = true,
            total = registros.Count,
            registros
        });
    }

    // Descarga ZIP con los archivos originales íntegros de la última carga/actualización de una entidad.
    // Solo SUPER_USUARIO.
    // Ejemplo: GET /api/informes/archivos-originales/1
    [Authorize]
    [HttpGet("archivos-originales/{idEntidadFederativa:int}")]
    public async Task<IActionResult> DescargarArchivosOriginales(int idEntidadFederativa)
    {
        var autorizacion = await ValidarSuperUsuarioAsync();

        if (autorizacion != null)
        {
            return autorizacion;
        }

        try
        {
            var zip = await _ultimosArchivosEntidadService.DescargarAsync(idEntidadFederativa);

            return File(zip.Archivo, "application/zip", zip.NombreArchivo);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "INFORMES_ARCHIVOS_ORIGINALES_NO_DISPONIBLES",
                mensaje = ex.Message
            });
        }
    }

    private async Task<IActionResult?> ValidarSuperUsuarioAsync()
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioConsulta))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_USUARIO_NO_ENCONTRADO",
                mensaje = "El usuario autenticado no existe o no está activo.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        if (!usuarioConsulta.EsSuperUsuario)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "INFORMES_ARCHIVOS_ORIGINALES_SIN_PERMISO",
                mensaje = "Solo un SUPER_USUARIO puede consultar o descargar archivos originales."
            });
        }

        return null;
    }
}