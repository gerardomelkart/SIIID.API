namespace SIIID2.Api.Models;

public class ErrorResponse
{
    public bool EsValido { get; set; } = false;
    public string Codigo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string? Detalle { get; set; }
    public string? TraceId { get; set; }
}