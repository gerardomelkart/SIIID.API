namespace SIIID2.Api.Models;
// Representa una fila leída desde un archivo CSV o Excel.
// Se usa un diccionario para poder manejar columnas dinámicas por nombre.
public class ArchivoFila
{
    // Número real de fila en el archivo. Sirve para reportar errores claros al usuario.
    public int? NumeroFila { get; set; }
    // Columnas de la fila: nombre normalizado de columna -> valor leído.
    // StringComparer.OrdinalIgnoreCase permite buscar columnas sin importar mayúsculas/minúsculas.
    public Dictionary<string, string?> Columnas { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
