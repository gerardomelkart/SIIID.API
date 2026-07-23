using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/semanal/envios")]
public class SemanalEnviosController : ControllerBase
{
    private readonly ISemanalEnviosService _semanalEnviosService;

    public SemanalEnviosController(ISemanalEnviosService semanalEnviosService) => _semanalEnviosService = semanalEnviosService;

    [HttpGet]
    public async Task<IActionResult> ObtenerEnvios([FromQuery] int? idEntidadFederativa = null, [FromQuery] int? anioSemana = null, [FromQuery] int? numeroSemana = null, [FromQuery] string? tipoCarga = null, [FromQuery] string? estado = null)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        try
        {
            var registros = await _semanalEnviosService.ObtenerEnviosAsync(idUsuario, idEntidadFederativa, anioSemana, numeroSemana, tipoCarga, estado);
            return Ok(new { esValido = true, total = registros.Count, registros });
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

    private bool ObtenerIdUsuario(out int idUsuario) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idUsuario);

    private IActionResult TokenSinUsuario() => Unauthorized(new
    {
        esValido = false,
        codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
        mensaje = "El token no contiene un id de usuario válido.",
        traceId = HttpContext.TraceIdentifier
    });
}