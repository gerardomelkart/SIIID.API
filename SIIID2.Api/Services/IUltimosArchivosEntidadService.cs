namespace SIIID2.Api.Services;

public interface IUltimosArchivosEntidadService
{
    Task GuardarAsync(int idEntidadFederativa, string codigoReferencia, string tipoMovimiento, int mesCorte, int anioCorte, IFormFile archivoCarpetas, IFormFile archivoDelitos, IFormFile archivoVictimas);
}