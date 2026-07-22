using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface ISemanalAdministracionCargasService
{
    Task<List<SemanalCargaPendienteAdministracionItem>> ObtenerPendientesAsync(int idUsuario);
    Task<SemanalCargaPendienteAdministracionDetalle?> ObtenerDetalleAsync(int idUsuario, string codigoReferencia);
    Task<SemanalCargaReferenciaAdministracionInfo?> ObtenerReferenciaAsync(int idUsuario, string codigoReferencia);
    Task<ConfirmarCargaResponse> AprobarAsync(int idUsuario, string codigoReferencia);
    Task<ConfirmarCargaResponse> RechazarAsync(int idUsuario, string codigoReferencia, string motivo);
    Task<InformeArchivoZipResponse> GenerarZipArchivosPendientesAsync(int idUsuario, string codigoReferencia);
}