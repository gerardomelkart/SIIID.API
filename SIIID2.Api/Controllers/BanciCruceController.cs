using System.Security.Claims;
using ClosedXML.Excel;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Data;
using SIIID2.Api.Readers;
using SIIID2.Api.Repositories;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController, Authorize(Policy = "MODULO_MENSUAL")]
[Route("api/banci/cruce/mensual")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BanciCruceController(IDbConnectionFactory factory, SistemaConfiguracionService config, IUsuarioRepository usuarios, BanciActualizacionService actualizaciones) : ControllerBase
{
    [HttpGet("{referencia}")]
    public async Task<IActionResult> Consultar(string referencia)
    {
        try { return Ok(new { victimas = await ObtenerAsync(referencia), actualizacionOpcional = true }); }
        catch (UnauthorizedAccessException) { return Ok(new { victimas = Array.Empty<object>(), actualizacionOpcional = true }); }
    }

    [HttpGet("{referencia}/plantilla")]
    public async Task<IActionResult> Plantilla(string referencia)
    {
        try
        {
            var filas = await ObtenerAsync(referencia);
            if (filas.Count == 0) return BadRequest(new { mensaje = "No hay víctimas disponibles para actualizar en esta carga." });
            var opciones = await actualizaciones.OpcionesAsync(Usuario());
            using var entrada = new MemoryStream(BanciActualizacionReader.Plantilla(opciones.Catalogos));
            using var libro = new XLWorkbook(entrada);
            var hoja = libro.Worksheet("Actualizacion");
            var columnas = hoja.Row(1).CellsUsed().ToDictionary(c => c.GetString(), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < filas.Count; i++)
                foreach (var campo in new[] { "no_banci", "id_delito", "id_vicf" })
                    hoja.Cell(i + 2, columnas[campo]).SetValue(Convert.ToString(filas[i][campo]) ?? "");
            using var salida = new MemoryStream();
            libro.SaveAs(salida);
            return File(salida.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "BANCI_actualizacion_desde_consolidado.xlsx");
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    private int Usuario() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException();
    private async Task<List<IDictionary<string, object>>> ObtenerAsync(string referencia)
    {
        await config.CargarAsync();
        if (!config.Activa("MENSUAL", "CRUCE_BANCI")) return [];
        var usuario = await usuarios.ObtenerUsuarioCargaAsync(Usuario()) ?? throw new UnauthorizedAccessException();
        var banci = await actualizaciones.AutorizarAsync(Usuario(), true);
        using var db = factory.CrearConexion();
        var carga = await db.QuerySingleOrDefaultAsync("SELECT id_carga, id_usuario_carga, id_entidad_federativa FROM dbo.carga WHERE codigo_referencia=@referencia AND activo=1 AND estado IN ('CONFIRMADO','CONFIRMADO_ACTUALIZACION');", new { referencia });
        if (carga == null) return [];
        if (!usuario.EsSuperUsuario && (int)carga.id_usuario_carga != usuario.IdUsuario) throw new UnauthorizedAccessException();
        if (!banci.EsSuperUsuario && banci.IdEntidadFederativa != (int)carga.id_entidad_federativa) throw new UnauthorizedAccessException();
        var filas = await db.QueryAsync("""
            SELECT v.* FROM dbo.banci_cruce_mensual cr
            CROSS APPLY OPENJSON(cr.victimas_json) j
            JOIN dbo.banci_vw_victimas_v2 v ON v.id_banci_victima=TRY_CONVERT(bigint,j.value)
            WHERE cr.id_carga=@id AND cr.fecha_cruce_utc IS NOT NULL AND v.id_entidad_federativa=@entidad
            ORDER BY v.ntra_ci, v.id_vicf;
            """, new { id = (long)carga.id_carga, entidad = (int)carga.id_entidad_federativa });
        return filas.Select(f => (IDictionary<string, object>)f).ToList();
    }
}
