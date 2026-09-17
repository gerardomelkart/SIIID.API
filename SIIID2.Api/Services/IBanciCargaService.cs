using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IBanciCargaService
{
    Task<IReadOnlyList<BanciCargaValidacionResponse>> ObtenerPendientesAsync(int idUsuario);
    Task<BanciCargaValidacionResponse?> ObtenerCargaAsync(string codigoReferencia, int idUsuario);
    Task<BanciCargaValidacionResponse> ConfirmarCargaAsync(string codigoReferencia, bool aceptar, int idUsuario);
    Task<BanciCargaValidacionResponse> ValidarArchivosAsync(BanciCargaArchivosRequest request, int idUsuarioCarga);
}
