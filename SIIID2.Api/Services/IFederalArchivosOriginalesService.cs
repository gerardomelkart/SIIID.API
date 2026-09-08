using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IFederalArchivosOriginalesService
{
    Task GuardarAsync(int idUsuarioCarga, string codigoReferencia, string tipoMovimiento, int mesCorte, int anioCorte, IFormFile archivoCarpetas, IFormFile archivoDelitos, IFormFile archivoVictimas);
    Task<List<FederalArchivosOriginalesResumen>> ObtenerResumenAsync();
    Task<InformeArchivoZipResponse> DescargarAsync();
}
