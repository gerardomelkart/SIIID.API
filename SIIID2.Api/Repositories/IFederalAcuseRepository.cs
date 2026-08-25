using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IFederalAcuseRepository
{
    Task<CargaAcuseInfo?> ObtenerCargaParaAcuseAsync(string codigoReferencia);
    Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseAsync(long idFederalCarga);
    Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseConfirmadoAsync(long idFederalCarga);
}
