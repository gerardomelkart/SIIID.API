using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IActualizacionCargaRepository
{
    // Guarda el intento completo de actualización.
    // Usa tipo_carga = ACTUALIZACION.
    Task<long> GuardarIntentoActualizacionAsync(int idUsuarioCarga, int? idEntidadFederativa, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError, List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas);

    // Obtiene el resumen de diferencias de una actualización validada.
    // Se usa en la respuesta de validación antes de confirmar.
    Task<List<CargaValidacionResumenItem>> ObtenerResumenDiferenciasActualizacionAsync(long idCargaActualizacion);
}