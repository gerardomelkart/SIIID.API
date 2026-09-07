using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IFederalActualizacionRepository
{
    Task<CargaPendienteInfo?> ObtenerPendienteAsync(int mesCorte, int anioCorte);
    Task<List<ActualizacionAnioDisponibleItem>> ObtenerPeriodosDisponiblesAsync();
    Task<long> GuardarIntentoAsync(int idUsuarioCarga, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError, List<CargaValidacionError> advertencias, List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas);
    Task<List<CargaValidacionResumenItem>> ObtenerResumenDiferenciasAsync(long idFederalCarga);
    Task<ActualizacionDiferenciasResponse?> ObtenerDiferenciasAsync(string codigoReferencia, int idUsuarioConsulta, int limitePorSeccion);
    Task<ConfirmarCargaResponse> ConfirmarAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion);
    Task<ConfirmarCargaResponse> AprobarAsync(string codigoReferencia, int idUsuarioAprobacion);
    Task<ConfirmarCargaResponse> RechazarAsync(string codigoReferencia, int idUsuarioRechazo, string motivo);
}
