using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface ISemanalEnviosService
{
    Task<List<SemanalEnvioItem>> ObtenerEnviosAsync(int idUsuarioConsulta, int? idEntidadFederativa, int? idUsuarioCarga, int? anioCorte, int? mesCorte, string? tipoCarga, string? estado);
    Task<InformeArchivoZipResponse> GenerarZipArchivosAsync(int idUsuarioConsulta, string codigoReferencia);
    Task<InformeArchivoZipResponse> GenerarZipAcusesAsync(int idUsuarioConsulta, int anioCorte, int mesCorte, int? idEntidadFederativa, int? idUsuarioCarga);
    Task<List<SemanalReporteCargaItem>> ObtenerReporteCargasAsync(int idUsuarioConsulta, int? idEntidadFederativa, int? idUsuarioCarga, int? anioCorte, int? mesCorte);
    Task<SemanalReportePreliminarOpcionesResponse> ObtenerOpcionesReportePreliminarAsync(int idUsuarioConsulta, int anioCorte, int mesCorte, int? idEntidadFederativa);
    Task<SemanalReportePreliminarArchivoResponse> GenerarReportePreliminarAsync(int idUsuarioConsulta, int anioCorte, int mesCorte, int idDelito, int? idEntidadFederativa);
}