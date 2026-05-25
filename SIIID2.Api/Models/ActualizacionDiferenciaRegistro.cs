namespace SIIID2.Api.Models;

public class ActualizacionDiferenciaRegistro
{
    public string TipoMovimiento { get; set; } = string.Empty;
    public string CampoIdentificador { get; set; } = string.Empty;
    public string IdentificadorFiscalia { get; set; } = string.Empty;
    public List<ActualizacionCampoDiferencia> CamposModificados { get; set; } = new();
}