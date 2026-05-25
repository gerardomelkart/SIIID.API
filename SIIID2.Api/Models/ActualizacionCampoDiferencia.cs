namespace SIIID2.Api.Models;

public class ActualizacionCampoDiferencia
{
    public string Campo { get; set; } = string.Empty;
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
}