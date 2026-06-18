using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IAdministracionCargasService
{
    Task<List<CargaPendienteAdministracionItem>> ObtenerPendientesAsync(int idUsuario);

    Task<CargaPendienteAdministracionDetalle?> ObtenerDetalleAsync(int idUsuario, string codigoReferencia);

    Task<ConfirmarCargaResponse> AprobarAsync(int idUsuario, string codigoReferencia);

    Task<ConfirmarCargaResponse> RechazarAsync(int idUsuario, string codigoReferencia, string motivo);

    Task<CargaReferenciaAdministracionInfo?> ObtenerReferenciaAsync(int idUsuario, string codigoReferencia);

    Task<InformeArchivoZipResponse> GenerarZipArchivosPendientesAsync(int idUsuario, string codigoReferencia);
}