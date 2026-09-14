using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IBanciCargaService
{
    Task<BanciCargaValidacionResponse> ValidarArchivosAsync(
        BanciCargaArchivosRequest request,
        int idUsuarioCarga);
}