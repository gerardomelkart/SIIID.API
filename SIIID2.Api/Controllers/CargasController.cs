using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Controllers;

// Controlador del módulo de cargas.
// Aquí se reciben las peticiones HTTP, pero la lógica de validación vive en el servicio.
[ApiController]
[Authorize(Policy = "MODULO_MENSUAL")]
[Route("api/cargas")]
public class CargasController : ControllerBase
{
    private readonly ICargaArchivosService _cargaArchivosService;
    private readonly IAcusePdfService _acusePdfService;
    private readonly ICargaRepository _cargaRepository;
    private readonly ILogger<CargasController> _logger;

    // ASP.NET inyecta aquí las implementaciones registradas en Program.cs.
    public CargasController(ICargaArchivosService cargaArchivosService, IAcusePdfService acusePdfService, ICargaRepository cargaRepository, ILogger<CargasController> logger)
    {
        _cargaArchivosService = cargaArchivosService;
        _acusePdfService = acusePdfService;
        _cargaRepository = cargaRepository;
        _logger = logger;
    }

    // Endpoint para validar los archivos antes de insertar información en base de datos.
    // Ejemplo: POST /api/cargas/validar
    [Authorize]
    [HttpPost("validar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ValidarArchivos()
    {
        // La petición debe venir como form-data porque incluye archivos.
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
                        Valor = null,
                        Codigo = "GENERAL_CONTENT_TYPE_INVALIDO",
                        DescripcionResumen = "Tipo de petición inválido",
                        Mensaje = "La petición debe enviarse como multipart/form-data."
                    }
                }
            });
        }

        // El usuario se obtiene del Bearer Token.
        // No se debe confiar en un id enviado por form-data.
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

        // El service recibe el form completo y el usuario autenticado.
        var resultado = await _cargaArchivosService.ValidarArchivosAsync(
            Request.Form,
            idUsuarioCarga);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }


    // Endpoint para descargar el acuse previo en PDF.
    // Ejemplo: GET /api/cargas/abc123/acuse
    [Authorize]
    [HttpGet("{codigoReferencia}/acuse")]
    public async Task<IActionResult> DescargarAcuse(string codigoReferencia)
    {
        // El usuario se obtiene del Bearer Token.
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
            var pdf = await _acusePdfService.GenerarAcusePrevioAsync(
                codigoReferencia,
                idUsuarioConsulta);

            return File(
                pdf,
                "application/pdf",
                $"INFORME_PREVIO_{codigoReferencia}.pdf");
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


    // Endpoint para confirmar o rechazar una carga previamente validada.
    // Ejemplo: POST /api/cargas/confirmar
    [Authorize]
    [HttpPost("confirmar")]
    public async Task<IActionResult> ConfirmarCarga([FromBody] ConfirmarCargaRequest request)
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

        if (string.IsNullOrWhiteSpace(request.CodigoReferencia))
        {
            return BadRequest(new ConfirmarCargaResponse
            {
                EsValido = false,
                Estado = "SOLICITUD_INVALIDA",
                Mensaje = "Debe enviar el código de referencia."
            });
        }

        var resultado = await _cargaRepository.ConfirmarCargaAsync(
            request.CodigoReferencia,
            request.Aceptar,
            idUsuarioConfirmacion);

        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }

        return Ok(resultado);
    }

    // Endpoint para descargar el acuse de carga confirmada en PDF.
    // Ejemplo: GET /api/cargas/abc123/acuse-confirmado
    [Authorize]
    [HttpGet("{codigoReferencia}/acuse-confirmado")]
    public async Task<IActionResult> DescargarAcuseConfirmado(string codigoReferencia)
    {
        // El usuario se obtiene del Bearer Token.
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
            var pdf = await _acusePdfService.GenerarAcuseConfirmadoAsync(
                codigoReferencia,
                idUsuarioConsulta);

            return File(
                pdf,
                "application/pdf",
                $"ACUSE_CARGA_{codigoReferencia}.pdf");
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

    // Endpoint especial para migración histórica.
    // No se usa desde el frontend.
    // Usa el usuario del token y confirma directamente la carga.
    //[Authorize]
    //[HttpPost("migracion-directa")]
    //[Consumes("multipart/form-data")]
    //public async Task<IActionResult> CargarMigracionDirecta()
    //{
    //    if (!Request.HasFormContentType)
    //    {
    //        return BadRequest(new ConfirmarCargaResponse
    //        {
    //            EsValido = false,
    //            Estado = "SOLICITUD_INVALIDA",
    //            Mensaje = "La petición debe enviarse como multipart/form-data."
    //        });
    //    }

    //    var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

    //    if (!int.TryParse(idUsuarioClaim, out var idUsuarioCarga))
    //    {
    //        return Unauthorized(new
    //        {
    //            esValido = false,
    //            codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
    //            mensaje = "El token no contiene un id de usuario válido.",
    //            traceId = HttpContext.TraceIdentifier
    //        });
    //    }

    //    var resultado = await _cargaArchivosService.CargarMigracionDirectaAsync(
    //        Request.Form,
    //        idUsuarioCarga);

    //    if (!resultado.EsValido)
    //    {
    //        return BadRequest(resultado);
    //    }

    //    return Ok(resultado);
    //}
}