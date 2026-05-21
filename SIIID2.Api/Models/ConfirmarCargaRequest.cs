namespace SIIID2.Api.Models;

public class ConfirmarCargaRequest
{
    public string CodigoReferencia { get; set; } = string.Empty;
    public bool Aceptar { get; set; }
}