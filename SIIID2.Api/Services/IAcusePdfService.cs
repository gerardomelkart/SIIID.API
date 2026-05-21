namespace SIIID2.Api.Services;

public interface IAcusePdfService
{
    // Genera el PDF del acuse previo.
    Task<byte[]> GenerarAcusePrevioAsync(string codigoReferencia, int idUsuarioConsulta);
}