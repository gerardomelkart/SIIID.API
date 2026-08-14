using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_FEDERAL")]
[Route("api/federal/catalogos")]
public class FederalCatalogosController : ControllerBase
{
    private readonly IFederalCatalogoRepository _federalCatalogoRepository;

    public FederalCatalogosController(IFederalCatalogoRepository federalCatalogoRepository)
    {
        _federalCatalogoRepository = federalCatalogoRepository;
    }

    [HttpGet("resumen")]
    public async Task<IActionResult> ObtenerResumen()
    {
        var resultado = await _federalCatalogoRepository.ObtenerResumenAsync();

        return Ok(resultado);
    }

    [HttpGet("bienes-juridicos")]
    public async Task<IActionResult> ObtenerBienesJuridicos()
    {
        var resultado = await _federalCatalogoRepository.ObtenerBienesJuridicosAsync();

        return Ok(resultado);
    }

    [HttpGet("delitos")]
    public async Task<IActionResult> ObtenerDelitos([FromQuery] int? idBienJuridico)
    {
        var resultado = await _federalCatalogoRepository.ObtenerDelitosAsync(idBienJuridico);

        return Ok(resultado);
    }

    [HttpGet("subtipos")]
    public async Task<IActionResult> ObtenerSubtipos([FromQuery] int? idDelito)
    {
        var resultado = await _federalCatalogoRepository.ObtenerSubtiposAsync(idDelito);

        return Ok(resultado);
    }

    [HttpGet("modalidades")]
    public async Task<IActionResult> ObtenerModalidades([FromQuery] int? idSubtipoDelito)
    {
        var resultado = await _federalCatalogoRepository.ObtenerModalidadesAsync(idSubtipoDelito);

        return Ok(resultado);
    }
}