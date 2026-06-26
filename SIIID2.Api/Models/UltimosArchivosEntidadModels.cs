namespace SIIID2.Api.Models;

public class UltimosArchivosEntidadArchivo
{
    public string Tipo { get; set; } = string.Empty;
    public string NombreOriginal { get; set; } = string.Empty;
    public string RutaRelativa { get; set; } = string.Empty;
    public long TamanioBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public class UltimosArchivosEntidadMetadata
{
    public int IdEntidadFederativa { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string TipoMovimiento { get; set; } = string.Empty;
    public int MesCorte { get; set; }
    public int AnioCorte { get; set; }
    public DateTimeOffset FechaGuardado { get; set; }
    public List<UltimosArchivosEntidadArchivo> Archivos { get; set; } = new();
}

public class UltimosArchivosEntidadResumen
{
    public int IdEntidadFederativa { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string TipoMovimiento { get; set; } = string.Empty;
    public int MesCorte { get; set; }
    public int AnioCorte { get; set; }
    public DateTimeOffset FechaGuardado { get; set; }
    public List<UltimosArchivosEntidadArchivo> Archivos { get; set; } = new();
}