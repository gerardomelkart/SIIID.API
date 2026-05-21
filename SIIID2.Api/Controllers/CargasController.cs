using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace SIIID2.Api.Controllers;

// Controlador del módulo de cargas.
// Aquí se reciben las peticiones HTTP, pero la lógica de validación vive en el servicio.
[ApiController]
[Route("api/cargas")]
public class CargasController : ControllerBase
{
    private readonly ICargaArchivosService _cargaArchivosService;
    private readonly IAcusePdfService _acusePdfService;

    // ASP.NET inyecta aquí las implementaciones registradas en Program.cs.
    public CargasController(ICargaArchivosService cargaArchivosService, IAcusePdfService acusePdfService)
    {
        _cargaArchivosService = cargaArchivosService;
        _acusePdfService = acusePdfService;
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
                mensaje = "El token no contiene un id de usuario válido."
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
                mensaje = "El token no contiene un id de usuario válido."
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
                $"ACUSE_PREVIO_{codigoReferencia}.pdf");
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


    // solo es prueba.
    //[HttpGet("{codigoReferencia}/acusenoseguro")]
    //public async Task<IActionResult> DescargarAcusenoseguro(string codigoReferencia)
    //{
    //    try
    //    {
    //        var pdf = await _acusePdfService.GenerarAcusePrevioAsync(
    //            codigoReferencia,
    //            1);   //harcodeamos el usuario super admin

    //        return File(
    //            pdf,
    //            "application/pdf",
    //            $"ACUSE_PREVIO_{codigoReferencia}.pdf");
    //    }
    //    catch (UnauthorizedAccessException ex)
    //    {
    //        return StatusCode(StatusCodes.Status403Forbidden, new
    //        {
    //            esValido = false,
    //            codigo = "ACUSE_SIN_PERMISO",
    //            mensaje = ex.Message
    //        });
    //    }
    //    catch (InvalidOperationException ex)
    //    {
    //        return BadRequest(new
    //        {
    //            esValido = false,
    //            codigo = "ACUSE_NO_DISPONIBLE",
    //            mensaje = ex.Message
    //        });
    //    }
    //}


}