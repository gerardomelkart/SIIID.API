namespace SIIID2.Api.Models;

// Modelo estándar para devolver errores de validación al cliente.
// La idea es que el usuario sepa archivo, fila, columna, valor y motivo del error.
public class CargaValidacionError
{
    // Archivo lógico donde ocurrió el error: carpetas, delitos, victimas o general.
    public string Archivo { get; set; } = string.Empty;
    // Fila donde ocurrió el error. Puede ser null si el error es general del archivo.
    public int? Fila { get; set; }
    // Nombre de la columna involucrada.
    public string Columna { get; set; } = string.Empty;
    // Campo que se está validando. Por ahora suele coincidir con Columna.
    public string Campo { get; set; } = string.Empty;
    // Valor recibido en el archivo. Puede ser null si la columna/campo no existe.
    public string? Valor { get; set; }
    // Mensaje legible para indicar qué debe corregirse.
    public string Mensaje { get; set; } = string.Empty;
}
