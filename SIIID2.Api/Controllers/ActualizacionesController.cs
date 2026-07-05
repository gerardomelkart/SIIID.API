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
    private readonly IAcusePdfService _acusePdfService;

    public ActualizacionesController(IActualizacionArchivosService actualizacionArchivosService, IAcusePdfService acusePdfService)
    {
        _actualizacionArchivosService = actualizacionArchivosService;
        _acusePdfService = acusePdfService;
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
    public async Task<IActionResult> ObtenerDiferencias(string codigoReferencia, [FromQuery] int limitePorSeccion = 100)
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

        if (limitePorSeccion < 0)
        {
            limitePorSeccion = 100;
        }

        if (limitePorSeccion <= 0)
        {
            limitePorSeccion = 100;
        }

        if (limitePorSeccion > 200)
        {
            limitePorSeccion = 200;
        }

        var resultado = await _actualizacionArchivosService.ObtenerDetalleDiferenciasAsync(
            codigoReferencia,
            idUsuarioConsulta,
            limitePorSeccion);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }

    [Authorize]
    [HttpPost("confirmar")]
    public async Task<IActionResult> ConfirmarActualizacion([FromBody] ConfirmarCargaRequest request)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioClaim, out var idUsuarioConfirmacion))
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var resultado = await _actualizacionArchivosService.ConfirmarActualizacionAsync(
            request,
            idUsuarioConfirmacion);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }

    [Authorize]
    [HttpGet("{codigoReferencia}/acuse")]
    public async Task<IActionResult> DescargarAcusePrevioActualizacion(string codigoReferencia)
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

        try
        {
            var pdf = await _acusePdfService.GenerarAcusePrevioActualizacionAsync(
                codigoReferencia,
                idUsuarioConsulta);

            return File(
                pdf,
                "application/pdf",
                $"INFORME_PREVIO_ACTUALIZACION_{codigoReferencia}.pdf");
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "ACUSE_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "ACUSE_NO_DISPONIBLE",
                mensaje = ex.Message
            });
        }
    }

    [Authorize]
    [HttpGet("{codigoReferencia}/acuse-confirmado")]
    public async Task<IActionResult> DescargarAcuseConfirmadoActualizacion(string codigoReferencia)
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

        try
        {
            var pdf = await _acusePdfService.GenerarAcuseConfirmadoActualizacionAsync(
                codigoReferencia,
                idUsuarioConsulta);

            return File(
                pdf,
                "application/pdf",
                $"ACUSE_ACTUALIZACION_{codigoReferencia}.pdf");
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "ACUSE_SIN_PERMISO",
                mensaje = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "ACUSE_NO_DISPONIBLE",
                mensaje = ex.Message
            });
        }
    }

    [Authorize]
    [HttpGet("periodo")]
    public async Task<IActionResult> ConsultarPeriodoActualizacion([FromQuery] int mesCorte, [FromQuery] int anioCorte, [FromQuery] int? idEntidadFederativa = null)
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

        var resultado = await _actualizacionArchivosService.ConsultarPeriodoActualizacionAsync(
            mesCorte,
            anioCorte,
            idUsuarioConsulta,
            idEntidadFederativa);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }

    [Authorize]
    [HttpGet("periodos-disponibles")]
    public async Task<IActionResult> ObtenerPeriodosDisponiblesActualizacion([FromQuery] int? idEntidadFederativa = null)
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

        var periodos = await _actualizacionArchivosService.ObtenerPeriodosDisponiblesActualizacionAsync(
            idUsuarioConsulta,
            idEntidadFederativa);

        return Ok(periodos);
    }
}
