namespace SIIID2.Api.Models;

public class SemanalCargaDiferenciasInfo
{
    public long IdSemanalCarga { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public int IdUsuarioCarga { get; set; }
    public int IdEntidadFederativa { get; set; }
    public string TipoCarga { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public int MesCorte { get; set; }
    public int AnioCorte { get; set; }
    public int AnioSemana { get; set; }
    public int NumeroSemana { get; set; }
    public DateTime FechaInicioSemana { get; set; }
    public DateTime FechaFinSemana { get; set; }
    public List<SemanalFilaComparacion> Carpetas { get; set; } = new();
    public List<SemanalFilaComparacion> Delitos { get; set; } = new();
    public List<SemanalFilaComparacion> Victimas { get; set; } = new();
}