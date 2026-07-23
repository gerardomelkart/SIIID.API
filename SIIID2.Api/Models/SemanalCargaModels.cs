namespace SIIID2.Api.Models;

public class SemanalCargaValidacionRequest
{
    public IFormFile? Carpetas { get; set; }
    public IFormFile? Delitos { get; set; }
    public IFormFile? Victimas { get; set; }
    public string TipoCarga { get; set; } = "CARGA_INICIAL";
    public string TipoContenido { get; set; } = string.Empty;
    public int AnioSemana { get; set; }
    public int NumeroSemana { get; set; }
    public DateTime FechaInicioSemana { get; set; }
    public int MesCorte { get; set; }
    public int AnioCorte { get; set; }
    public int? IdEntidadFederativa { get; set; }
}

public class SemanalPeriodoCarga
{
    public string TipoContenido { get; set; } = string.Empty;
    public int AnioSemana { get; set; }
    public int NumeroSemana { get; set; }
    public DateTime FechaInicioSemana { get; set; }
    public DateTime FechaFinSemana { get; set; }
    public DateTime FechaInicioTramo { get; set; }
    public DateTime FechaFinTramo { get; set; }
    public int MesCorte { get; set; }
    public int AnioCorte { get; set; }
}

public class SemanalCargaValidacionResponse : CargaValidacionResponse
{
    public string TipoCarga { get; set; } = "CARGA_INICIAL";
    public SemanalPeriodoCarga? Periodo { get; set; }
    public int TotalCarpetasIncluidas { get; set; }
    public int TotalDelitosIncluidos { get; set; }
    public int TotalVictimasIncluidas { get; set; }
    public int TotalCarpetasExcluidas { get; set; }
    public int TotalDelitosExcluidos { get; set; }
    public int TotalVictimasExcluidas { get; set; }
}

public class SemanalArchivoFilaCarga
{
    public ArchivoFila Fila { get; set; } = new();
    public bool Incluido { get; set; }
    public string? CodigoExclusion { get; set; }
}

public class SemanalCargaPersistencia
{
    public int IdUsuarioCarga { get; set; }
    public int IdEntidadFederativa { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string TipoCarga { get; set; } = "CARGA_INICIAL";
    public SemanalPeriodoCarga Periodo { get; set; } = new();
    public int TotalCarpetasIncluidas { get; set; }
    public int TotalDelitosIncluidos { get; set; }
    public int TotalVictimasIncluidas { get; set; }
    public int TotalCarpetasExcluidas { get; set; }
    public int TotalDelitosExcluidos { get; set; }
    public int TotalVictimasExcluidas { get; set; }
    public List<CargaValidacionError> Advertencias { get; set; } = new();
    public List<ConfiguracionModalidadSemanalItem> ModalidadesConfiguradas { get; set; } = new();
    public List<SemanalArchivoFilaCarga> Carpetas { get; set; } = new();
    public List<SemanalArchivoFilaCarga> Delitos { get; set; } = new();
    public List<SemanalArchivoFilaCarga> Victimas { get; set; } = new();
}

public class SemanalFilaComparacion
{
    public long IdSemanalCarga { get; set; }
    public DateTime FechaConfirmacion { get; set; }
    public string IdCi { get; set; } = string.Empty;
    public string Datos { get; set; } = "{}";
}

public class SemanalCargaPendienteComparacion
{
    public string CodigoReferencia { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public DateTime FechaInicioSemana { get; set; }
    public string FechaInicioCarpeta { get; set; } = string.Empty;
}

public class SemanalDatosComparacion
{
    public List<SemanalFilaComparacion> CarpetasConfirmadas { get; set; } = new();
    public List<SemanalFilaComparacion> DelitosConfirmados { get; set; } = new();
    public List<SemanalFilaComparacion> VictimasConfirmadas { get; set; } = new();
    public List<SemanalCargaPendienteComparacion> CargasPendientes { get; set; } = new();
}

public class SemanalSemanaEstadoInfo
{
    public bool TieneCargaConfirmada { get; set; }
    public string? CodigoReferenciaPendiente { get; set; }
    public string? EstadoPendiente { get; set; }
    public string? TipoCargaPendiente { get; set; }
    public int? IdUsuarioCargaPendiente { get; set; }
}

public class SemanalSemanaDisponibilidadResponse
{
    public bool EsValido { get; set; }
    public bool Disponible { get; set; }
    public bool TieneCargaConfirmada { get; set; }
    public bool ExisteOperacionPendiente { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public string? CodigoReferenciaPendiente { get; set; }
    public string? EstadoPendiente { get; set; }
    public string? TipoCargaPendiente { get; set; }
    public bool PendientePropia { get; set; }
    public bool PuedeResolverPendiente { get; set; }
    public bool DebeUsarActualizacion { get; set; }
}