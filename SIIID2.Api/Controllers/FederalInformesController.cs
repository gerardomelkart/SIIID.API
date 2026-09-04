using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_FEDERAL")]
[Route("api/federal/informes")]
public class FederalInformesController : ControllerBase
{
    private readonly IFederalEnviosService _federalEnviosService;

    public FederalInformesController(IFederalEnviosService federalEnviosService)
    {
        _federalEnviosService = federalEnviosService;
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