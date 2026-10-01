using System.Security.Claims;
using ClosedXML.Excel;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using SIIID2.Api.Readers;
using SIIID2.Api.Services;
namespace SIIID2.Api.Controllers;

[ApiController, Authorize, Route("api/banci/cruce/{modulo}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BanciCruceController(IDbConnectionFactory factory, SistemaConfiguracionService config,
    BanciActualizacionService actualizaciones, BanciCruceService cruce, IAuthorizationService autorizacion) : ControllerBase
{
    public sealed record Resultado(bool Activo, bool PuedeActualizar, string? Motivo, List<IDictionary<string, object>> Victimas);
    [HttpPost("{referencia}/preparar")]
    public Task<IActionResult> Preparar(string modulo, string referencia) => Ejecutar(async () => Ok(await ObtenerAsync(modulo, referencia, true)));
    [HttpGet("{referencia}")]
    public Task<IActionResult> Consultar(string modulo, string referencia) => Ejecutar(async () => Ok(await ObtenerAsync(modulo, referencia, false)));
    [HttpGet("{referencia}/plantilla")]
    public Task<IActionResult> Plantilla(string modulo, string referencia, [FromQuery] int? entidad) => Ejecutar(async () =>
    {
        var resultado = await ObtenerAsync(modulo, referencia, false);
        if (!resultado.PuedeActualizar) return Forbid();
        var filas = resultado.Victimas.Where(v => !entidad.HasValue || Convert.ToInt32(v["id_entidad_federativa"]) == entidad.Value).ToList();
        if (filas.Count == 0) return BadRequest(new { mensaje = "No hay víctimas disponibles para esta carga y entidad." });
        if (filas.Select(v => Convert.ToInt32(v["id_entidad_federativa"])).Distinct().Count() != 1) return BadRequest(new { mensaje = "Seleccione una entidad para descargar su plantilla BANCI." });
        var opciones = await actualizaciones.OpcionesAsync(Usuario());
        using var entrada = new MemoryStream(BanciActualizacionReader.Plantilla(opciones.Catalogos));
        using var libro = new XLWorkbook(entrada);
        var hoja = libro.Worksheet("Actualizacion");
        var columnas = hoja.Row(1).CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < filas.Count; i++) foreach (var campo in new[] { "no_banci", "id_delito", "id_vicf" }) hoja.Cell(i + 2, columnas[campo]).SetValue(Convert.ToString(filas[i][campo]) ?? "");
        using var salida = new MemoryStream(); libro.SaveAs(salida);
        return File(salida.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "BANCI_actualizacion_desde_carga.xlsx");
    });
    private async Task<IActionResult> Ejecutar(Func<Task<IActionResult>> accion)
    {
        try { return await accion(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException ex) { return BadRequest(new { mensaje = ex.Message }); }
        catch (BanciCruceException ex) { return BadRequest(new CargaValidacionResponse { Errores = ex.Errores, Mensaje = ex.Message }); }
    }
    private int Usuario() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException();
    private async Task<Resultado> ObtenerAsync(string modulo, string referencia, bool preparar)
    {
        var federal = modulo.Equals("federal", StringComparison.OrdinalIgnoreCase);
        if (!federal && !modulo.Equals("mensual", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Módulo inválido.");
        var clave = federal ? "FEDERAL" : "MENSUAL";
        if (!(await autorizacion.AuthorizeAsync(User, null, "MODULO_" + clave)).Succeeded) throw new UnauthorizedAccessException();
        using var bloqueo = await config.BloquearLecturaAsync();
        await config.CargarAsync();
        if (!config.Activa(clave, "CRUCE_BANCI")) return new(false, false, null, []);
        var usuario = Usuario();
        var tabla = federal ? "federal_carga" : "carga"; var columna = federal ? "id_federal_carga" : "id_carga"; var tablaCruce = federal ? "banci_cruce_federal" : "banci_cruce_mensual";
        using var db = factory.CrearConexion(); db.Open(); using var tx = db.BeginTransaction();
        var carga = await db.QuerySingleOrDefaultAsync($"SELECT {columna} id,id_usuario_carga usuario,estado FROM dbo.{tabla} WITH(UPDLOCK,HOLDLOCK) WHERE codigo_referencia=@referencia AND activo=1;", new { referencia }, tx);
        if (carga is null) throw new UnauthorizedAccessException();
        if ((int)carga.usuario != usuario) throw new UnauthorizedAccessException();
        if ((string)carga.estado is not ("VALIDADO_PENDIENTE" or "VALIDADO_PENDIENTE_ACTUALIZACION")) throw new ArgumentException("La carga ya no está pendiente de aceptar o rechazar. Actualice su estado.");
        if (preparar) await cruce.ConfirmarAsync(db, tx, (long)carga.id, federal);
        var filas = (await db.QueryAsync($"""
            SELECT v.* FROM dbo.{tablaCruce} cr CROSS APPLY OPENJSON(cr.victimas_json) j
            JOIN dbo.banci_vw_victimas_v2 v ON v.id_banci_victima=TRY_CONVERT(bigint,j.value)
            WHERE cr.{columna}=@id AND cr.fecha_cruce_utc IS NOT NULL
            ORDER BY v.id_entidad_federativa,v.ntra_ci,v.id_vicf;
            """, new { id = (long)carga.id }, tx)).Select(f => (IDictionary<string, object>)f).ToList();
        tx.Commit();
        if (filas.Count == 0) return new(true, false, null, []);
        try { await actualizaciones.AutorizarAsync(usuario, true); }
        catch (UnauthorizedAccessException) { return new(true, false, "Su cuenta no tiene permiso vigente de modificación BANCI. Puede omitir la actualización opcional; solicite el permiso si necesita realizarla.", []); }
        return new(true, true, null, filas);
    }
}
