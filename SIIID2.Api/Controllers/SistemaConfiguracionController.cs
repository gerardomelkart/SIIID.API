using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Repositories;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController, Authorize, Route("api/sistema"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SistemaConfiguracionController(SistemaConfiguracionService config, IDbConnectionFactory factory, IUsuarioRepository usuarios) : ControllerBase
{
    private int Id => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    [HttpGet("sesion")]
    public async Task<IActionResult> Sesion()
    {
        using var db = factory.CrearConexion();
        var nombre = await db.QuerySingleOrDefaultAsync<string>("SELECT usuario FROM dbo.usuario WHERE id_usuario = @Id AND activo = 1;", new { Id });
        if (nombre == null) return Unauthorized();
        var usuario = await usuarios.ObtenerUsuarioAuthAsync(nombre);
        if (usuario == null) return Unauthorized();
        using var bloqueo = await config.BloquearLecturaAsync();
        await config.CargarAsync();
        return Ok(new { administraSistema = await config.EsAdministradorAsync(Id), modulos = usuario.Modulos.Where(m => config.Modulos.Any(c => c.Clave == m.Clave && c.Activo)), version = config.Version, opciones = config.Opciones.Where(o => usuario.Modulos.Any(m => m.Clave == o.Modulo)).Select(o => new { o.Modulo, o.Clave, habilitado = config.Activa(o.Modulo, o.Clave) }) });
    }
    [HttpGet("configuracion")]
    public async Task<IActionResult> Obtener()
    {
        if (!await config.EsAdministradorAsync(Id)) return Forbid();
        using var bloqueo = await config.BloquearLecturaAsync();
        await config.CargarAsync();
        return Ok(new { version = config.Version, modulos = config.Modulos, opciones = config.Opciones.Select(o => new { o.Modulo, o.Clave, o.Descripcion, o.Habilitado, o.Disponible, efectivo = config.Activa(o.Modulo, o.Clave) }) });
    }
    [HttpPut("configuracion")]
    public async Task<IActionResult> Cambiar(SistemaCambio request)
    {
        if (!await config.EsAdministradorAsync(Id)) return Forbid();
        using var db = factory.CrearConexion();
        try { return Ok(await db.QuerySingleAsync("dbo.sp_sistema_configuracion_cambiar", new { IdUsuario = Id, request.Modulo, request.Clave, request.Habilitado, request.VersionEsperada, request.Motivo }, commandType: CommandType.StoredProcedure)); }
        catch (SqlException ex) when (ex.Number is >= 52600 and <= 52606) { return StatusCode(ex.Number == 52603 ? 403 : ex.Number is 52601 or 52604 ? 409 : 400, new { codigo = $"CONFIG_{ex.Number}", mensaje = ex.Message }); }
    }
    [HttpGet("bitacora")]
    public async Task<IActionResult> Bitacora([FromQuery, Range(1, 1000000)] int pagina = 1)
    {
        if (!await config.EsAdministradorAsync(Id)) return Forbid();
        using var db = factory.CrearConexion();
        return Ok(await db.QueryAsync("SELECT b.id, b.fecha_utc AS fechaUtc, u.usuario, b.modulo, b.clave, b.valor_anterior AS valorAnterior, b.valor_nuevo AS valorNuevo, b.motivo, b.version FROM dbo.sistema_configuracion_bitacora b LEFT JOIN dbo.usuario u ON u.id_usuario = b.id_usuario ORDER BY b.id DESC OFFSET @Offset ROWS FETCH NEXT 50 ROWS ONLY;", new { Offset = (pagina - 1) * 50 }));
    }
}
public sealed class SistemaCambio
{
    [Required, StringLength(20)] public string Modulo { get; set; } = "";
    [Required, StringLength(60)] public string Clave { get; set; } = "";
    [Required] public bool? Habilitado { get; set; }
    [Range(1, long.MaxValue)] public long VersionEsperada { get; set; }
    [Required, StringLength(500, MinimumLength = 3)] public string Motivo { get; set; } = "";
}
