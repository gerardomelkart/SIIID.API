using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IBanciCargaRepository
{
    Task<IReadOnlyList<BanciCargaValidacionResponse>> ObtenerPendientesAsync(int idUsuario);
    Task<BanciCargaValidacionResponse?> ObtenerCargaAsync(string codigoReferencia, int idUsuario);
    Task<BanciCargaValidacionResponse> ConfirmarCargaAsync(string codigoReferencia, bool aceptar, int idUsuario);

    Task<BanciUsuarioCargaInfo?> ObtenerUsuarioCargaAsync(int idUsuario);

    Task<int?> ResolverEntidadFederativaAsync(string valor);

    Task<long> GuardarCargaValidadaAsync(
        int idUsuarioCarga,
        int idEntidadFederativa,
        string codigoReferencia,
        string modalidadIngreso,
        BanciLecturaArchivosResultado lectura,
        BanciCargaValidacionResponse validacion);
}
