namespace SIIID2.Api.Models;

public class SemanalCargaAcuseBloque
{
    public int AnioSemana { get; set; }
    public int NumeroSemana { get; set; }
    public DateTime FechaInicioSemana { get; set; }
    public DateTime FechaFinSemana { get; set; }
    public int AnioCorte { get; set; }
    public int MesCorte { get; set; }
    public DateTime FechaInicioTramo { get; set; }
    public DateTime FechaFinTramo { get; set; }
    public DateTime? FechaFinInformacion { get; set; }
    public int TotalCarpetas { get; set; }
    public int TotalDelitos { get; set; }
    public int TotalVictimas { get; set; }
    public bool ReemplazaInformacion { get; set; }
}

public class SemanalCargaAcuseInfo
{
    public long IdSemanalCarga { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public int? IdEntidadFederativa { get; set; }
    public string EntidadFederativa { get; set; } = string.Empty;
    public string TipoCarga { get; set; } = string.Empty;
    public string TipoContenido { get; set; } = string.Empty;
    public int AnioSemana { get; set; }
    public int NumeroSemana { get; set; }
    public DateTime FechaInicioSemana { get; set; }
    public DateTime FechaFinSemana { get; set; }
    public DateTime FechaInicioTramo { get; set; }
    public DateTime FechaFinTramo { get; set; }
    public int MesCorte { get; set; }
    public int AnioCorte { get; set; }
    public int TotalCarpetasIncluidas { get; set; }
    public int TotalDelitosIncluidos { get; set; }
    public int TotalVictimasIncluidas { get; set; }
    public int TotalCarpetasExcluidas { get; set; }
    public int TotalDelitosExcluidos { get; set; }
    public int TotalVictimasExcluidas { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateTime FechaValidacion { get; set; }
    public DateTime? FechaConfirmacion { get; set; }
    public int IdUsuarioCarga { get; set; }
    public string UsuarioCarga { get; set; } = string.Empty;
    public List<SemanalCargaAcuseBloque> Bloques { get; set; } = new();
}