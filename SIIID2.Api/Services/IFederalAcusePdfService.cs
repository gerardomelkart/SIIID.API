namespace SIIID2.Api.Services;

public interface IFederalAcusePdfService
{
    Task<byte[]> GenerarAcusePrevioAsync(string codigoReferencia, int idUsuarioConsulta);
    Task<byte[]> GenerarAcuseConfirmadoAsync(string codigoReferencia, int idUsuarioConsulta);
    Task<byte[]> GenerarAcusePrevioActualizacionAsync(string codigoReferencia, int idUsuarioConsulta);
    Task<byte[]> GenerarAcuseConfirmadoActualizacionAsync(string codigoReferencia, int idUsuarioConsulta);
}