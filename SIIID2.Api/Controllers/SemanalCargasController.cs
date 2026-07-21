using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/semanal/cargas")]
public class SemanalCargasController : ControllerBase
{
    private readonly ISemanalCargaService _semanalCargaService;

    public SemanalCargasController(ISemanalCargaService semanalCargaService) => _semanalCargaService = semanalCargaService;

    [HttpPost("validar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ValidarArchivos([FromForm] SemanalCargaValidacionRequest request)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _semanalCargaService.ValidarArchivosAsync(request, idUsuario);

        return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost("confirmar")]
    public async Task<IActionResult> ConfirmarCarga([FromBody] ConfirmarCargaRequest request)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        if (string.IsNullOrWhiteSpace(request.CodigoReferencia))
        {
            return BadRequest(new ConfirmarCargaResponse
            {
                EsValido = false,
                Estado = "SOLICITUD_INVALIDA",
                Mensaje = "Debe enviar el código de referencia."
            });
        }

        var resultado = await _semanalCargaService.ConfirmarCargaAsync(request, idUsuario);

        return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
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
