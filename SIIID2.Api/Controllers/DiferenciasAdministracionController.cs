using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Roles = "SUPER_USUARIO")]
[Route("api/administracion/diferencias")]
public sealed class DiferenciasAdministracionController : ControllerBase
{
    [Authorize(Policy = "MODULO_MENSUAL")]
    [HttpGet("mensual/{referencia}/excel")]
    public Task<IActionResult> Mensual(string referencia, [FromServices] IActualizacionArchivosService servicio,
        [FromServices] IDbConnectionFactory db) => Exportar("mensual", async usuario =>
    {
        var resultado = await servicio.ObtenerDetalleDiferenciasAsync(referencia, usuario, int.MaxValue, incluirResumen: false);
        await CodigoPostalActualizacionHelper.AplicarDetalleAsync(db, referencia, resultado, int.MaxValue);
        return resultado;
    });

    [Authorize(Policy = "MODULO_FEDERAL")]
    [HttpGet("federal/{referencia}/excel")]
    public Task<IActionResult> Federal(string referencia, [FromServices] IFederalActualizacionArchivosService servicio) =>
        Exportar("federal", usuario => servicio.ObtenerDiferenciasAsync(referencia, usuario, int.MaxValue, incluirResumen: false));

    [Authorize(Policy = "MODULO_SEMANAL")]
    [HttpGet("semanal/{referencia}/excel")]
    public Task<IActionResult> Semanal(string referencia, [FromServices] ISemanalCargaService servicio) =>
        Exportar("semanal", usuario => servicio.ObtenerDiferenciasAsync(referencia, usuario, int.MaxValue));

    [Authorize(Policy = "MODULO_BANCI")]
    [HttpGet("banci/{referencia:guid}/excel")]
    public Task<IActionResult> Banci(Guid referencia, [FromServices] BanciMensualService servicio) => Exportar("banci", async usuario =>
    {
        var libro = await servicio.DetalleAsync(usuario, referencia);
        return new ActualizacionDiferenciasResponse
        {
            EsValido = true,
            Victimas = libro.Cambios.Select(c => new ActualizacionDiferenciaRegistro
            {
                TipoMovimiento = "MODIFICADO",
                CampoIdentificador = "id_ci + id_delito + id_vicf",
                IdentificadorFiscalia = $"{c.Carpeta} | {c.Delito} | {c.Victima}",
                CamposModificados = [new() { Campo = c.Campo, ValorAnterior = c.Anterior, ValorNuevo = c.Nuevo }]
            }).ToList()
        };
    });

    private async Task<IActionResult> Exportar(string modulo, Func<int, Task<ActualizacionDiferenciasResponse>> consultar)
    {
        if(!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuario)) return Unauthorized();
        try
        {
            var diferencias = await consultar(usuario);
            if(!diferencias.EsValido) return BadRequest(new { mensaje = diferencias.Mensaje });
            return File(DiferenciasExcel.Crear(diferencias), DiferenciasExcel.Mime, $"Diferencias_{modulo}.xlsx");
        }
        catch(SqlException ex) when(ex.Number == -2)
        {
            return StatusCode(504, new { mensaje = "La descarga excedió el tiempo de espera. Intente nuevamente; el envío continúa pendiente de aprobación." });
        }
        catch(UnauthorizedAccessException) { return Forbid(); }
        catch(KeyNotFoundException ex) { return NotFound(new { mensaje = ex.Message }); }
        catch(InvalidOperationException ex) { return BadRequest(new { mensaje = ex.Message }); }
    }
}
