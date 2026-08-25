using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IFederalCargaArchivosService
{
    Task<CargaValidacionResponse> ValidarArchivosAsync(IFormCollection form, int idUsuarioCarga);
}
