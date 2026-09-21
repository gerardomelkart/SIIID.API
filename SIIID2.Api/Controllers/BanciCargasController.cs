using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_BANCI")]
[Route("api/banci/cargas")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class BanciCargasController : ControllerBase
{
    private readonly IBanciCargaService _banciCargaService;

    public BanciCargasController(
        IBanciCargaService banciCargaService)
    {
        _banciCargaService =
            banciCargaService;
    }

    [HttpPost("validar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ValidarArchivos(
        [FromForm] BanciCargaArchivosRequest request)
    {
        if (!TryObtenerIdUsuario(
                out var idUsuarioCarga))
        {
            return TokenInvalido();
        }

        try
        {
            var resultado = await _banciCargaService.ValidarArchivosAsync(request, idUsuarioCarga);
            return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { mensaje = ex.Message }); }
    }

    [HttpGet("formulario/opciones")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ObtenerFormularioOpciones()
    {
        if (!TryObtenerIdUsuario(out var idUsuario)) return TokenInvalido();
        try { return Ok(await _banciCargaService.ObtenerFormularioOpcionesAsync(idUsuario)); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { mensaje = ex.Message }); }
    }

    [HttpPost("formulario/validar")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> ValidarFormulario([FromBody] BanciFormularioRequest request)
    {
        if (!TryObtenerIdUsuario(out var idUsuario)) return TokenInvalido();
        try
        {
            var resultado = await _banciCargaService.ValidarFormularioAsync(request, idUsuario);
            return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { mensaje = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { mensaje = ex.Message }); }
    }

    [HttpGet("pendientes")]
    public async Task<IActionResult> ObtenerPendientes()
    {
        if (!TryObtenerIdUsuario(out var idUsuario)) return TokenInvalido();
        return Ok(await _banciCargaService.ObtenerPendientesAsync(idUsuario));
    }

    [HttpGet("{codigoReferencia}")]
    public async Task<IActionResult> ObtenerCarga(string codigoReferencia)
    {
        if (!TryObtenerIdUsuario(out var idUsuario)) return TokenInvalido();
        if (string.IsNullOrWhiteSpace(codigoReferencia) || codigoReferencia.Length > 50)
            return BadRequest(new { mensaje = "Referencia inválida." });

        var resultado = await _banciCargaService.ObtenerCargaAsync(codigoReferencia, idUsuario);
        return resultado == null
            ? NotFound(new { mensaje = "La carga no está disponible para este usuario." })
            : Ok(resultado);
    }

    [HttpPost("confirmar")]
    public async Task<IActionResult> ConfirmarCarga([FromBody] BanciCargaConfirmacionRequest request)
    {
        if (!TryObtenerIdUsuario(out var idUsuario)) return TokenInvalido();
        if (!request.Aceptar.HasValue || string.IsNullOrWhiteSpace(request.CodigoReferencia))
            return BadRequest(new { mensaje = "Indique la referencia y una decisión explícita." });

        try
        {
            return Ok(await _banciCargaService.ConfirmarCargaAsync(
                request.CodigoReferencia, request.Aceptar.Value, idUsuario, request.HuellaVistaPrevia));
        }
        catch (SqlException ex) when (ex.Number is >= 52400 and <= 52426)
        {
            var mensaje = ex.Number switch
            {
                52426 => "Sus permisos BANCI actuales no permiten integrar esta carga. Actualice el estado para revisar qué permiso falta; no se integró información.",
                52425 => "La información cambió desde la vista previa o ésta no fue consultada. Actualice el estado, revise los cambios y vuelva a decidir. No se integró esta carga.",
                52424 => "La carpeta ya fue registrada por otra carga. Rechace esta captura pendiente; no se duplicó ni se sobrescribió la carpeta existente.",
                52402 or 52404 => "La carga no está disponible para este usuario.",
                52405 => "El usuario ya no tiene acceso a esta carga.",
                52403 => "Hay otra operación BANCI en curso. Actualice el estado antes de intentar nuevamente.",
                _ => "No fue posible aplicar esta decisión al estado actual de la carga. Actualice el estado; no vuelva a subir los archivos."
            };
            var status = ex.Number switch
            {
                52402 or 52404 => StatusCodes.Status404NotFound,
                52405 or 52426 => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status409Conflict
            };
            return StatusCode(status, new
            {
                codigo = $"BANCI_{ex.Number}",
                mensaje,
                request.CodigoReferencia,
                traceId = HttpContext.TraceIdentifier
            });
        }
    }

    private bool TryObtenerIdUsuario(
        out int idUsuario)
    {
        return int.TryParse(
            User.FindFirstValue(
                ClaimTypes.NameIdentifier),
            out idUsuario);
    }

    private ObjectResult TokenInvalido()
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
