namespace SIIID2.Api.Models;

public class CargaAcuseResumenItem
{
    public string ClaveDelito { get; set; } = string.Empty;
    public string TipoDelito { get; set; } = string.Empty;
    public string ClaveSubtipo { get; set; } = string.Empty;
    public string SubtipoDelito { get; set; } = string.Empty;
    public int TotalDelitos { get; set; }
    public int TotalVictimas { get; set; }
}