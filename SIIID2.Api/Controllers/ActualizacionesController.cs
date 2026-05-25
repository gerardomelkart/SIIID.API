using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Route("api/actualizaciones")]
public class ActualizacionesController : ControllerBase
{
    private readonly IActualizacionArchivosService _actualizacionArchivosService;

    public ActualizacionesController(IActualizacionArchivosService actualizacionArchivosService)
    {
        _actualizacionArchivosService = actualizacionArchivosService;
    }

    // Endpoint para validar archivos de actualización.
    // Ejemplo: POST /api/actualizaciones/validar
    [Authorize]
    [HttpPost("validar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ValidarActualizacion()
    {
        if (!Request.HasFormContentType)
        {
            return BadRequest(new CargaValidacionResponse
            {
                Mensaje = "La petición debe enviarse como multipart/form-data.",
                Errores = new List<CargaValidacionError>
                {
                    new CargaValidacionError
                    {
                        Archivo = "general",
                        Fila = null,
                        Columna = "",
                        Campo = "",
                        Valor = Request.ContentType,
                        Codigo = "GENERAL_CONTENT_TYPE_INVALIDO",
                        DescripcionResumen = "Tipo de petición inválido",
                        Mensaje = "La petición debe enviarse como multipart/form-data."
                    }
                }
            });
        }

        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioCarga))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var resultado = await _actualizacionArchivosService.ValidarActualizacionAsync(
            Request.Form,
            idUsuarioCarga);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }

    [Authorize]
    [HttpGet("diferencias/{codigoReferencia}")]
    public async Task<IActionResult> ObtenerDiferencias(string codigoReferencia)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioConsulta))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var resultado = await _actualizacionArchivosService.ObtenerDetalleDiferenciasAsync(
            codigoReferencia,
            idUsuarioConsulta);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }
}