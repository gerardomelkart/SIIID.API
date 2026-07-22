using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ISemanalAdministracionCargasRepository
{
    Task<List<SemanalCargaPendienteAdministracionItem>> ObtenerPendientesAsync();
    Task<SemanalCargaPendienteAdministracionDetalle?> ObtenerDetalleAsync(string codigoReferencia);
    Task<SemanalCargaReferenciaAdministracionInfo?> ObtenerReferenciaAsync(string codigoReferencia);
    Task<List<IDictionary<string, object?>>> ObtenerCarpetasPendientesAsync(long idSemanalCarga);
    Task<List<IDictionary<string, object?>>> ObtenerDelitosPendientesAsync(long idSemanalCarga);
    Task<List<IDictionary<string, object?>>> ObtenerVictimasPendientesAsync(long idSemanalCarga);
}