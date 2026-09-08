using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Repositories;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_FEDERAL")]
[Authorize(Roles = "SUPER_USUARIO")]
[Route("api/federal/informes/archivos-originales")]
public class FederalArchivosOriginalesController : ControllerBase
{
    private readonly IFederalArchivosOriginalesService _archivosService;
    private readonly IFederalCargaRepository _federalCargaRepository;

    public FederalArchivosOriginalesController(IFederalArchivosOriginalesService archivosService, IFederalCargaRepository federalCargaRepository)
    {
        _archivosService = archivosService;
        _federalCargaRepository = federalCargaRepository;
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerResumen()
    {
        var autorizacion = await ValidarSuperUsuarioAsync();
        if (autorizacion != null) return autorizacion;
        try
        {
            var registros = await _archivosService.ObtenerResumenAsync();
            return Ok(new { esValido = true, total = registros.Count, registros });
        }
        catch (InvalidOperationException ex)
        {
            return ArchivosNoDisponibles(ex.Message);
        }
    }

    [HttpGet("descargar")]
    public async Task<IActionResult> Descargar()
    {
        var autorizacion = await ValidarSuperUsuarioAsync();
        if (autorizacion != null) return autorizacion;
        try
        {
            var zip = await _archivosService.DescargarAsync();
            return File(zip.Archivo, "application/zip", zip.NombreArchivo);
        }
        catch (InvalidOperationException ex)
        {
            return ArchivosNoDisponibles(ex.Message);
        }
    }

    private async Task<IActionResult?> ValidarSuperUsuarioAsync()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario))
            return Unauthorized(new { esValido = false, codigo = "GENERAL_TOKEN_SIN_ID_USUARIO", mensaje = "El token no contiene un id de usuario válido.", traceId = HttpContext.TraceIdentifier });

        var usuario = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuario);
        if (usuario == null || !usuario.EsSuperUsuario)
            return StatusCode(StatusCodes.Status403Forbidden, new { esValido = false, codigo = "FEDERAL_ARCHIVOS_ORIGINALES_SIN_PERMISO", mensaje = "Solo un SUPER_USUARIO con acceso activo al módulo Federal puede consultar o descargar archivos originales." });
        return null;
    }

    private IActionResult ArchivosNoDisponibles(string mensaje) => BadRequest(new { esValido = false, codigo = "FEDERAL_ARCHIVOS_ORIGINALES_NO_DISPONIBLES", mensaje });
}
