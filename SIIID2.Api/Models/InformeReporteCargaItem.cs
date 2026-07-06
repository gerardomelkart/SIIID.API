namespace SIIID2.Api.Models;

public class InformeReporteCargaItem
{
    public int IdEntidadFederativa { get; set; }

    public string EntidadFederativa { get; set; } = string.Empty;

    public string ClaveEntidad { get; set; } = string.Empty;

    public int MesCorte { get; set; }

    public int AnioCorte { get; set; }

    public string Corte { get; set; } = string.Empty;

    public int Intentos { get; set; }

    public string? UltimoIntento { get; set; }

    public string? TipoCargaUltimoIntento { get; set; }

    public string? EstatusUltimoIntento { get; set; }

    public DateTime? FechaUltimaCarga { get; set; }

    public string FechaUltimaCargaTexto { get; set; } = string.Empty;

    public DateTime? FechaCargaExitosa { get; set; }
}