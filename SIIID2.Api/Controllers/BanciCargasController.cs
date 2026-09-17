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

        var resultado =
            await _banciCargaService
                .ValidarArchivosAsync(
                    request,
                    idUsuarioCarga);

        return resultado.EsValido
            ? Ok(resultado)
            : BadRequest(resultado);
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
                request.CodigoReferencia, request.Aceptar.Value, idUsuario));
        }
        catch (SqlException ex) when (ex.Number is >= 52400 and <= 52423)
        {
            var mensaje = ex.Number switch
            {
                52402 or 52404 => "La carga no está disponible para este usuario.",
                52405 => "El usuario ya no tiene acceso a esta carga.",
                52403 => "Hay otra operación BANCI en curso. Actualice el estado antes de intentar nuevamente.",
                _ => "No fue posible aplicar esta decisión al estado actual de la carga. Actualice el estado; no vuelva a subir los archivos."
            };
            var status = ex.Number switch
            {
                52402 or 52404 => StatusCodes.Status404NotFound,
                52405 => StatusCodes.Status403Forbidden,
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
