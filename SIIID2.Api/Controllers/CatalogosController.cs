using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Controllers;

[ApiController]
[Route("api/catalogos")]
public class CatalogosController : ControllerBase
{
    private readonly ICatalogoRepository _catalogoRepository;

    public CatalogosController(ICatalogoRepository catalogoRepository)
    {
        _catalogoRepository = catalogoRepository;
    }

    // Obtiene las entidades federativas activas para combos del front.
    // Ejemplo: GET /api/catalogos/entidades-federativas
    [Authorize]
    [HttpGet("entidades-federativas")]
    public async Task<IActionResult> ObtenerEntidadesFederativas()
    {
        var entidades = await _catalogoRepository.ObtenerEntidadesFederativasActivasAsync();

        return Ok(entidades);
    }

    // Obtiene los roles activos para combos del front.
    // Ejemplo: GET /api/catalogos/roles
    [Authorize]
    [HttpGet("roles")]
    public async Task<IActionResult> ObtenerRoles()
    {
        var roles = await _catalogoRepository.ObtenerRolesActivosAsync();

        return Ok(roles);
    }
}