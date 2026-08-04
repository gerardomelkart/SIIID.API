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
    Task<List<SemanalReportePreliminarUsuarioItem>> ObtenerUsuariosReportePreliminarAsync(int anioCorte, int mesCorte, int? idUsuarioCarga);
    Task<List<SemanalReportePreliminarDelitoItem>> ObtenerDelitosReportePreliminarAsync(int anioCorte, int mesCorte, int? idUsuarioCarga);
    Task<List<IDictionary<string, object?>>> ObtenerCarpetasReportePreliminarAsync(int anioCorte, int mesCorte, int idDelito, int? idUsuarioCarga);
    Task<List<IDictionary<string, object?>>> ObtenerDelitosReportePreliminarAsync(int anioCorte, int mesCorte, int idDelito, int? idUsuarioCarga);
    Task<List<IDictionary<string, object?>>> ObtenerVictimasReportePreliminarAsync(int anioCorte, int mesCorte, int idDelito, int? idUsuarioCarga);
}