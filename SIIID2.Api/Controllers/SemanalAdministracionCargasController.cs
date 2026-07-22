using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Roles = "SUPER_USUARIO")]
[Route("api/semanal/administracion/cargas-pendientes")]
public class SemanalAdministracionCargasController : ControllerBase
{
    private readonly ISemanalAdministracionCargasService _administracionService;

    public SemanalAdministracionCargasController(ISemanalAdministracionCargasService administracionService) => _administracionService = administracionService;

    [HttpGet]
    public async Task<IActionResult> ObtenerPendientes()
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var registros = await _administracionService.ObtenerPendientesAsync(idUsuario);
        return Ok(new { esValido = true, total = registros.Count, registros });
    }

    [HttpGet("{codigoReferencia}")]
    public async Task<IActionResult> ObtenerDetalle(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var detalle = await _administracionService.ObtenerDetalleAsync(idUsuario, codigoReferencia);

        if (detalle != null) return Ok(new { esValido = true, detalle });

        var referencia = await _administracionService.ObtenerReferenciaAsync(idUsuario, codigoReferencia);

        if (referencia == null)
        {
            return NotFound(new
            {
                esValido = false,
                codigo = "CARGA_SEMANAL_NO_ENCONTRADA",
                codigoReferencia,
                mensaje = "No se encontró una carga semanal con ese código de referencia."
            });
        }

        return Conflict(new
        {
            esValido = false,
            codigo = "CARGA_SEMANAL_NO_PENDIENTE",
            codigoReferencia = referencia.CodigoReferencia,
            estado = referencia.Estado,
            mensaje = $"La carga semanal ya no se encuentra pendiente de aprobación. Estado actual: {referencia.Estado}."
        });
    }

    [HttpPost("{codigoReferencia}/aprobar")]
    public async Task<IActionResult> Aprobar(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _administracionService.AprobarAsync(idUsuario, codigoReferencia);

        if (resultado.EsValido) return Ok(resultado);
        if (resultado.Estado == "NO_ENCONTRADA") return NotFound(resultado);
        if (EsCargaYaResuelta(resultado.Estado)) return Conflict(resultado);

        return BadRequest(resultado);
    }

    [HttpPost("{codigoReferencia}/rechazar")]
    public async Task<IActionResult> Rechazar(string codigoReferencia, [FromBody] RechazarCargaSemanalAdministracionRequest request)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _administracionService.RechazarAsync(idUsuario, codigoReferencia, request.Motivo);

        if (resultado.EsValido) return Ok(resultado);
        if (resultado.Estado == "NO_ENCONTRADA") return NotFound(resultado);
        if (EsCargaYaResuelta(resultado.Estado)) return Conflict(resultado);

        return BadRequest(resultado);
    }

    [HttpGet("{codigoReferencia}/archivos")]
    public async Task<IActionResult> DescargarArchivos(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        try
        {
            var zip = await _administracionService.GenerarZipArchivosPendientesAsync(idUsuario, codigoReferencia);
            return File(zip.Archivo, "application/zip", zip.NombreArchivo);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                esValido = false,
                codigo = "CARGA_SEMANAL_NO_ENCONTRADA",
                codigoReferencia,
                mensaje = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                esValido = false,
                codigo = "CARGA_SEMANAL_NO_PENDIENTE",
                codigoReferencia,
                mensaje = ex.Message
            });
        }
    }

    private bool ObtenerIdUsuario(out int idUsuario) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idUsuario);

    private IActionResult TokenSinUsuario() => Unauthorized(new
    {
        esValido = false,
        codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
        mensaje = "El token no contiene un id de usuario válido.",
        traceId = HttpContext.TraceIdentifier
    });

    private static bool EsCargaYaResuelta(string? estado) =>
        string.Equals(estado, "CONFIRMADO", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(estado, "RECHAZADO_ADMIN", StringComparison.OrdinalIgnoreCase);
}