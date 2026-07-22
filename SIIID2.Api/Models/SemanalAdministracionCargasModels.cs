using System.ComponentModel.DataAnnotations;

namespace SIIID2.Api.Models;

public class SemanalCargaPendienteAdministracionItem
{
    public long IdSemanalCarga { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string TipoContenido { get; set; } = string.Empty;
    public int? IdEntidadFederativa { get; set; }
    public string EntidadFederativa { get; set; } = string.Empty;
    public int AnioSemana { get; set; }
    public int NumeroSemana { get; set; }
    public DateTime FechaInicioSemana { get; set; }
    public DateTime FechaFinSemana { get; set; }
    public DateTime FechaInicioTramo { get; set; }
    public DateTime FechaFinTramo { get; set; }
    public int MesCorte { get; set; }
    public int AnioCorte { get; set; }
    public DateTime FechaValidacion { get; set; }
    public int IdUsuarioCarga { get; set; }
    public string UsuarioCarga { get; set; } = string.Empty;
    public string NombreUsuarioCarga { get; set; } = string.Empty;
    public int TotalCarpetasIncluidas { get; set; }
    public int TotalDelitosIncluidos { get; set; }
    public int TotalVictimasIncluidas { get; set; }
    public int TotalCarpetasExcluidas { get; set; }
    public int TotalDelitosExcluidos { get; set; }
    public int TotalVictimasExcluidas { get; set; }
    public int TotalAdvertencias { get; set; }
}

public class SemanalCargaAdvertenciaAdministracionItem
{
    public long IdSemanalCargaAdvertencia { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Archivo { get; set; } = string.Empty;
    public int? NumeroFila { get; set; }
    public string? Columna { get; set; }
    public string? Campo { get; set; }
    public string? Valor { get; set; }
    public string DescripcionResumen { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public bool AceptadaUsuario { get; set; }
    public DateTime? FechaAceptacion { get; set; }
}

public class SemanalCargaPendienteAdministracionDetalle : SemanalCargaPendienteAdministracionItem
{
    public List<SemanalCargaAdvertenciaAdministracionItem> Advertencias { get; set; } = [];
}

public class SemanalCargaReferenciaAdministracionInfo
{
    public string CodigoReferencia { get; set; } = string.Empty;
    public string TipoCarga { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
}

public class RechazarCargaSemanalAdministracionRequest
{
    [Required(ErrorMessage = "El motivo del rechazo es obligatorio.")]
    [StringLength(2000, MinimumLength = 5, ErrorMessage = "El motivo debe tener entre 5 y 2000 caracteres.")]
    public string Motivo { get; set; } = string.Empty;
}