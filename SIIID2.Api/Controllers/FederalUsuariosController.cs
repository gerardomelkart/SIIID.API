using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_FEDERAL", Roles = "SUPER_USUARIO")]
[Route("api/federal/usuarios")]
public class FederalUsuariosController : ControllerBase
{
    private readonly IFederalUsuarioService _service;
    public FederalUsuariosController(IFederalUsuarioService service) => _service = service;

    [HttpGet]
    public Task<IActionResult> ObtenerUsuarios([FromQuery] bool incluirInactivos = false) => EjecutarAsync(async id =>
    {
        var usuarios = await _service.ObtenerUsuariosAsync(id, incluirInactivos);
        return Ok(new { esValido = true, total = usuarios.Count, usuarios });
    });

    [HttpGet("{idUsuario:int}")]
    public Task<IActionResult> ObtenerDetalle(int idUsuario) => EjecutarAsync(async id => Ok(await _service.ObtenerDetalleAsync(id, idUsuario)));

    [HttpPost]
    public Task<IActionResult> Crear([FromBody] CrearUsuarioFederalRequest request) => EjecutarAsync(async id => Responder(await _service.CrearAsync(id, request)));

    [HttpPut("{idUsuario:int}")]
    public Task<IActionResult> Editar(int idUsuario, [FromBody] EditarUsuarioFederalRequest request) => EjecutarAsync(async id => Responder(await _service.EditarAsync(id, idUsuario, request)));

    [HttpDelete("{idUsuario:int}")]
    public Task<IActionResult> Desactivar(int idUsuario) => EjecutarAsync(async id => Responder(await _service.DesactivarAsync(id, idUsuario)));

    [HttpPut("{idUsuario:int}/reactivar")]
    public Task<IActionResult> Reactivar(int idUsuario, [FromBody] ReactivarUsuarioFederalRequest request) => EjecutarAsync(async id => Responder(await _service.ReactivarAsync(id, idUsuario, request)));

    private IActionResult Responder(UsuarioOperacionResponse response) => response.EsValido ? Ok(response) : BadRequest(response);

    private async Task<IActionResult> EjecutarAsync(Func<int, Task<IActionResult>> accion)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario))
            return Unauthorized(new { esValido = false, codigo = "GENERAL_TOKEN_SIN_ID_USUARIO", mensaje = "El token no contiene un id de usuario válido." });
        try { return await accion(idUsuario); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { esValido = false, codigo = "FEDERAL_USUARIOS_SIN_PERMISO", mensaje = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { esValido = false, codigo = "FEDERAL_USUARIO_NO_EXISTE", mensaje = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { esValido = false, codigo = "FEDERAL_USUARIO_OPERACION_INVALIDA", mensaje = ex.Message }); }
    }
}
