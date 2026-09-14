using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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