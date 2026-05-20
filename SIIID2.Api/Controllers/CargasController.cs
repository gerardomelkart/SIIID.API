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
    // ASP.NET inyecta aquí la implementación registrada en Program.cs.
    public CargasController(ICargaArchivosService cargaArchivosService)
    {
        _cargaArchivosService = cargaArchivosService;
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
}
