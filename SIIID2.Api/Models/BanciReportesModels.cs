namespace SIIID2.Api.Models;

public sealed class BanciReporteCarga
{
    public int Entidad { get; set; }
    public string EntidadNombre { get; set; } = "";
    public int Intentos { get; set; }
    public string Estado { get; set; } = "";
    public DateTime? FechaCarga { get; set; }
    public DateTime? FechaAprobacion { get; set; }
    public DateTime? FechaCargaExitosa { get; set; }
    public int? Ranking { get; set; }
}

public sealed class BanciArchivoOriginal
{
    public Guid Referencia { get; set; }
    public int Entidad { get; set; }
    public string EntidadNombre { get; set; } = "";
    public int Mes { get; set; }
    public int Anio { get; set; }
    public string Tipo { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string NombreOriginal { get; set; } = "";
    public long TamanioBytes { get; set; }
    public string Sha256 { get; set; } = "";
}
