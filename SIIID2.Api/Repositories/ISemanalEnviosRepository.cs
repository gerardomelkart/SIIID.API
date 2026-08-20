using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ISemanalEnviosRepository
{
    Task<List<SemanalEnvioItem>> ObtenerEnviosAsync(bool esSuperUsuario, int idUsuarioConsulta, int? idEntidadFederativa, int? idUsuarioCarga, int? anioCorte, int? mesCorte, string? tipoCarga, string? estado);
    Task<SemanalEnvioReferenciaInfo?> ObtenerReferenciaAsync(string codigoReferencia);
    Task<List<IDictionary<string, object?>>> ObtenerCarpetasConfirmadasSemanaAsync(SemanalEnvioReferenciaInfo referencia);
    Task<List<IDictionary<string, object?>>> ObtenerDelitosConfirmadosSemanaAsync(SemanalEnvioReferenciaInfo referencia);
    Task<List<IDictionary<string, object?>>> ObtenerVictimasConfirmadasSemanaAsync(SemanalEnvioReferenciaInfo referencia);
    Task<List<SemanalReporteCargaItem>> ObtenerReporteCargasAsync(int? idEntidadFederativa, int? idUsuarioCarga, int? anioCorte, int? mesCorte);
    Task<List<SemanalReportePreliminarEntidadItem>> ObtenerEntidadesReportePreliminarAsync(int anioCorte, int mesCorte, string modoReporte, int? idEntidadFederativa, int? idUsuarioCarga);
    Task<List<SemanalReportePreliminarDelitoItem>> ObtenerDelitosReportePreliminarAsync(int anioCorte, int mesCorte, string modoReporte, int? idEntidadFederativa, int? idUsuarioCarga);
    Task<List<IDictionary<string, object?>>> ObtenerCarpetasReportePreliminarAsync(int anioCorte, int mesCorte, int idDelito, string modoReporte, int? idEntidadFederativa, int? idUsuarioCarga);
    Task<List<IDictionary<string, object?>>> ObtenerDelitosReportePreliminarAsync(int anioCorte, int mesCorte, int idDelito, string modoReporte, int? idEntidadFederativa, int? idUsuarioCarga);
    Task<List<IDictionary<string, object?>>> ObtenerVictimasReportePreliminarAsync(int anioCorte, int mesCorte, int idDelito, string modoReporte, int? idEntidadFederativa, int? idUsuarioCarga);
    Task<List<IDictionary<string, object?>>> ObtenerSabanaEstatalDelitosAsync(int anioCorte, int? idEntidadFederativa, int? idUsuarioCarga, string modoPlano, int mesUltimoCorte);
    Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalDelitosAsync(int anioCorte, int? idEntidadFederativa, int? idUsuarioCarga, string modoPlano, int mesUltimoCorte);
    Task<List<IDictionary<string, object?>>> ObtenerSabanaEstatalVictimasAsync(int anioCorte, int? idEntidadFederativa, int? idUsuarioCarga, string modoPlano, int mesUltimoCorte);
    Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalVictimasAsync(int anioCorte, int? idEntidadFederativa, int? idUsuarioCarga, string modoPlano, int mesUltimoCorte);
    Task<InformeSabanaFirma> ObtenerFirmaSabanaAsync(int anioCorte, int? idEntidadFederativa, int? idUsuarioCarga);
}