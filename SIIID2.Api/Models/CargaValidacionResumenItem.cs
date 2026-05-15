namespace SIIID2.Api.Models;

public class CargaValidacionResumenItem
{
    // Archivo al que pertenece el resumen: carpetas, delitos, víctimas o general.
    public string Archivo { get; set; } = string.Empty;
    // Código de la validación o del error.
    public string Codigo { get; set; } = string.Empty;
    // Texto que se puede mostrar al usuario.
    public string Descripcion { get; set; } = string.Empty;
    // Total de registros o errores de esta categoría.
    public int TotalRegistros { get; set; }
    // Indica si este renglón representa un error.
    public bool EsError { get; set; }
}