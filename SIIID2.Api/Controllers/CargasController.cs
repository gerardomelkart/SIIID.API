using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;
using SIIID2.Api.Repositories;

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
    [HttpPost("validar")]
    public async Task<IActionResult> ValidarArchivos()
    {
        // Evita errores 500 cuando el cliente manda body vacío, binary, raw, etc.
        // Para cargar archivos debe usarse multipart/form-data.
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
                        Mensaje = "Debe enviar los archivos en Body > form-data."
                    }
                }
            });
        }

        // Request.Form.Files contiene todos los archivos enviados en el form-data.
        // No dependemos del nombre de la llave; el servicio identifica cada archivo por su FileName.
        var archivos = Request.Form.Files;
        var resultado = await _cargaArchivosService.ValidarArchivosAsync(archivos);
        // Si hay errores de validación, se devuelve 400 con el detalle.
        if (!resultado.EsValido)
        {
            return BadRequest(resultado);
        }
        // Si no hay errores, se devuelve 200.
        return Ok(resultado);
    }


    [HttpGet("probar-bd")]
    public async Task<IActionResult> ProbarBd(
        [FromServices] ICatalogoRepository catalogoRepository)
    {
        var existe = await catalogoRepository.ExisteClaveNumericaAsync(
            "catalogo_tipo_victima",
            "clave",
            1);

        return Ok(new
        {
            conexion = "ok",
            catalogo = "catalogo_tipo_victima",
            clave = 1,
            existe
        });
    }
}
