namespace SIIID2.Api.Models;

public class ActualizacionDiferenciasResponse
{
    public bool EsValido { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public List<ActualizacionDiferenciaRegistro> Carpetas { get; set; } = new();
    public List<ActualizacionDiferenciaRegistro> Delitos { get; set; } = new();
    public List<ActualizacionDiferenciaRegistro> Victimas { get; set; } = new();
}