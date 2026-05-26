namespace SIIID2.Api.Services;

public interface IAcusePdfService
{
    // Genera el PDF del acuse previo.
    Task<byte[]> GenerarAcusePrevioAsync(string codigoReferencia, int idUsuarioConsulta);

    // Genera el PDF del acuse confirmado.
    Task<byte[]> GenerarAcuseConfirmadoAsync(string codigoReferencia, int idUsuarioConsulta);

    // Genera el PDF del acuse previo de actualización.
    Task<byte[]> GenerarAcusePrevioActualizacionAsync(string codigoReferencia, int idUsuarioConsulta);

    // Genera el PDF del acuse confirmado de actualización.
    Task<byte[]> GenerarAcuseConfirmadoActualizacionAsync(string codigoReferencia, int idUsuarioConsulta);
}