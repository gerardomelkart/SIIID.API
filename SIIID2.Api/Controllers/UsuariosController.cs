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

        var resultadoPermiso = await _usuarioService.ValidarSuperUsuarioAsync(idUsuarioConsulta);

        if (!resultadoPermiso.EsValido)
        {
            return StatusCode(StatusCodes.Status403Forbidden, resultadoPermiso);
        }

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

        var resultadoPermiso = await _usuarioService.ValidarSuperUsuarioAsync(idUsuarioConsulta);

        if (!resultadoPermiso.EsValido)
        {
            return StatusCode(StatusCodes.Status403Forbidden, resultadoPermiso);
        }

        var resultado = await _usuarioService.ObtenerUsuarioDetalleAsync(idUsuario);

        if (!resultado.EsValido)
        {
            return NotFound(resultado);
        }

        return Ok(resultado);
    }

    // Registra un usuario nuevo.
    // Por ahora solo SUPER_USUARIO puede registrar usuarios.
    [Authorize(Policy = "MODULO_MENSUAL")]
    [HttpPost]
    public async Task<IActionResult> CrearUsuario([FromBody] CrearUsuarioRequest request)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioAlta))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
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

    // Registra una cuenta desde la administración semanal.
    // Los permisos mensuales quedan deshabilitados por definición.
    [Authorize(Policy = "MODULO_SEMANAL")]
    [HttpPost("semanal")]
    public async Task<IActionResult> CrearUsuarioSemanal([FromBody] CrearUsuarioSemanalRequest request)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioAlta))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var resultado = await _usuarioService.CrearUsuarioSemanalAsync(request, idUsuarioAlta);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }


    // Edita un usuario existente.
    // Si nuevaPassword viene vacía o null, conserva la contraseña actual.
    // Ejemplo: PUT /api/usuarios/3
    [Authorize(Policy = "MODULO_MENSUAL")]
    [HttpPut("{idUsuario:int}")]
    public async Task<IActionResult> EditarUsuario(int idUsuario, [FromBody] EditarUsuarioRequest request)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioModificacion))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var resultado = await _usuarioService.EditarUsuarioAsync(
            idUsuario,
            request,
            idUsuarioModificacion);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }

    // Edita datos generales y permisos semanales sin modificar permisos mensuales.
    [Authorize(Policy = "MODULO_SEMANAL")]
    [HttpPut("{idUsuario:int}/semanal")]
    public async Task<IActionResult> EditarUsuarioSemanal(int idUsuario, [FromBody] EditarUsuarioSemanalRequest request)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioModificacion))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var resultado = await _usuarioService.EditarUsuarioSemanalAsync(idUsuario, request, idUsuarioModificacion);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }


    // Actualiza exclusivamente los permisos del módulo semanal.
    // Ejemplo: PUT /api/usuarios/3/permisos-semanales
    [Authorize(Policy = "MODULO_SEMANAL")]
    [HttpPut("{idUsuario:int}/permisos-semanales")]
    public async Task<IActionResult> ActualizarPermisosSemanales(int idUsuario, [FromBody] ActualizarPermisosSemanalesRequest request)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioModificacion))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var resultado = await _usuarioService.ActualizarPermisosSemanalesAsync(idUsuario, request, idUsuarioModificacion);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }

    // Baja lógica de usuario.
    // No elimina físicamente para conservar auditoría.
    // Ejemplo: DELETE /api/usuarios/3
    [Authorize]
    [HttpDelete("{idUsuario:int}")]
    public async Task<IActionResult> DesactivarUsuario(int idUsuario)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioModificacion))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var resultado = await _usuarioService.DesactivarUsuarioAsync(
            idUsuario,
            idUsuarioModificacion);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }

    // Activa/desactiva carga y modificación para todos los usuarios activos.
    // Ejemplo: PUT /api/usuarios/permisos-globales
    [Authorize(Policy = "MODULO_MENSUAL")]
    [HttpPut("permisos-globales")]
    public async Task<IActionResult> ActualizarPermisosGlobales([FromBody] PermisosGlobalesUsuariosRequest request)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioModificacion))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var resultado = await _usuarioService.ActualizarPermisosGlobalesAsync(
            request,
            idUsuarioModificacion);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }

    // Reactiva un usuario dado de baja lógicamente.
    // No cambia contraseña, rol, entidad ni datos personales.
    // Ejemplo: PUT /api/usuarios/3/reactivar
    [Authorize(Policy = "MODULO_MENSUAL")]
    [HttpPut("{idUsuario:int}/reactivar")]
    public async Task<IActionResult> ReactivarUsuario(int idUsuario, [FromBody] ReactivarUsuarioRequest request)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioModificacion))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var resultado = await _usuarioService.ReactivarUsuarioAsync(
            idUsuario,
            request,
            idUsuarioModificacion);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }


    // Reactiva la cuenta conservando los valores mensuales y configurando el módulo semanal.
    [Authorize(Policy = "MODULO_SEMANAL")]
    [HttpPut("{idUsuario:int}/reactivar-semanal")]
    public async Task<IActionResult> ReactivarUsuarioSemanal(int idUsuario, [FromBody] ReactivarUsuarioSemanalRequest request)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioModificacion))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var resultado = await _usuarioService.ReactivarUsuarioSemanalAsync(idUsuario, request, idUsuarioModificacion);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }
}