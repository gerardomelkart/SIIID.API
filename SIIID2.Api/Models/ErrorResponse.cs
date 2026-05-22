namespace SIIID2.Api.Models;

public class ErrorResponse
{
    public bool EsValido { get; set; } = false;
    public string Codigo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string? Detalle { get; set; }
    //identificador único de la petición. Sirve para rastrear el error en logs.
    public string? TraceId { get; set; }
}