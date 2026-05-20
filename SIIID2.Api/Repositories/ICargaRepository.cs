using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ICargaRepository
{
    // Crea el registro principal del intento de carga.
    Task<long> CrearCargaAsync(int idUsuarioCarga, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError);

    // Guarda las filas leídas de carpetas en staging.
    Task GuardarTmpCarpetasAsync(long idCarga, List<ArchivoFila> filasCarpetas);

    // Guarda las filas leídas de delitos en staging.
    Task GuardarTmpDelitosAsync(long idCarga, List<ArchivoFila> filasDelitos);

    // Guarda las filas leídas de víctimas en staging.
    Task GuardarTmpVictimasAsync(long idCarga, List<ArchivoFila> filasVictimas);

    // Actualiza el estado del intento de carga.
    Task ActualizarEstadoCargaAsync(long idCarga, string estado, string? mensajeError);
}