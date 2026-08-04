namespace SIIID2.Api.Services;

public interface ISemanalAcusePdfService
{
    Task<byte[]> GenerarAcusePrevioAsync(string codigoReferencia, int idUsuarioConsulta, int? anioCorte = null, int? mesCorte = null);
    Task<byte[]> GenerarAcuseConfirmadoAsync(string codigoReferencia, int idUsuarioConsulta, int? anioCorte = null, int? mesCorte = null);
}