namespace SIIID2.Api.Models;

public class FederalArchivosOriginalesResumen
{
    public int IdUsuarioCarga { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string TipoMovimiento { get; set; } = string.Empty;
    public int MesCorte { get; set; }
    public int AnioCorte { get; set; }
    public DateTimeOffset FechaGuardado { get; set; }
    public List<UltimosArchivosEntidadArchivo> Archivos { get; set; } = new();
}
