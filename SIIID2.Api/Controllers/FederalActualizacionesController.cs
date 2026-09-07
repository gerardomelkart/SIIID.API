using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_FEDERAL")]
[Route("api/federal/actualizaciones")]
public class FederalActualizacionesController : ControllerBase
{
    private readonly IFederalActualizacionArchivosService _service;
    private readonly IFederalAcusePdfService _acusePdfService;

    public FederalActualizacionesController(IFederalActualizacionArchivosService service, IFederalAcusePdfService acusePdfService)
    {
        _service = service;
        _acusePdfService = acusePdfService;
    }

    [HttpPost("validar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Validar()
    {
        if (!Request.HasFormContentType) return BadRequest(new CargaValidacionResponse { Mensaje = "La petición debe enviarse como multipart/form-data.", Errores = [new CargaValidacionError { Archivo = "general", Codigo = "FEDERAL_ACTUALIZACION_CONTENT_TYPE_INVALIDO", DescripcionResumen = "Tipo de petición inválido", Mensaje = "La petición debe enviarse como multipart/form-data." }] });
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _service.ValidarActualizacionAsync(Request.Form, idUsuario);
        return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpGet("diferencias/{codigoReferencia}")]
    public async Task<IActionResult> ObtenerDiferencias(string codigoReferencia, [FromQuery] int limitePorSeccion = 100, [FromQuery] bool incluirResumen = true)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _service.ObtenerDiferenciasAsync(codigoReferencia.Trim(), idUsuario, Math.Clamp(limitePorSeccion, 0, 200), incluirResumen);
        return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost("confirmar")]
    public async Task<IActionResult> Confirmar([FromBody] ConfirmarCargaRequest request)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _service.ConfirmarActualizacionAsync(request, idUsuario);
        return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpGet("periodo")]
    public async Task<IActionResult> ConsultarPeriodo([FromQuery] int mesCorte, [FromQuery] int anioCorte)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        var resultado = await _service.ConsultarPeriodoAsync(mesCorte, anioCorte, idUsuario);
        return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpGet("periodos-disponibles")]
    public async Task<IActionResult> ObtenerPeriodosDisponibles()
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();
        return Ok(await _service.ObtenerPeriodosDisponiblesAsync(idUsuario));
    }

    [HttpGet("{codigoReferencia}/acuse")]
    public async Task<IActionResult> DescargarAcusePrevio(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        try
        {
            var pdf = await _acusePdfService.GenerarAcusePrevioActualizacionAsync(codigoReferencia.Trim(), idUsuario);
            return File(pdf, "application/pdf", $"INFORME_PREVIO_ACTUALIZACION_FEDERAL_{codigoReferencia.Trim()}.pdf");
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { esValido = false, codigo = "FEDERAL_ACUSE_SIN_PERMISO", mensaje = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { esValido = false, codigo = "FEDERAL_ACUSE_NO_DISPONIBLE", mensaje = ex.Message }); }
    }

    [HttpGet("{codigoReferencia}/acuse-confirmado")]
    public async Task<IActionResult> DescargarAcuseConfirmado(string codigoReferencia)
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        try
        {
            var pdf = await _acusePdfService.GenerarAcuseConfirmadoActualizacionAsync(codigoReferencia.Trim(), idUsuario);
            return File(pdf, "application/pdf", $"ACUSE_ACTUALIZACION_FEDERAL_{codigoReferencia.Trim()}.pdf");
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { esValido = false, codigo = "FEDERAL_ACUSE_SIN_PERMISO", mensaje = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { esValido = false, codigo = "FEDERAL_ACUSE_NO_DISPONIBLE", mensaje = ex.Message }); }
    }

    private bool ObtenerIdUsuario(out int idUsuario) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idUsuario);

    private IActionResult TokenSinUsuario() => Unauthorized(new { esValido = false, codigo = "GENERAL_TOKEN_SIN_ID_USUARIO", mensaje = "El token no contiene un id de usuario válido.", traceId = HttpContext.TraceIdentifier });
}
