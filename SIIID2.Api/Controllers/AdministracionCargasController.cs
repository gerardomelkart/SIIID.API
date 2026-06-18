using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Roles = "SUPER_USUARIO")]
[Route("api/administracion/cargas-pendientes")]
public class AdministracionCargasController : ControllerBase
{
    private readonly IAdministracionCargasService _administracionService;

    public AdministracionCargasController(IAdministracionCargasService administracionService)
    {
        _administracionService = administracionService;
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerPendientes()
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        var registros = await _administracionService.ObtenerPendientesAsync(idUsuario);

        return Ok( new {esValido = true, total = registros.Count, registros});
    }

    [HttpGet("{codigoReferencia}")]
    public async Task<IActionResult> ObtenerDetalle(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        var detalle = await _administracionService.ObtenerDetalleAsync(idUsuario, codigoReferencia);

        if (detalle == null)
        {
            return NotFound(new {esValido = false, codigo = "CARGA_PENDIENTE_NO_ENCONTRADA", mensaje = "No se encontro una carga pendiente con ese codigo."});
        }

        return Ok(new {esValido = true, detalle });
    }

    [HttpPost("{codigoReferencia}/aprobar")]
    public async Task<IActionResult> Aprobar(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        var resultado = await _administracionService.AprobarAsync(idUsuario, codigoReferencia);

        if (resultado.EsValido)
        {
            return Ok(resultado);
        }

        if (resultado.Estado == "NO_ENCONTRADA")
        {
            return NotFound(resultado);
        }

        return BadRequest(resultado);
    }

    [HttpPost("{codigoReferencia}/rechazar")]
    public async Task<IActionResult> Rechazar(string codigoReferencia, [FromBody] RechazarCargaAdministracionRequest request)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        var resultado = await _administracionService.RechazarAsync(idUsuario, codigoReferencia, request.Motivo);

        if (resultado.EsValido)
        {
            return Ok(resultado);
        }

        if (resultado.Estado == "NO_ENCONTRADA")
        {
            return NotFound(resultado);
        }

        return BadRequest(resultado);
    }

    private bool ObtenerIdUsuario(out int idUsuario)
    {
        var idUsuarioClaim = User.FindFirstValue( ClaimTypes.NameIdentifier);

        return int.TryParse(idUsuarioClaim, out idUsuario);
    }

    private IActionResult TokenSinUsuario()
    {
        return Unauthorized(new {esValido = false, codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
            mensaje = "El token no contiene un id de usuario valido.", traceId =  HttpContext.TraceIdentifier });
    }
}