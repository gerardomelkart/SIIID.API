using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Route("api/informes")]
public class InformesController : ControllerBase
{
    private readonly IInformeService _informeService;

    public InformesController(IInformeService informeService)
    {
        _informeService = informeService;
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
}