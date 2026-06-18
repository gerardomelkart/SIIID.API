using System.ComponentModel.DataAnnotations;

namespace SIIID2.Api.Models;

public class CargaPendienteAdministracionItem
{
    public long IdCarga { get; set; }

    public string CodigoReferencia { get; set; } = string.Empty;

    public string TipoCarga { get; set; } = string.Empty;

    public int? IdEntidadFederativa { get; set; }

    public string EntidadFederativa { get; set; } = string.Empty;

    public int MesCorte { get; set; }

    public int AnioCorte { get; set; }

    public DateTime FechaValidacion { get; set; }

    public int IdUsuarioCarga { get; set; }

    public string UsuarioCarga { get; set; } = string.Empty;

    public string NombreUsuarioCarga { get; set; } = string.Empty;

    public int TotalCarpetas { get; set; }

    public int TotalDelitos { get; set; }

    public int TotalVictimas { get; set; }

    public int TotalAdvertencias { get; set; }
}

public class CargaAdvertenciaAdministracionItem
{
    public long IdCargaAdvertencia { get; set; }

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

public class CargaPendienteAdministracionDetalle : CargaPendienteAdministracionItem
{
    public List<CargaAdvertenciaAdministracionItem> Advertencias { get; set; } = [];
}

public class RechazarCargaAdministracionRequest
{
    [Required(ErrorMessage = "El motivo del rechazo es obligatorio.")]
    [StringLength(2000, MinimumLength = 5, ErrorMessage = "El motivo debe tener entre 5 y 2000 caracteres.")]
    public string Motivo { get; set; } = string.Empty;
}