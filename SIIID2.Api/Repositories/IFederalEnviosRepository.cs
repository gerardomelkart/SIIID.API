using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IFederalEnviosRepository
{
    Task<List<InformePeriodoItem>> ObtenerPeriodosAsync();
    Task<List<InformeEnvioItem>> ObtenerEnviosAsync(int? mesCorte, int? anioCorte);
    Task<List<InformeReporteCargaItem>> ObtenerReporteCargasAsync(int? mesCorte, int? anioCorte);
    Task<InformeArchivoCargaInfo?> ObtenerCargaParaArchivosAsync(string codigoReferencia);
    Task<List<IDictionary<string, object?>>> ObtenerCarpetasConfirmadasPeriodoAsync(int mesCorte, int anioCorte);
    Task<List<IDictionary<string, object?>>> ObtenerDelitosConfirmadosPeriodoAsync(int mesCorte, int anioCorte);
    Task<List<IDictionary<string, object?>>> ObtenerVictimasConfirmadasPeriodoAsync(int mesCorte, int anioCorte);
    Task<List<IDictionary<string, object?>>> ObtenerCarpetasStagingAsync(long idFederalCarga);
    Task<List<IDictionary<string, object?>>> ObtenerDelitosStagingAsync(long idFederalCarga);
    Task<List<IDictionary<string, object?>>> ObtenerVictimasStagingAsync(long idFederalCarga);

    Task<List<IDictionary<string, object?>>> ObtenerSabanaEstatalDelitosAsync(int anioCorte, string modoPlano, int mesUltimoCorte);
    Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalDelitosAsync(int anioCorte, string modoPlano, int mesUltimoCorte);
    Task<List<IDictionary<string, object?>>> ObtenerSabanaEstatalVictimasAsync(int anioCorte, string modoPlano, int mesUltimoCorte);
    Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalVictimasAsync(int anioCorte, string modoPlano, int mesUltimoCorte);
    Task<InformeSabanaFirma> ObtenerFirmaSabanaAsync(int anioCorte);
}
