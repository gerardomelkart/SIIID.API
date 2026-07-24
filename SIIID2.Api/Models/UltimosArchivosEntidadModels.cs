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

public class UltimosArchivosEntidadSemanalMetadata
{
    public int IdEntidadFederativa { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string TipoMovimiento { get; set; } = string.Empty;
    public string TipoContenido { get; set; } = string.Empty;
    public int AnioSemana { get; set; }
    public int NumeroSemana { get; set; }
    public DateTime FechaInicioSemana { get; set; }
    public DateTime FechaFinSemana { get; set; }
    public DateTime FechaInicioTramo { get; set; }
    public DateTime FechaFinTramo { get; set; }
    public int MesCorte { get; set; }
    public int AnioCorte { get; set; }
    public DateTimeOffset FechaGuardado { get; set; }
    public List<UltimosArchivosEntidadArchivo> Archivos { get; set; } = new();
}

public class UltimosArchivosEntidadSemanalResumen
{
    public int IdEntidadFederativa { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string TipoMovimiento { get; set; } = string.Empty;
    public string TipoContenido { get; set; } = string.Empty;
    public int AnioSemana { get; set; }
    public int NumeroSemana { get; set; }
    public DateTime FechaInicioSemana { get; set; }
    public DateTime FechaFinSemana { get; set; }
    public DateTime FechaInicioTramo { get; set; }
    public DateTime FechaFinTramo { get; set; }
    public int MesCorte { get; set; }
    public int AnioCorte { get; set; }
    public DateTimeOffset FechaGuardado { get; set; }
    public List<UltimosArchivosEntidadArchivo> Archivos { get; set; } = new();
}