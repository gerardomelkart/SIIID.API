namespace SIIID2.Api.Models;

public class SemanalEnvioBloqueItem
{
    public long IdSemanalCarga { get; set; }
    public int AnioSemana { get; set; }
    public int NumeroSemana { get; set; }
    public DateTime FechaInicioSemana { get; set; }
    public DateTime FechaFinSemana { get; set; }
    public int AnioCorte { get; set; }
    public int MesCorte { get; set; }
    public DateTime FechaInicioTramo { get; set; }
    public DateTime FechaFinTramo { get; set; }
    public int TotalCarpetas { get; set; }
    public int TotalDelitos { get; set; }
    public int TotalVictimas { get; set; }
    public bool ReemplazaInformacion { get; set; }
}

public class SemanalEnvioPeriodoItem
{
    public long IdSemanalCarga { get; set; }
    public int AnioCorte { get; set; }
    public int MesCorte { get; set; }
}

public class SemanalEnvioPeriodoOpcionItem
{
    public int AnioCorte { get; set; }
    public int MesCorte { get; set; }
}

public class SemanalEnvioSemanaOpcionItem
{
    public int AnioCorte { get; set; }
    public int MesCorte { get; set; }
    public int AnioSemana { get; set; }
    public int NumeroSemana { get; set; }
    public DateTime FechaInicioSemana { get; set; }
    public DateTime FechaFinSemana { get; set; }
}

public class SemanalEnviosOpcionesResponse
{
    public bool EsValido { get; set; } = true;
    public List<SemanalEnvioPeriodoOpcionItem> Periodos { get; set; } = [];
    public List<SemanalEnvioSemanaOpcionItem> Semanas { get; set; } = [];
}

public class SemanalEnvioItem
{
    public long IdSemanalCarga { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string TipoCarga { get; set; } = string.Empty;
    public int IdEntidadFederativa { get; set; }
    public string EntidadFederativa { get; set; } = string.Empty;
    public string ClaveEntidad { get; set; } = string.Empty;
    public List<string> Delitos { get; set; } = [];
    public int AnioSemana { get; set; }
    public int NumeroSemana { get; set; }
    public DateTime FechaInicioSemana { get; set; }
    public DateTime FechaFinSemana { get; set; }
    public DateTime FechaInicioTramo { get; set; }
    public DateTime FechaFinTramo { get; set; }
    public int MesCorte { get; set; }
    public int AnioCorte { get; set; }
    public string Periodo { get; set; } = string.Empty;
    public List<SemanalEnvioPeriodoItem> Periodos { get; set; } = [];
    public int IdUsuarioCarga { get; set; }
    public string UsuarioCarga { get; set; } = string.Empty;
    public string NombreUsuarioCarga { get; set; } = string.Empty;
    public int TotalCarpetasIncluidas { get; set; }
    public int TotalDelitosIncluidos { get; set; }
    public int TotalVictimasIncluidas { get; set; }
    public int TotalAdvertencias { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string EstadoTexto { get; set; } = string.Empty;
    public DateTime FechaCarga { get; set; }
    public DateTime? FechaValidacion { get; set; }
    public DateTime? FechaConfirmacion { get; set; }
    public DateTime FechaMovimiento { get; set; }
    public string? MotivoRechazo { get; set; }
    public string? UsuarioResolucion { get; set; }
    public bool EsConfirmado { get; set; }
    public bool EsPendiente { get; set; }
    public bool PuedeResolverPendiente { get; set; }
    public string EndpointAcuse { get; set; } = string.Empty;
    public string EndpointArchivos { get; set; } = string.Empty;
    public string FechaEnvioTexto { get; set; } = string.Empty;
    public string Semana { get; set; } = string.Empty;
    public bool EsRechazadoAdministrador { get; set; }
    public bool TieneStagingDisponible { get; set; }
    public string FechaRechazoTexto { get; set; } = string.Empty;
    public List<SemanalEnvioBloqueItem> Bloques { get; set; } = [];
}

public class SemanalEnvioReferenciaInfo
{
    public long IdSemanalCarga { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string TipoCarga { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public int IdEntidadFederativa { get; set; }
    public string EntidadFederativa { get; set; } = string.Empty;
    public int IdUsuarioCarga { get; set; }
    public int AnioSemana { get; set; }
    public int NumeroSemana { get; set; }
    public int AnioCorte { get; set; }
    public int MesCorte { get; set; }
}

public class SemanalCargaDelitoItem
{
    public long IdSemanalCarga { get; set; }
    public string Delito { get; set; } = string.Empty;
}

public class SemanalReporteCargaDelitoItem
{
    public int IdEntidadFederativa { get; set; }
    public int IdUsuarioCarga { get; set; }
    public int AnioCorte { get; set; }
    public int MesCorte { get; set; }
    public string Delito { get; set; } = string.Empty;
}