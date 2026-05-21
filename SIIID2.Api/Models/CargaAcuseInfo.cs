namespace SIIID2.Api.Models;

public class CargaAcuseInfo
{
    public long IdCarga { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public int? IdEntidadFederativa { get; set; }
    public string EntidadFederativa { get; set; } = string.Empty;
    public int MesCorte { get; set; }
    public int AnioCorte { get; set; }
    public int TotalCarpetasInvestigacion { get; set; }
    public int TotalDelitos { get; set; }
    public int TotalVictimas { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateTime FechaValidacion { get; set; }
    public int IdUsuarioCarga { get; set; }
    public string UsuarioCarga { get; set; } = string.Empty;
}