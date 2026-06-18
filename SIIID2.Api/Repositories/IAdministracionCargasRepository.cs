using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IAdministracionCargasRepository
{
    Task<List<CargaPendienteAdministracionItem>> ObtenerPendientesAsync();

    Task<CargaPendienteAdministracionDetalle?> ObtenerDetalleAsync(string codigoReferencia);

    Task<CargaReferenciaAdministracionInfo?> ObtenerReferenciaAsync(string codigoReferencia);

    Task<List<IDictionary<string, object?>>> ObtenerCarpetasPendientesAsync(long idCarga);

    Task<List<IDictionary<string, object?>>> ObtenerDelitosPendientesAsync(long idCarga);

    Task<List<IDictionary<string, object?>>> ObtenerVictimasPendientesAsync(long idCarga);
}