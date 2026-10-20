using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_BANCI")]
[Route("api/banci/mensual")]
public sealed class BanciMensualController(BanciMensualService servicio, ILogger<BanciMensualController> logger) : ControllerBase
{
    [HttpGet("opciones")]
    public Task<IActionResult> Opciones() => Ejecutar(async id => Ok(await servicio.OpcionesAsync(id)));
    [HttpGet("precarga")]
    public Task<IActionResult> Precarga(int entidad,
        int mes,
        int anio,
        bool actualizacion = false, bool manual = false) => Ejecutar(async id => Ok(await servicio.PrecargaAsync(id, entidad, mes, anio, actualizacion, manual)));
    [HttpGet("plantilla")]
    public Task<IActionResult> Plantilla(int entidad, int mes, int anio, bool actualizacion = false, bool manual = false) => Ejecutar(async id =>
    {
        var libro = await servicio.PrecargaAsync(id, entidad, mes, anio, actualizacion, manual);
        return File(servicio.Excel(libro), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"BANCI_{anio}{mes:D2}_{entidad:D2}.xlsx");
    });
    [HttpPost("validar/manual")]
    [RequestSizeLimit(60 * 1024 * 1024)]
    public Task<IActionResult> ValidarManual(BanciLibro libro) => Ejecutar(async id => Ok(await servicio.ValidarAsync(id, libro, [], "", manual: true)));
    [HttpPost("validar/archivo")]
    [RequestSizeLimit(60 * 1024 * 1024)]
    public Task<IActionResult> ValidarArchivo([FromForm] IFormFile archivo,
        [FromForm] int entidad,
        [FromForm] int mes,
        [FromForm] int anio,
        [FromForm] bool actualizacion = false) => Ejecutar(async id =>
    {
        var libro = await servicio.LeerExcelAsync(archivo, entidad, mes, anio, actualizacion);
        await using var stream = new MemoryStream();
        await archivo.CopyToAsync(stream);
        return Ok(await servicio.ValidarAsync(id, libro, stream.ToArray(), archivo.FileName));
    });
    [Authorize(Roles = "SUPER_USUARIO")]
    [HttpGet("reportes/cargas/periodos")]
    public Task<IActionResult> PeriodosCargas() => Ejecutar(async id => Ok(await servicio.PeriodosCargasAsync(id)));
    [Authorize(Roles = "SUPER_USUARIO")]
    [HttpGet("reportes/cargas")]
    public Task<IActionResult> ReporteCargas(int anio, int mes) => Ejecutar(async id => Ok(await servicio.ReporteCargasAsync(id, anio, mes)));
    [Authorize(Roles = "SUPER_USUARIO")]
    [HttpGet("reportes/originales")]
    public Task<IActionResult> UltimosOriginales() => Ejecutar(async id => Ok(await servicio.UltimosOriginalesAsync(id)));
    [Authorize(Roles = "SUPER_USUARIO")]
    [HttpGet("reportes/originales/{referencia:guid}/zip")]
    public Task<IActionResult> OriginalZip(Guid referencia) => Ejecutar(async id => File(await servicio.OriginalZipAsync(id, referencia), "application/zip", "BANCI_original.zip"));
    [Authorize(Roles = "SUPER_USUARIO")]
    [HttpGet("administracion/pendientes")]
    public Task<IActionResult> PendientesAdministracion() => Ejecutar(async id => Ok(await servicio.PendientesAdministracionAsync(id)));
    [Authorize(Roles = "SUPER_USUARIO")]
    [HttpGet("administracion/{referencia:guid}/revision")]
    public Task<IActionResult> RevisionAdministracion(Guid referencia) => Ejecutar(async id => Ok(await servicio.RevisionAdministracionAsync(id, referencia)));
    [HttpPost("notificaciones/rechazos/consumir")]
    public Task<IActionResult> ConsumirRechazos() => Ejecutar(async id => Ok(await servicio.ConsumirRechazosAsync(id)));
    [HttpGet("recordatorio")]
    public Task<IActionResult> Recordatorio() => Ejecutar(async id => Ok(await servicio.RecordatorioAsync(id)));
    [HttpGet("envios/periodos")]
    public Task<IActionResult> PeriodosEnvios() => Ejecutar(async id => Ok(await servicio.PeriodosEnviosAsync(id)));
    [HttpGet("envios/acuses")]
    public Task<IActionResult> AcusesEnvios(int anio, int mes) => Ejecutar(async id =>
        File(await servicio.AcusesEnviosAsync(id, anio, mes), "application/zip", $"BANCI_acuses_{anio}{mes:D2}.zip"));
    [HttpGet("envios")]
    public Task<IActionResult> Envios(int anio) => Ejecutar(async id => Ok(await servicio.ListarAsync(id, anio, "envios")));
    [Authorize(Roles = "SUPER_USUARIO")]
    [HttpGet("cargas")]
    public Task<IActionResult> Cargas(int anio) => Ejecutar(async id => Ok(await servicio.ListarAsync(id, anio, "cargas")));
    [Authorize(Roles = "SUPER_USUARIO")]
    [HttpGet("originales")]
    public Task<IActionResult> Originales(int anio) => Ejecutar(async id => Ok(await servicio.ListarAsync(id, anio, "originales")));
    [Authorize(Roles = "SUPER_USUARIO")]
    [HttpGet("pendientes")]
    public Task<IActionResult> Pendientes(int anio) => Ejecutar(async id => Ok(await servicio.ListarAsync(id, anio, "pendientes")));
    [HttpGet("envios/{referencia:guid}")]
    public Task<IActionResult> Detalle(Guid referencia) => Ejecutar(async id => Ok(await servicio.DetalleAsync(id, referencia)));
    [HttpGet("envios/{referencia:guid}/acuse")]
    public Task<IActionResult> Acuse(Guid referencia) => Ejecutar(async id =>
        File(await servicio.AcuseAsync(id, referencia), "application/pdf", "BANCI_acuse.pdf"));
    [HttpPost("envios/{referencia:guid}/confirmar")]
    public Task<IActionResult> Confirmar(Guid referencia,
        BanciDecision decision) => Ejecutar(async id => Ok(await servicio.ResolverAsync(id, referencia, decision, false)));
    [Authorize(Roles = "SUPER_USUARIO")]
    [HttpPost("envios/{referencia:guid}/resolver")]
    public Task<IActionResult> Resolver(Guid referencia,
        BanciDecision decision) => Ejecutar(async id => Ok(await servicio.ResolverAsync(id, referencia, decision, true)));
    [HttpGet("envios/{referencia:guid}/original")]
    public Task<IActionResult> Original(Guid referencia) => Ejecutar(async id =>
    {
        var archivo = await servicio.OriginalAsync(id, referencia);
        return File(archivo.Bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", archivo.Nombre);
    });
    private async Task<IActionResult> Ejecutar(Func<int, Task<IActionResult>> accion)
    {
        if(!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return Unauthorized();
        try
        {
            return await accion(id);
        }
        catch(UnauthorizedAccessException ex)
        {
            return StatusCode(403, new
            {
                mensaje = ex.Message
            });
        }
        catch(KeyNotFoundException ex)
        {
            return NotFound(new
            {
                mensaje = ex.Message
            });
        }
        catch(BanciEnvioPendienteException ex)
        {
            return Conflict(new { codigo = "BANCI_ENVIO_PENDIENTE", mensaje = ex.Message });
        }
        catch(InvalidOperationException ex)
        {
            return BadRequest(new
            {
                mensaje = ex.Message
            });
        }
        catch(ArgumentException)
        {
            return BadRequest(new
            {
                mensaje = "El archivo o los datos recibidos no tienen un formato válido."
            });
        }
        catch(SqlException ex) when(ex.Number is 547 or 2601 or 2627 or 2628 or 8152)
        {
            logger.LogWarning(ex, "BANCI: conflicto de integridad. Usuario {Usuario}, Trace {Trace}", id, HttpContext.TraceIdentifier);
            return BadRequest(new
            {
                mensaje = "Los registros o catálogos cambiaron o no cumplen la integridad de la base. No se aplicó la operación; actualice la plantilla y valide nuevamente.",
                traceId = HttpContext.TraceIdentifier
            });
        }
        catch(Exception ex)
        {
            logger.LogError(ex, "Error de operación mensual BANCI. Usuario {Usuario}, Trace {Trace}", id, HttpContext.TraceIdentifier);
            return StatusCode(500, new
            {
                mensaje = "No fue posible completar la operación BANCI.",
                traceId = HttpContext.TraceIdentifier
            });
        }
    }
}
