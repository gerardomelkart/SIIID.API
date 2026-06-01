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
}