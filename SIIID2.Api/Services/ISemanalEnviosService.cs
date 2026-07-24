using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface ISemanalEnviosService
{
    Task<List<SemanalEnvioItem>> ObtenerEnviosAsync(int idUsuarioConsulta, int? idEntidadFederativa, int? anioSemana, int? numeroSemana, string? tipoCarga, string? estado);
    Task<InformeArchivoZipResponse> GenerarZipArchivosAsync(int idUsuarioConsulta, string codigoReferencia);
    Task<InformeArchivoZipResponse> GenerarZipAcusesAsync(int idUsuarioConsulta, int anioSemana, int numeroSemana);
    Task<List<SemanalReporteCargaItem>> ObtenerReporteCargasAsync(int idUsuarioConsulta, int? idEntidadFederativa, int? anioSemana, int? numeroSemana);
    Task<InformeArchivoZipResponse> GenerarZipPlanosAsync(int idUsuarioConsulta, int anioCorte, int mesCorte, string? tipoPlano);
}