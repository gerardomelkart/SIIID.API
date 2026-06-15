using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace SIIID2.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    // Endpoint de login real.
    // Ejemplo: POST /api/auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var resultado = await _authService.LoginAsync(request);

        if (!resultado.EsValido)
        {
            return Unauthorized(resultado);
        }

        return Ok(resultado);
    }

    [Authorize]
    [HttpPost("cambiar-password")]
    public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordRequest request)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuario))
        {
            return Unauthorized(new CambiarPasswordResponse
            {
                EsValido = false,
                Codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                Mensaje = "El token no contiene un id de usuario válido."
            });
        }

        var resultado = await _authService.CambiarPasswordAsync(
            idUsuario,
            request);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }
}
