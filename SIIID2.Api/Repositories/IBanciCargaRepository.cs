using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IBanciCargaRepository
{
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