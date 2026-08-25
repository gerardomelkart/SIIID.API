using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using SIIID2.Api.Services;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize(Policy = "MODULO_FEDERAL")]
[Route("api/federal/cargas")]
public class FederalCargasController : ControllerBase
{
    private readonly IFederalCargaArchivosService _federalCargaArchivosService;
    private readonly IFederalCargaRepository _federalCargaRepository;

    public FederalCargasController(IFederalCargaArchivosService federalCargaArchivosService, IFederalCargaRepository federalCargaRepository)
    {
        _federalCargaArchivosService = federalCargaArchivosService;
        _federalCargaRepository = federalCargaRepository;
    }

    [HttpPost("validar")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ValidarArchivos()
    {
        if (!Request.HasFormContentType)
        {
            return BadRequest(new CargaValidacionResponse
            {
                Mensaje = "La petición debe enviarse como multipart/form-data.",
                Errores =
                [
                    new CargaValidacionError
                    {
                        Archivo = "general",
                        Codigo = "FEDERAL_GENERAL_CONTENT_TYPE_INVALIDO",
                        DescripcionResumen = "Tipo de petición inválido",
                        Mensaje = "La petición debe enviarse como multipart/form-data."
                    }
                ]
            });
        }

        if (!TryObtenerIdUsuario(out var idUsuarioCarga)) return TokenInvalido();

        var resultado = await _federalCargaArchivosService.ValidarArchivosAsync(Request.Form, idUsuarioCarga);
        return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
    }

    [HttpPost("confirmar")]
    public async Task<IActionResult> ConfirmarCarga([FromBody] ConfirmarCargaRequest request)
    {
        if (!TryObtenerIdUsuario(out var idUsuarioConfirmacion)) return TokenInvalido();

        if (string.IsNullOrWhiteSpace(request.CodigoReferencia))
        {
            return BadRequest(new ConfirmarCargaResponse
            {
                EsValido = false,
                Estado = "SOLICITUD_INVALIDA",
                Mensaje = "Debe enviar el código de referencia federal."
            });
        }

        var resultado = await _federalCargaRepository.ConfirmarCargaAsync(request.CodigoReferencia.Trim(), request.Aceptar, idUsuarioConfirmacion);
        return resultado.EsValido ? Ok(resultado) : BadRequest(resultado);
    }

    private bool TryObtenerIdUsuario(out int idUsuario)
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idUsuario);
    }

    private ObjectResult TokenInvalido()
    {
        return Unauthorized(new
        {
            esValido = false,
            codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
            mensaje = "El token no contiene un id de usuario válido.",
            traceId = HttpContext.TraceIdentifier
        });
    }
}
