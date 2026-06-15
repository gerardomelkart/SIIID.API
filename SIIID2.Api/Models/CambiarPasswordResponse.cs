namespace SIIID2.Api.Models;

public class CambiarPasswordResponse
{
    public bool EsValido { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
}