using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Models;
using SIIID2.Api.Readers;
using SIIID2.Api.Repositories;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController, Authorize(Policy = "MODULO_BANCI")]
[Route("api/banci/actualizaciones")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BanciActualizacionesController(BanciActualizacionService service, BanciActualizacionRepository repository) : ControllerBase
{
    [HttpGet("opciones")]
    public Task<IActionResult> Opciones() => Ejecutar(async usuario => Ok(await service.OpcionesAsync(usuario)));

    [HttpGet("buscar")]
    public Task<IActionResult> Buscar([FromQuery, Required, StringLength(250, MinimumLength = 3)] string texto, [FromQuery, Range(1, 32)] int? idEntidadFederativa, [FromQuery, Range(1, 1000000)] int pagina = 1, [FromQuery, Range(1, 100)] int tamanoPagina = 50) => Ejecutar(async usuario => Ok(await repository.BuscarAsync(usuario, texto.Trim(), idEntidadFederativa, pagina, tamanoPagina)));

    [HttpGet("plantilla")]
    public Task<IActionResult> Plantilla() => Ejecutar(async usuario =>
    {
        await service.AutorizarAsync(usuario, true);
        return File(BanciActualizacionReader.Plantilla(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "BANCI_actualizacion_victimas_v2.xlsx");
    });

    [HttpPost("formulario/validar"), RequestSizeLimit(5 * 1024 * 1024)]
    public Task<IActionResult> ValidarFormulario([FromBody] BanciActualizacionRequest request) => Ejecutar(async usuario =>
    {
        var resultado = await service.ValidarAsync(usuario, request.IdEntidadFederativa, [request.Datos], "FORMULARIO", HttpContext.RequestAborted);
        return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
    });

    [HttpPost("archivo/validar"), Consumes("multipart/form-data"), RequestSizeLimit(55 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 55 * 1024 * 1024)]
    public Task<IActionResult> ValidarArchivo([FromForm] BanciActualizacionArchivoRequest request) => Ejecutar(async usuario =>
    {
        await service.AutorizarAsync(usuario, true);
        var filas = BanciActualizacionReader.Leer(request.Archivo);
        var resultado = await service.ValidarAsync(usuario, request.IdEntidadFederativa, filas, "EXCEL", HttpContext.RequestAborted);
        return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
    });

    [HttpGet("pendientes")]
    public Task<IActionResult> Pendientes() => Ejecutar(async usuario =>
    {
        var acceso = await service.AutorizarAsync(usuario);
        return Ok(await repository.EstadosAsync(usuario, acceso.EsSuperUsuario ? null : acceso.IdEntidadFederativa));
    });

    [HttpGet("{referencia:guid}")]
    public Task<IActionResult> Estado(Guid referencia) => Ejecutar(async usuario =>
    {
        var acceso = await service.AutorizarAsync(usuario);
        return Ok(await repository.EstadosAsync(usuario, acceso.EsSuperUsuario ? null : acceso.IdEntidadFederativa, referencia));
    });

    [HttpGet("{referencia:guid}/vista-previa")]
    public Task<IActionResult> VistaPrevia(Guid referencia) => Ejecutar(async usuario => Ok(await repository.VistaPreviaAsync(referencia, usuario)));

    [HttpPost("{referencia:guid}/confirmar")]
    public Task<IActionResult> Confirmar(Guid referencia, [FromBody] BanciActualizacionConfirmacion request) => Ejecutar(async usuario =>
    {
        if (!request.Aceptar.HasValue) return BadRequest(new { mensaje = "Indique explícitamente aceptar o rechazar." });
        return Ok(await repository.ConfirmarAsync(referencia, usuario, request));
    });

    private async Task<IActionResult> Ejecutar(Func<int, Task<IActionResult>> accion)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuario)) return Unauthorized(new { mensaje = "Token sin usuario válido." });
        try { return await accion(usuario); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { mensaje = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { mensaje = ex.Message }); }
        catch (InvalidDataException) { return BadRequest(new { mensaje = "El Excel está dañado o no es un libro .xlsx válido." }); }
        catch (SqlException ex) when (ex.Number is >= 52520 and <= 52536)
        {
            var status = ex.Number switch { 52521 => 404, 52523 => 403, 52520 or 52522 or 52532 or 52533 or 52534 => 409, _ => 400 };
            return StatusCode(status, new { codigo = $"BANCI_{ex.Number}", mensaje = ex.Message, traceId = HttpContext.TraceIdentifier });
        }
    }
}
