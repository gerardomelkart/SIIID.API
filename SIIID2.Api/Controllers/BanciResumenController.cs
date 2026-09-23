using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Data;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

/// <summary>Acuse de registros asociados a una carga BANCI V2 ya integrada.
/// Restringido a quien registró la carga y tiene acceso BANCI vigente.</summary>
[ApiController]
[Authorize(Policy = "MODULO_BANCI")]
[Route("api/banci/cargas")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BanciResumenController : ControllerBase
{
    private readonly IBanciCargaService _cargas;
    private readonly IDbConnectionFactory _factory;

    public BanciResumenController(IBanciCargaService cargas, IDbConnectionFactory factory)
    {
        _cargas = cargas;
        _factory = factory;
    }

    [HttpGet("{codigoReferencia}/acuse")]
    public async Task<IActionResult> Acuse(string codigoReferencia)
    {
        var resumen = await Resumen(codigoReferencia);
        if (resumen is not OkObjectResult { Value: List<BanciResumenRegistro> filas }) return resumen;
        var bytes = BanciAcusePdf.Generar(filas, codigoReferencia);
        var entidad = string.Concat((filas.FirstOrDefault()?.Entidad ?? "entidad").Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var fecha = filas.FirstOrDefault()?.FechaIntegracion;
        return File(bytes, "application/pdf", $"BANCI_integracion_{entidad}_{fecha:yyyyMMdd_HHmmss}.pdf");
    }

    [HttpGet("{codigoReferencia}/resumen")]
    public async Task<IActionResult> Resumen(string codigoReferencia)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario))
            return Unauthorized(new { mensaje = "La sesión no tiene un usuario válido." });
        if (string.IsNullOrWhiteSpace(codigoReferencia) || codigoReferencia.Length > 50)
            return BadRequest(new { mensaje = "La referencia no es válida." });

        // La consulta ya verifica autoría, entidad y acceso vigente; no exponer acuses ajenos.
        var carga = await _cargas.ObtenerCargaAsync(codigoReferencia, idUsuario);
        if (carga is null) return NotFound(new { mensaje = "La carga no está disponible para este usuario." });
        if (carga.VersionFormato != 2 ||
            carga.Estado is not ("PROCESADO" or "PROCESADO_CON_ADVERTENCIAS"))
            return Conflict(new { mensaje = "El resumen únicamente se emite para una carga BANCI V2 integrada." });

        using var connection = _factory.CrearConexion();
        // El folio se lee de las tablas definitivas. Las tablas tmp sólo fijan qué llaves
        // pertenecieron a ESTA operación. No mostrar CURP ni datos personales ajenos al acuse.
        const string sql = """
            SELECT DISTINCT
                e.nombre AS Entidad,
                c.no_banci AS NoBanci,
                ci.id_ci AS IdCi,
                ci.ntra_ci AS NtraCi,
                di.id_delito AS IdDelito,
                vi.id_vicf AS IdVicf,
                v.folio_rnpdno AS FolioRnpdno,
                N'Registro asociado a la carga' AS Resultado,
                carga.fecha_confirmacion AS FechaIntegracion
            FROM dbo.banci_carga carga
            JOIN dbo.catalogo_entidad_federativa e
              ON e.id_entidad_federativa = carga.id_entidad_federativa
            JOIN dbo.banci_carga_tmp_victima vi
              ON vi.id_banci_carga = carga.id_banci_carga AND vi.activo = 1
            JOIN dbo.banci_carga_tmp_carpeta ci
              ON ci.id_banci_carga = carga.id_banci_carga
             AND ci.id_ci = vi.id_ci AND ci.activo = 1
            JOIN dbo.banci_carga_tmp_delito di
              ON di.id_banci_carga = carga.id_banci_carga
             AND di.id_ci = vi.id_ci AND di.id_delito = vi.id_delito AND di.activo = 1
            JOIN dbo.banci_carpeta_investigacion c
              ON c.id_entidad_federativa = carga.id_entidad_federativa
             AND c.id_ci = vi.id_ci AND c.activo = 1
            JOIN dbo.banci_delito d
              ON d.id_banci_carpeta_investigacion = c.id_banci_carpeta_investigacion
             AND d.id_delito = vi.id_delito AND d.activo = 1
            JOIN dbo.banci_victima v
              ON v.id_banci_delito = d.id_banci_delito
             AND v.id_vicf = vi.id_vicf AND v.activo = 1
            WHERE carga.id_banci_carga = @IdBanciCarga
              AND carga.id_usuario_carga = @IdUsuario
              AND carga.estado IN (N'PROCESADO', N'PROCESADO_CON_ADVERTENCIAS')
            ORDER BY IdCi, IdDelito, IdVicf;
            """;
        var filas = (await connection.QueryAsync<BanciResumenRegistro>(sql,
            new { carga.IdBanciCarga, IdUsuario = idUsuario }, commandTimeout: 120)).ToList();

        // Si el mantenimiento ya eliminó los archivos temporales o una llave dejó de existir,
        // nunca presentar un acuse incompleto como si fuera el resultado definitivo.
        if (filas.Count != carga.TotalVictimas || filas.Any(f => string.IsNullOrWhiteSpace(f.NoBanci)))
            return Conflict(new
            {
                mensaje = "No se pudo reconstruir el acuse completo desde los registros actuales. " +
                "No se entregará un resumen parcial; consulte la carga y sus identificadores en BANCI."
            });

        return Ok(filas);
    }

    public sealed class BanciResumenRegistro
    {
        public string Entidad { get; set; } = "";
        public string NoBanci { get; set; } = "";
        public string IdCi { get; set; } = "";
        public string NtraCi { get; set; } = "";
        public string IdDelito { get; set; } = "";
        public string IdVicf { get; set; } = "";
        public string? FolioRnpdno { get; set; }
        public string Resultado { get; set; } = "";
        public DateTime? FechaIntegracion { get; set; }
    }
}
