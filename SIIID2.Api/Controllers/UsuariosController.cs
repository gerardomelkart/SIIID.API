using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
public class UsuariosController : ControllerBase
{
    private readonly IUsuarioService _usuarioService;

    public UsuariosController(IUsuarioService usuarioService)
    {
        _usuarioService = usuarioService;
    }

    // Registra un usuario nuevo.
    // Por ahora solo SUPER_USUARIO puede registrar usuarios.
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CrearUsuario([FromBody] CrearUsuarioRequest request)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioAlta))
        {
            return Unauthorized(new
            {
                mensaje = "El token no contiene un id de usuario válido."
            });
        }

        var resultado = await _usuarioService.CrearUsuarioAsync(
            request,
            idUsuarioAlta);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }
}