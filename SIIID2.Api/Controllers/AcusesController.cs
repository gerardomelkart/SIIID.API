using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Route("api/acuses")]
public class AcusesController : ControllerBase
{
    private readonly IAcusePdfService _acusePdfService;
    private readonly IAcuseRepository _acuseRepository;
    private readonly IMemoryCache _cache;

    public AcusesController(IAcusePdfService acusePdfService, IAcuseRepository acuseRepository, IMemoryCache cache)
    {
        _acusePdfService = acusePdfService;
        _acuseRepository = acuseRepository;
        _cache = cache;
    }

    [Authorize]
    [HttpPost("{codigoReferencia}/ticket")]
    public async Task<IActionResult> CrearTicket(string codigoReferencia, [FromQuery] string tipo)
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

        var tipoNormalizado = (tipo ?? string.Empty).Trim().ToUpperInvariant();

        try
        {
            var pdf = tipoNormalizado switch
            {
                "PREVIO_CARGA" => await _acusePdfService.GenerarAcusePrevioAsync(codigoReferencia, idUsuarioConsulta),
                "CONFIRMADO_CARGA" => await _acusePdfService.GenerarAcuseConfirmadoAsync(codigoReferencia, idUsuarioConsulta),
                "PREVIO_ACTUALIZACION" => await _acusePdfService.GenerarAcusePrevioActualizacionAsync(codigoReferencia, idUsuarioConsulta),
                "CONFIRMADO_ACTUALIZACION" => await _acusePdfService.GenerarAcuseConfirmadoActualizacionAsync(codigoReferencia, idUsuarioConsulta),
                _ => throw new ArgumentException("El tipo de acuse solicitado no es válido.")
            };

            var carga = await _acuseRepository.ObtenerCargaParaAcuseAsync(codigoReferencia) ?? throw new InvalidOperationException("No se encontró la información de la carga para nombrar el acuse.");
            var nombreArchivo = ObtenerNombreArchivo(carga, tipoNormalizado);
            var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var cacheKey = $"ACUSE_DOWNLOAD_TICKET:{ticket}";

            _cache.Set(cacheKey, new AcuseTicketArchivo(pdf, nombreArchivo), new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5), AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15) });

            return Ok(new { esValido = true, ticket, nombreArchivo });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { esValido = false, codigo = "ACUSE_TIPO_INVALIDO", mensaje = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { esValido = false, codigo = "ACUSE_SIN_PERMISO", mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { esValido = false, codigo = "ACUSE_NO_DISPONIBLE", mensaje = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpGet("descargar")]
    public IActionResult DescargarPorTicket([FromQuery] string ticket)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return Unauthorized(new { esValido = false, codigo = "ACUSE_TICKET_REQUERIDO", mensaje = "Debe proporcionar un ticket de descarga válido." });
        }

        var cacheKey = $"ACUSE_DOWNLOAD_TICKET:{ticket}";

        if (!_cache.TryGetValue<AcuseTicketArchivo>(cacheKey, out var archivo) || archivo == null)
        {
            return Unauthorized(new { esValido = false, codigo = "ACUSE_TICKET_INVALIDO", mensaje = "El ticket no existe o ya expiró." });
        }

        Response.Headers.CacheControl = "no-store";
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{archivo.NombreArchivo}\"";

        return File(archivo.Archivo, "application/pdf");
    }

    private static string ObtenerNombreArchivo(CargaAcuseInfo carga, string tipo)
    {
        var entidad = LimpiarNombreArchivo(carga.EntidadFederativa);
        var mes = ObtenerNombreMes(carga.MesCorte).ToUpperInvariant();
        var prefijo = tipo switch
        {
            "PREVIO_CARGA" => "INFORME_PREVIO",
            "CONFIRMADO_CARGA" => "ACUSE",
            "PREVIO_ACTUALIZACION" => "INFORME_PREVIO_ACTUALIZACION",
            "CONFIRMADO_ACTUALIZACION" => "ACUSE_ACTUALIZACION",
            _ => "ACUSE"
        };

        return $"{prefijo}_{entidad}_{mes}_{carga.AnioCorte}.pdf";
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

    private static string LimpiarNombreArchivo(string valor)
    {
        var normalizado = valor.Normalize(NormalizationForm.FormD);
        var sinAcentos = new string(normalizado.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC).ToUpperInvariant();
        return Regex.Replace(sinAcentos, @"[^A-Z0-9]+", "_").Trim('_');
    }

    private sealed record AcuseTicketArchivo(byte[] Archivo, string NombreArchivo);
}
