using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IFederalEnviosRepository
{
    Task<List<InformePeriodoItem>> ObtenerPeriodosAsync();
    Task<List<InformeEnvioItem>> ObtenerEnviosAsync(int? mesCorte, int? anioCorte);
    Task<InformeArchivoCargaInfo?> ObtenerCargaParaArchivosAsync(string codigoReferencia);
    Task<List<IDictionary<string, object?>>> ObtenerCarpetasConfirmadasPeriodoAsync(int mesCorte, int anioCorte);
    Task<List<IDictionary<string, object?>>> ObtenerDelitosConfirmadosPeriodoAsync(int mesCorte, int anioCorte);
    Task<List<IDictionary<string, object?>>> ObtenerVictimasConfirmadasPeriodoAsync(int mesCorte, int anioCorte);
    Task<List<IDictionary<string, object?>>> ObtenerCarpetasStagingAsync(long idFederalCarga);
    Task<List<IDictionary<string, object?>>> ObtenerDelitosStagingAsync(long idFederalCarga);
    Task<List<IDictionary<string, object?>>> ObtenerVictimasStagingAsync(long idFederalCarga);
}