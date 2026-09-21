namespace SIIID2.Api.Models;

public class BanciCargaArchivosRequest
{
    public IFormFile? ArchivoLibro { get; set; }
    public IFormFile? ArchivoCarpetas { get; set; }
    public IFormFile? ArchivoDelitos { get; set; }
    public IFormFile? ArchivoVictimas { get; set; }
}

public class BanciCargaValidacionResponse
{
    public BanciVistaPrevia? VistaPrevia { get; set; }
    public bool EsValido => Errores.Count == 0;

    public long IdBanciCarga { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateTime? FechaCarga { get; set; }
    public bool? AceptadaUsuario { get; set; }
    public int? IdUsuarioConfirmacion { get; set; }
    public DateTime? FechaConfirmacion { get; set; }
    public bool YaResuelta { get; set; }
    public int TotalAltas { get; set; }
    public int TotalActualizaciones { get; set; }
    public int TotalSinCambio { get; set; }
    public int TotalAdvertencias { get; set; }

    public string CodigoReferencia { get; set; } = string.Empty;

    public string ModalidadIngreso { get; set; } = string.Empty;

    public int TotalCarpetas { get; set; }
    public int TotalDelitos { get; set; }
    public int TotalVictimas { get; set; }

    public string Mensaje { get; set; } = string.Empty;

    public List<BanciCargaValidacionError> Errores { get; set; } = [];
    public List<BanciCargaValidacionError> Advertencias { get; set; } = [];
}

public class BanciCargaConfirmacionRequest
{
    [System.ComponentModel.DataAnnotations.StringLength(64)]
    public string? HuellaVistaPrevia { get; set; }
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(50)]
    public string CodigoReferencia { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    public bool? Aceptar { get; set; }
}

public class BanciVistaPrevia
{
    public bool PuedeAceptar { get; set; }
    public string? MotivoBloqueo { get; set; }
    public string Huella { get; set; } = string.Empty;
    public int TotalCambios { get; set; }
    public List<BanciVistaPreviaResumen> Resumen { get; set; } = [];
    public List<BanciVistaPreviaCambio> Cambios { get; set; } = [];
}

public class BanciVistaPreviaResumen
{
    public string Tipo { get; set; } = string.Empty;
    public int Altas { get; set; }
    public int Actualizaciones { get; set; }
    public int SinCambio { get; set; }
}

public class BanciVistaPreviaCambio
{
    public string Tipo { get; set; } = string.Empty;
    public string IdCi { get; set; } = string.Empty;
    public string? IdDelito { get; set; }
    public string? IdVictima { get; set; }
    public string Campo { get; set; } = string.Empty;
    public string? Anterior { get; set; }
    public string? Nuevo { get; set; }
}

public class BanciCargaValidacionError
{
    public string Archivo { get; set; } = string.Empty;
    public string? Hoja { get; set; }
    public int? NumeroFila { get; set; }
    public string? Campo { get; set; }
    public string? Valor { get; set; }

    public string Codigo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
}

public class BanciLecturaArchivosResultado
{
    public string ModalidadIngreso { get; set; } = string.Empty;

    public List<ArchivoFila> Carpetas { get; set; } = [];
    public List<ArchivoFila> Delitos { get; set; } = [];
    public List<ArchivoFila> Victimas { get; set; } = [];

    public List<BanciCargaValidacionError> Errores { get; set; } = [];
}

public class BanciUsuarioCargaInfo
{
    public int IdUsuario { get; set; }
    public int? IdEntidadFederativa { get; set; }
    public string Rol { get; set; } = string.Empty;
    public bool HabilitaCarga { get; set; }
    public bool HabilitaModificacion { get; set; }

    public bool EsSuperUsuario =>
        string.Equals(
            Rol,
            "SUPER_USUARIO",
            StringComparison.OrdinalIgnoreCase);
}
