using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface ISemanalCargaService
{
    Task<SemanalCargaValidacionResponse> ValidarArchivosAsync(SemanalCargaValidacionRequest request, int idUsuarioCarga);
    Task<ConfirmarCargaResponse> ConfirmarCargaAsync(ConfirmarCargaRequest request, int idUsuarioConfirmacion);
}
