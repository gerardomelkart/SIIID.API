using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IActualizacionArchivosService
{
    Task<CargaValidacionResponse> ValidarActualizacionAsync(IFormCollection form, int idUsuarioCarga);

    Task<ActualizacionDiferenciasResponse> ObtenerDetalleDiferenciasAsync(string codigoReferencia, int idUsuarioConsulta);

    Task<ConfirmarCargaResponse> ConfirmarActualizacionAsync(ConfirmarCargaRequest request, int idUsuarioConfirmacion);
}