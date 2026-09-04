using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_FEDERAL")]
[Authorize(Roles = "SUPER_USUARIO")]
[Route("api/federal/administracion/cargas-pendientes")]
public class FederalAdministracionCargasController : ControllerBase
{
    private readonly IFederalCargaRepository _federalCargaRepository;

    public FederalAdministracionCargasController(IFederalCargaRepository federalCargaRepository)
    {
        _federalCargaRepository = federalCargaRepository;
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerPendientes()
    {
        if (!ObtenerIdUsuario(out _)) return TokenSinUsuario();

        var registros = await _federalCargaRepository.ObtenerPendientesAdministracionAsync();

        return Ok(new
        {
            esValido = true,
            total = registros.Count,
            registros
        });
    }

    [HttpGet("{codigoReferencia}")]
    public async Task<IActionResult> ObtenerDetalle(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out _)) return TokenSinUsuario();

        var detalle = await _federalCargaRepository.ObtenerDetalleAdministracionAsync(codigoReferencia.Trim());

        if (detalle == null)
        {
            return NotFound(new
            {
                esValido = false,
                codigo = "FEDERAL_CARGA_NO_PENDIENTE",
                codigoReferencia,
                mensaje = "No se encontró una carga federal pendiente con ese código de referencia."
            });
        }

        return Ok(new
        {
            esValido = true,
            detalle
        });
    }

    [HttpPost("{codigoReferencia}/aprobar")]
    public async Task<IActionResult> Aprobar(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _federalCargaRepository.AprobarCargaPendienteAsync(codigoReferencia.Trim(), idUsuario);

        if (resultado.EsValido) return Ok(resultado);
        if (resultado.Estado == "NO_ENCONTRADA") return NotFound(resultado);
        if (resultado.Estado is "CONFIRMADO" or "RECHAZADO_ADMIN") return Conflict(resultado);

        return BadRequest(resultado);
    }

    [HttpPost("{codigoReferencia}/rechazar")]
    public async Task<IActionResult> Rechazar(string codigoReferencia, [FromBody] RechazarCargaAdministracionRequest request)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _federalCargaRepository.RechazarCargaPendienteAsync(codigoReferencia.Trim(), idUsuario, request.Motivo);

        if (resultado.EsValido) return Ok(resultado);
        if (resultado.Estado == "NO_ENCONTRADA") return NotFound(resultado);
        if (resultado.Estado is "CONFIRMADO" or "RECHAZADO_ADMIN") return Conflict(resultado);

        return BadRequest(resultado);
    }

    private bool ObtenerIdUsuario(out int idUsuario) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idUsuario);

    private IActionResult TokenSinUsuario()
    {
        return Unauthorized(new
        {
            esValido = false,
            codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
            mensaje = "El token no contiene un id de usuario válido.",
            traceId = HttpContext.TraceIdentifier
        });
    }
}