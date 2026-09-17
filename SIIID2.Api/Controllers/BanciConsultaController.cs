using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_BANCI")]
[Route("api/banci/consulta")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class BanciConsultaController : ControllerBase
{
    private readonly IBanciConsultaService _service;

    public BanciConsultaController(IBanciConsultaService service) { _service = service; }

    [HttpGet("opciones")]
    public Task<IActionResult> ObtenerOpciones() => EjecutarAsync(async idUsuario =>
        Ok(await _service.ObtenerOpcionesAsync(idUsuario)));

    [HttpGet]
    public Task<IActionResult> Consultar([FromQuery] BanciConsultaFiltro filtro) => EjecutarAsync(async idUsuario =>
        Ok(await _service.ConsultarAsync(idUsuario, filtro)));

    [HttpGet("carpetas/{idCarpeta:long}")]
    public Task<IActionResult> ObtenerDetalle(long idCarpeta) => EjecutarAsync(async idUsuario =>
    {
        if (idCarpeta <= 0) return BadRequest(new { mensaje = "Identificador de carpeta inválido." });
        var detalle = await _service.ObtenerDetalleAsync(idUsuario, idCarpeta);
        if (detalle == null) return NotFound(new { mensaje = "La carpeta no está disponible para este usuario." });
        return Ok(detalle);
    });

    private async Task<IActionResult> EjecutarAsync(Func<int, Task<IActionResult>> accion)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario) || idUsuario <= 0)
            return Unauthorized();
        try { return await accion(idUsuario); }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { mensaje = "No tiene acceso a la entidad solicitada." });
        }
    }
}
