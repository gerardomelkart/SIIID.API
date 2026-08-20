namespace SIIID2.Api.Models;
// Modelo estándar para devolver errores de validación al cliente.
// La idea es que el usuario sepa archivo, fila, columna, valor y motivo del error.
public class CargaValidacionError
{
    // Archivo donde ocurrió el error: carpetas, delitos, víctimas o general.
    public string Archivo { get; set; } = string.Empty;
    // Número de fila donde ocurrió el error. Puede ser null si es un error general.
    public int? Fila { get; set; }
    // Nombre de la columna donde ocurrió el error.
    public string Columna { get; set; } = string.Empty;
    // Campo lógico que se está validando. Normalmente coincide con la columna.
    public string Campo { get; set; } = string.Empty;
    // Valor original recibido en el archivo.
    public string? Valor { get; set; }
    // Código interno del error. Sirve para agrupar errores en el resumen.
    public string Codigo { get; set; } = string.Empty;
    // Descripción corta para mostrar en la tabla resumen.
    public string DescripcionResumen { get; set; } = string.Empty;
    // Mensaje detallado del error.
    public string Mensaje { get; set; } = string.Empty;
    // Cantidad de registros representados por este detalle.
    // Normalmente es 1; las advertencias agrupadas pueden representar varios registros.
    public int TotalRegistrosAfectados { get; set; } = 1;
}