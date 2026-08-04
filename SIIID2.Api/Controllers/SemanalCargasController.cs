using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/semanal/cargas")]
public class SemanalCargasController : ControllerBase
{
    private readonly ISemanalCargaService _semanalCargaService;
    private readonly ISemanalAcusePdfService _semanalAcusePdfService;

    public SemanalCargasController(ISemanalCargaService semanalCargaService, ISemanalAcusePdfService semanalAcusePdfService)
    {
        _semanalCargaService = semanalCargaService;
        _semanalAcusePdfService = semanalAcusePdfService;
    }

    [HttpGet("semana/disponibilidad")]
    public async Task<IActionResult> ValidarSemana([FromQuery] string tipoCarga, [FromQuery] int anioSemana, [FromQuery] int numeroSemana, [FromQuery] int? idEntidadFederativa)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _semanalCargaService.ValidarSemanaAsync(tipoCarga, anioSemana, numeroSemana, idEntidadFederativa, idUsuario);
        return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost("validar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ValidarArchivos([FromForm] SemanalCargaValidacionRequest request)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        var resultado =
            await _semanalCargaService.ValidarArchivosAsync(
                request,
                idUsuario);

        return resultado.EsValido
            ? Ok(resultado)
            : BadRequest(resultado);
    }

    [HttpGet("{codigoReferencia}/diferencias")]
    public async Task<IActionResult> ObtenerDiferencias(
    string codigoReferencia,
    [FromQuery] int limitePorSeccion = 100)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        limitePorSeccion = Math.Clamp(
            limitePorSeccion,
            1,
            200);

        var resultado =
            await _semanalCargaService.ObtenerDiferenciasAsync(
                codigoReferencia,
                idUsuario,
                limitePorSeccion);

        return resultado.EsValido
            ? Ok(resultado)
            : BadRequest(resultado);
    }

    [HttpGet("{codigoReferencia}/acuse")]
    public async Task<IActionResult> DescargarAcuse(string codigoReferencia, [FromQuery] int? anioCorte, [FromQuery] int? mesCorte)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        try
        {
            var pdf = await _semanalAcusePdfService.GenerarAcusePrevioAsync(codigoReferencia, idUsuario, anioCorte, mesCorte);

            return File(
                pdf,
                "application/pdf",
                $"INFORME_PREVIO_SEMANAL_{codigoReferencia}.pdf");
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    esValido = false,
                    codigo = "ACUSE_SEMANAL_SIN_PERMISO",
                    mensaje = ex.Message
                });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(
                new
                {
                    esValido = false,
                    codigo = "ACUSE_SEMANAL_NO_DISPONIBLE",
                    mensaje = ex.Message
                });
        }
    }

    [HttpPost("confirmar")]
    public async Task<IActionResult> ConfirmarCarga([FromBody] ConfirmarCargaRequest request)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        if (string.IsNullOrWhiteSpace(
                request.CodigoReferencia))
        {
            return BadRequest(
                new ConfirmarCargaResponse
                {
                    EsValido = false,
                    Estado = "SOLICITUD_INVALIDA",
                    Mensaje = "Debe enviar el código de referencia."
                });
        }

        var resultado =
            await _semanalCargaService.ConfirmarCargaAsync(request, idUsuario);

        return resultado.EsValido
            ? Ok(resultado)
            : BadRequest(resultado);
    }

    [HttpGet("{codigoReferencia}/acuse-confirmado")]
    public async Task<IActionResult> DescargarAcuseConfirmado(string codigoReferencia, [FromQuery] int? anioCorte, [FromQuery] int? mesCorte)
    {
        if (!ObtenerIdUsuario(out var idUsuario))
        {
            return TokenSinUsuario();
        }

        try
        {
            var pdf = await _semanalAcusePdfService.GenerarAcuseConfirmadoAsync(codigoReferencia, idUsuario, anioCorte, mesCorte);

            return File(
                pdf,
                "application/pdf",
                $"ACUSE_SEMANAL_{codigoReferencia}.pdf");
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    esValido = false,
                    codigo = "ACUSE_SEMANAL_SIN_PERMISO",
                    mensaje = ex.Message
                });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(
                new
                {
                    esValido = false,
                    codigo = "ACUSE_SEMANAL_NO_DISPONIBLE",
                    mensaje = ex.Message
                });
        }
    }

    private bool ObtenerIdUsuario(out int idUsuario)
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),
            out idUsuario);
    }

    private IActionResult TokenSinUsuario()
    {
        return Unauthorized(
            new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
    }
}