using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_SEMANAL")]
[Route("api/semanal/delitos")]
public class SemanalDelitosController : ControllerBase
{
    private readonly ISemanalDelitoService _semanalDelitoService;

    public SemanalDelitosController(ISemanalDelitoService semanalDelitoService) => _semanalDelitoService = semanalDelitoService;

    [HttpGet]
    public async Task<IActionResult> ObtenerConfiguracion()
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _semanalDelitoService.ObtenerConfiguracionAsync(idUsuario);

        return resultado.EsValido ? Ok(resultado) : StatusCode(StatusCodes.Status403Forbidden, resultado);
    }

    [HttpPut]
    public async Task<IActionResult> GuardarConfiguracion([FromBody] ActualizarConfiguracionDelitosSemanalesRequest request)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _semanalDelitoService.GuardarConfiguracionAsync(request, idUsuario);

        if (resultado.EsValido) return Ok(resultado);

        return resultado.Codigo == "SEMANAL_ADMINISTRACION_DELITOS_SIN_PERMISO"
            ? StatusCode(StatusCodes.Status403Forbidden, resultado)
            : BadRequest(resultado);
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