namespace SIIID2.Api.Models;

// Respuesta general del endpoint de validación.
// Si Errores está vacío, la carga se considera válida.
public class CargaValidacionResponse
{
    // La validación es correcta si no hay errores.
    public bool EsValido => Errores.Count == 0;

    // Código generado para identificar este intento de carga.
    public string CodigoReferencia { get; set; } = string.Empty;

    // Total de errores encontrados.
    public int TotalErrores => Errores.Count;

    // Total de advertencias encontradas.
    public int TotalAdvertencias => Advertencias.Sum(x => x.TotalRegistrosAfectados);

    // Mensaje general del resultado de la validación.
    public string Mensaje { get; set; } = string.Empty;

    // Resumen agrupado por tipo de validación.
    public List<CargaValidacionResumenItem> ResumenValidacion { get; set; } = new();

    // Lista detallada de errores por fila, columna y valor.
    public List<CargaValidacionError> Errores { get; set; } = new();

    // Lista detallada de advertencias no bloqueantes.
    public List<CargaValidacionError> Advertencias { get; set; } = new();
}
