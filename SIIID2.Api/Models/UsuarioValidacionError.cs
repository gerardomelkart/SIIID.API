namespace SIIID2.Api.Models;

public class UsuarioValidacionError
{
    public string Campo { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
}