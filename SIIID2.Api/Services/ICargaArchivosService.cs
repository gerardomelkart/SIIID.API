using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

// Contrato del servicio principal de carga de archivos.
// El controller depende de esta interfaz, no de la clase concreta.
public interface ICargaArchivosService
{
    // Valida los archivos recibidos y registra el intento de carga.
    // El idUsuarioCarga viene del Bearer Token, no del form-data.
    Task<CargaValidacionResponse> ValidarArchivosAsync(IFormCollection form, int idUsuarioCarga);
}