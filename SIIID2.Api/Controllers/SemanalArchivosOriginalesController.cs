using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Repositories;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_SEMANAL")]
[Route("api/semanal/informes/archivos-originales")]
public class SemanalArchivosOriginalesController : ControllerBase
{
    private readonly IUltimosArchivosEntidadService _ultimosArchivosEntidadService;
    private readonly ISemanalCargaRepository _semanalCargaRepository;

    public SemanalArchivosOriginalesController(IUltimosArchivosEntidadService ultimosArchivosEntidadService, ISemanalCargaRepository semanalCargaRepository)
    {
        _ultimosArchivosEntidadService = ultimosArchivosEntidadService;
        _semanalCargaRepository = semanalCargaRepository;
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerArchivosOriginales()
    {
        var autorizacion = await ValidarSuperUsuarioAsync();

        if (autorizacion != null)
        {
            return autorizacion;
        }

        var registros =
            await _ultimosArchivosEntidadService.ObtenerResumenSemanalAsync();

        return Ok(new
        {
            esValido = true,
            total = registros.Count,
            registros
        });
    }

    [HttpGet("{idEntidadFederativa:int}/{idUsuarioCarga:int}")]
    public async Task<IActionResult> DescargarArchivosOriginales(int idEntidadFederativa, int idUsuarioCarga)
    {
        var autorizacion = await ValidarSuperUsuarioAsync();

        if (autorizacion != null) return autorizacion;

        try
        {
            var zip = await _ultimosArchivosEntidadService.DescargarSemanalAsync(
                idEntidadFederativa,
                idUsuarioCarga);

            return File(
                zip.Archivo,
                "application/zip",
                zip.NombreArchivo);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "SEMANAL_ARCHIVOS_ORIGINALES_NO_DISPONIBLES",
                mensaje = ex.Message
            });
        }
    }

    private async Task<IActionResult?> ValidarSuperUsuarioAsync()
    {
        var idUsuarioClaim =
            User.FindFirstValue(ClaimTypes.NameIdentifier);

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

        var usuario =
            await _semanalCargaRepository.ObtenerUsuarioCargaAsync(
                idUsuarioConsulta);

        if (usuario == null)
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "SEMANAL_USUARIO_SIN_ACCESO",
                mensaje = "El usuario no existe, está inactivo o no tiene acceso al módulo semanal.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        if (!usuario.EsSuperUsuario)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "SEMANAL_ARCHIVOS_ORIGINALES_SIN_PERMISO",
                mensaje = "Solo un SUPER_USUARIO puede consultar o descargar archivos originales semanales."
            });
        }

        return null;
    }
}