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
    public bool EsValido => Errores.Count == 0;

    public string CodigoReferencia { get; set; } = string.Empty;

    public string ModalidadIngreso { get; set; } = string.Empty;

    public int TotalCarpetas { get; set; }
    public int TotalDelitos { get; set; }
    public int TotalVictimas { get; set; }

    public string Mensaje { get; set; } = string.Empty;

    public List<BanciCargaValidacionError> Errores { get; set; } = [];
    public List<BanciCargaValidacionError> Advertencias { get; set; } = [];
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