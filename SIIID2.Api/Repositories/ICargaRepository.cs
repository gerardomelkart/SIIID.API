using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ICargaRepository
{
    // Guarda el intento completo de carga:
    // 1. Inserta el registro principal en carga.
    // 2. Guarda carpetas en staging.
    // 3. Guarda delitos en staging.
    // 4. Guarda víctimas en staging.
    // Todo se ejecuta dentro de una transacción.
    Task<long> GuardarIntentoCargaAsync(int idUsuarioCarga, int? idEntidadFederativa, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError, List<CargaValidacionError> advertencias, List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas);

    // Valida si ya existe una carga confirmada para la misma entidad y periodo.
    Task<bool> ExisteCargaConfirmadaAsync(int idEntidadFederativa, int mesCorte, int anioCorte);

    // Actualiza el estado del intento de carga.
    Task ActualizarEstadoCargaAsync(long idCarga, string estado, string? mensajeError);

    // Confirma o rechaza una carga validada.
    // Si acepta, mueve staging a tablas finales.
    // Si rechaza, solo actualiza estados.
    Task<ConfirmarCargaResponse> ConfirmarCargaAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion);
    // Obtiene el código de una carga pendiente para la misma entidad y periodo.
    // Si regresa null, no existe pendiente.
    Task<string?> ObtenerCodigoCargaPendienteAsync(int idEntidadFederativa, int mesCorte, int anioCorte);

    // Obtiene el código de una actualización pendiente para la misma entidad y periodo.
    // Si regresa null, no existe pendiente.
    Task<string?> ObtenerCodigoActualizacionPendienteAsync(int idEntidadFederativa, int mesCorte, int anioCorte);

    Task<List<ActualizacionAnioDisponibleItem>> ObtenerPeriodosDisponiblesActualizacionAsync(int idEntidadFederativa);

    Task<ConfirmarCargaResponse> GuardarYConfirmarCargaDirectaAsync(int idUsuarioCarga, int idEntidadFederativa, string codigoReferencia, int mesCorte, int anioCorte, List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas);

    Task<ConfirmarCargaResponse> AprobarCargaPendienteAsync(string codigoReferencia, int idUsuarioAprobacion);

    Task<ConfirmarCargaResponse> RechazarCargaPendienteAsync(string codigoReferencia, int idUsuarioRechazo, string motivo);
}