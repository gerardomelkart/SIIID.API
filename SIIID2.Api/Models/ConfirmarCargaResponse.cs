namespace SIIID2.Api.Models;

public class ConfirmarCargaResponse
{
    public bool EsValido { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
}