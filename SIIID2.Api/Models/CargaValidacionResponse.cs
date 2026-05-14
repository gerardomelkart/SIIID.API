namespace SIIID2.Api.Models;

// Respuesta general del endpoint de validación.
// Si Errores está vacío, la carga se considera válida.
public class CargaValidacionResponse
{
    // Lista acumulada de errores detectados en todos los archivos.
    public List<CargaValidacionError> Errores { get; set; } = new();
    // Propiedad calculada: true cuando no hay errores.
    public bool EsValido => Errores.Count == 0;
    // Propiedad calculada para facilitar la lectura desde front o Postman.
    public int TotalErrores => Errores.Count;
    // Mensaje general de la validación.
    public string Mensaje { get; set; } = string.Empty;
}
