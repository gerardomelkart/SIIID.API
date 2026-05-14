using SIIID2.Api.Models;

namespace SIIID2.Api.Readers;

// Contrato para cualquier lector de archivos de carga.
// Permite cambiar la implementación sin modificar el servicio de carga.
public interface IArchivoReader
{
    // Lee un archivo recibido por la API y lo transforma en una lista de filas genéricas.
    Task<List<ArchivoFila>> LeerAsync(IFormFile archivo);
}
