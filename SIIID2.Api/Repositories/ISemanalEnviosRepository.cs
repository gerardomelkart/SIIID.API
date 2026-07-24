using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ISemanalEnviosRepository
{
    Task<List<SemanalEnvioItem>> ObtenerEnviosAsync(bool esSuperUsuario, int? idEntidadFederativaUsuario, int? idEntidadFederativa, int? anioSemana, int? numeroSemana, string? tipoCarga, string? estado);
    Task<SemanalEnvioReferenciaInfo?> ObtenerReferenciaAsync(string codigoReferencia);
    Task<List<IDictionary<string, object?>>> ObtenerCarpetasConfirmadasSemanaAsync(SemanalEnvioReferenciaInfo referencia);
    Task<List<IDictionary<string, object?>>> ObtenerDelitosConfirmadosSemanaAsync(SemanalEnvioReferenciaInfo referencia);
    Task<List<IDictionary<string, object?>>> ObtenerVictimasConfirmadasSemanaAsync(SemanalEnvioReferenciaInfo referencia);
    Task<List<SemanalReporteCargaItem>> ObtenerReporteCargasAsync(int? idEntidadFederativa, int? anioSemana, int? numeroSemana);
    Task<bool> ExisteInformacionConfirmadaPlanoAsync(int anioCorte, int mesCorte, int? idEntidadFederativa);
    Task<List<IDictionary<string, object?>>> ObtenerPlanoEstatalDelitosAsync(int anioCorte, int mesCorte, int? idEntidadFederativa);
    Task<List<IDictionary<string, object?>>> ObtenerPlanoMunicipalDelitosAsync(int anioCorte, int mesCorte, int? idEntidadFederativa);
    Task<List<IDictionary<string, object?>>> ObtenerPlanoEstatalVictimasAsync(int anioCorte, int mesCorte, int? idEntidadFederativa);
    Task<List<IDictionary<string, object?>>> ObtenerPlanoMunicipalVictimasAsync(int anioCorte, int mesCorte, int? idEntidadFederativa);
}