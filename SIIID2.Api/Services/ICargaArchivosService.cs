using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

// Contrato del servicio principal de carga de archivos.
// El controller depende de esta interfaz, no de la clase concreta.
public interface ICargaArchivosService
{
    // Valida los archivos recibidos y devuelve todos los errores encontrados.
    Task<CargaValidacionResponse> ValidarArchivosAsync(IFormFileCollection archivos);
}
