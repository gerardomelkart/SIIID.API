namespace SIIID2.Api.Models;

public class SemanalReporteCargaItem
{
    public int IdEntidadFederativa { get; set; }
    public string EntidadFederativa { get; set; } = string.Empty;
    public string ClaveEntidad { get; set; } = string.Empty;
    public int IdUsuarioCarga { get; set; }
    public string UsuarioCarga { get; set; } = string.Empty;
    public string NombreUsuarioCarga { get; set; } = string.Empty;
    public int AnioCorte { get; set; }
    public int MesCorte { get; set; }
    public string Periodo { get; set; } = string.Empty;
    public int Intentos { get; set; }
    public string? UltimoIntento { get; set; }
    public string? TipoCargaUltimoIntento { get; set; }
    public string? EstatusUltimoIntento { get; set; }
    public DateTime? FechaCargaActualizacion { get; set; }
    public string FechaCargaActualizacionTexto { get; set; } = string.Empty;
    public DateTime? FechaAprobacion { get; set; }
    public string FechaAprobacionTexto { get; set; } = string.Empty;
    public DateTime? FechaCargaExitosa { get; set; }
}