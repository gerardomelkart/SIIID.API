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

    // Lista usuarios para la tabla administrativa.
    // Ejemplo: GET /api/usuarios
    // Ejemplo: GET /api/usuarios?incluirInactivos=true
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> ObtenerUsuarios([FromQuery] bool incluirInactivos = false)
    {
        var usuarios = await _usuarioService.ObtenerUsuariosAsync(incluirInactivos);

        return Ok(new
        {
            esValido = true,
            total = usuarios.Count,
            usuarios
        });
    }

    // Obtiene el detalle de un usuario para edición.
    // Ejemplo: GET /api/usuarios/3
    [Authorize]
    [HttpGet("{idUsuario:int}")]
    public async Task<IActionResult> ObtenerUsuarioDetalle(int idUsuario)
    {
        var resultado = await _usuarioService.ObtenerUsuarioDetalleAsync(idUsuario);

        if (!resultado.EsValido)
        {
            return NotFound(resultado);
        }

        return Ok(resultado);
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