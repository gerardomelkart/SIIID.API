namespace SIIID2.Api.Models;

public class CrearUsuarioResponse
{
    public bool EsValido { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public int? IdUsuario { get; set; }
}