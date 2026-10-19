using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Services;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/plantillas")]
public sealed class PlantillasController(CarpetasValidator carpetas, DelitosValidator delitos, VictimasValidator victimas) : ControllerBase
{
    [Authorize(Policy = "MODULO_MENSUAL")]
    [HttpGet("mensual/{archivo}")]
    public IActionResult Mensual(string archivo) => Descargar("mensual", archivo);

    [Authorize(Policy = "MODULO_FEDERAL")]
    [HttpGet("federal/{archivo}")]
    public IActionResult Federal(string archivo) => Descargar("federal", archivo);

    [Authorize(Policy = "MODULO_SEMANAL")]
    [HttpGet("semanal/{archivo}")]
    public IActionResult Semanal(string archivo) => Descargar("semanal", archivo);

    private IActionResult Descargar(string modulo, string archivo)
    {
        if(archivo is not ("carpetas" or "delitos" or "victimas")) return NotFound();
        return File(PlantillasExcel.Crear(modulo, archivo, carpetas, delitos, victimas), DiferenciasExcel.Mime, $"{archivo}.xlsx");
    }
}
