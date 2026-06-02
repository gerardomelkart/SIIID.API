using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IInformeService
{
    Task<List<InformeEnvioItem>> ObtenerEnviosAsync(int idUsuarioConsulta, int? idEntidadFederativa, int? mesCorte, int? anioCorte);

    Task<InformeArchivoZipResponse> GenerarZipArchivosEnvioAsync(string codigoReferencia, int idUsuarioConsulta);

    Task<List<InformeReporteCargaItem>> ObtenerReporteCargasAsync(int idUsuarioConsulta, int? idEntidadFederativa, int mesCorte, int anioCorte);
}