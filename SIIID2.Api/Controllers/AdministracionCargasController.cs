using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Roles = "SUPER_USUARIO")]
[Route("api/administracion/cargas-pendientes")]
public class AdministracionCargasController : ControllerBase
{
    private readonly IAdministracionCargasService _administracionService;
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public AdministracionCargasController(IAdministracionCargasService administracionService, IDbConnectionFactory dbConnectionFactory)
    {
        _administracionService = administracionService;
        _dbConnectionFactory = dbConnectionFactory;
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerPendientes()
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        var registros = await _administracionService.ObtenerPendientesAsync(idUsuario);

        return Ok(new {esValido = true, total = registros.Count, registros});
    }

    [HttpGet("{codigoReferencia}")]
    public async Task<IActionResult> ObtenerDetalle(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        var detalle = await _administracionService.ObtenerDetalleAsync(idUsuario, codigoReferencia);

        if (detalle != null)
        {
            return Ok(new
            {
                esValido = true,
                detalle
            });
        }

        /*
            Si no apareció en el detalle pendiente, revisamos si
            la carga existe pero ya fue aprobada o rechazada.
        */
        var referencia = await _administracionService.ObtenerReferenciaAsync(idUsuario, codigoReferencia);

        if (referencia == null)
        {
            return NotFound(new
            {
                esValido = false,
                codigo = "CARGA_NO_ENCONTRADA",
                codigoReferencia,
                mensaje = "No se encontro una carga con ese codigo de referencia."
            });
        }

        return Conflict(new
        {
            esValido = false,
            codigo = "CARGA_NO_PENDIENTE",
            codigoReferencia = referencia.CodigoReferencia,
            tipoCarga = referencia.TipoCarga,
            estado = referencia.Estado,
            mensaje = $"La carga ya no se encuentra pendiente de aprobacion. Estado actual: {referencia.Estado}."
        });
    }

    [HttpPost("{codigoReferencia}/aprobar")]
    public async Task<IActionResult> Aprobar(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        var resultadoCp = await CodigoPostalCatalogoHelper.AsegurarAsync(_dbConnectionFactory, codigoReferencia);

        if (resultadoCp?.CodigosSinPlantilla > 0)
        {
            return BadRequest(new
            {
                esValido = false,
                codigo = "CODIGO_POSTAL_SIN_PLANTILLA_MUNICIPIO",
                mensaje = "Hay códigos postales válidos que no pudieron agregarse porque el municipio no tiene una referencia previa en el catálogo de códigos postales.",
                total = resultadoCp.CodigosSinPlantilla
            });
        }

        var resultado = await _administracionService.AprobarAsync(idUsuario, codigoReferencia);

        if (resultado.EsValido)
        {
            return Ok(resultado);
        }

        if (resultado.Estado == "NO_ENCONTRADA")
        {
            return NotFound(resultado);
        }

        if (EsCargaYaResuelta(resultado.Estado))
        {
            return Conflict(resultado);
        }

        return BadRequest(resultado);
    }

    [HttpPost("{codigoReferencia}/rechazar")]
    public async Task<IActionResult> Rechazar(string codigoReferencia, [FromBody] RechazarCargaAdministracionRequest request)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        var resultado = await _administracionService.RechazarAsync(idUsuario, codigoReferencia, request.Motivo);

        if (resultado.EsValido)
        {
            return Ok(resultado);
        }

        if (resultado.Estado == "NO_ENCONTRADA")
        {
            return NotFound(resultado);
        }

        if (EsCargaYaResuelta(resultado.Estado))
        {
            return Conflict(resultado);
        }

        return BadRequest(resultado);
    }

    private bool ObtenerIdUsuario(out int idUsuario)
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(idUsuarioClaim, out idUsuario);
    }

    private IActionResult TokenSinUsuario()
    {
        return Unauthorized(new {esValido = false, codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
            mensaje = "El token no contiene un id de usuario valido.", traceId = HttpContext.TraceIdentifier });
    }

    private static bool EsCargaYaResuelta(string? estado)
    {
        return string.Equals(
                   estado,
                   "CONFIRMADO",
                   StringComparison.OrdinalIgnoreCase)
               ||
               string.Equals(
                   estado,
                   "CONFIRMADO_ACTUALIZACION",
                   StringComparison.OrdinalIgnoreCase)
               ||
               string.Equals(
                   estado,
                   "RECHAZADO_ADMIN",
                   StringComparison.OrdinalIgnoreCase);
    }

    [HttpGet("{codigoReferencia}/archivos")]
    public async Task<IActionResult> DescargarArchivos(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        try
        {
            var zip = await _administracionService.GenerarZipArchivosPendientesAsync(idUsuario, codigoReferencia);
            return File(zip.Archivo, "application/zip", zip.NombreArchivo);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                esValido = false,
                codigo = "CARGA_NO_ENCONTRADA",
                codigoReferencia,
                mensaje = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                esValido = false,
                codigo = "CARGA_NO_PENDIENTE",
                codigoReferencia,
                mensaje = ex.Message
            });
        }
    }


}
