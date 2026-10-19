namespace SIIID2.Api.Models;
// El libro conserva las llaves del consolidado; únicamente los complementos se editan en BANCI.
public sealed class BanciLibro
{
    public int Entidad { get; set; }
    public int Mes { get; set; }
    public int Anio { get; set; }
    public bool Actualizacion { get; set; }
    public bool Manual { get; set; }
    public List<BanciCambioVista> Cambios { get; set; } = [];
    public List<ArchivoFila> Carpetas { get; set; } = [];
    public List<ArchivoFila> Delitos { get; set; } = [];
    public List<ArchivoFila> Victimas { get; set; } = [];
}

public sealed class BanciOperacion
{
    public long Id { get; set; }
    public Guid Referencia { get; set; }
    public int Entidad { get; set; }
    public int Mes { get; set; }
    public int Anio { get; set; }
    public string Tipo { get; set; } = "";
    public string Estado { get; set; } = "";
    public int Usuario { get; set; }
    public string NombreUsuario { get; set; } = "";
    public string EntidadNombre { get; set; } = "";
    public string? Snapshot { get; set; }
    public string? Huella { get; set; }
    public DateTime Fecha { get; set; }
    public string? Motivo { get; set; }
    public bool Manual { get; set; }
    public bool TieneOriginal { get; set; }
    public DateTime? FechaEnvio { get; set; }
    public string? NombreOriginal { get; set; }
    public int TotalCarpetas { get; set; }
    public int TotalDelitos { get; set; }
    public int TotalVictimas { get; set; }
}

public sealed class BanciDecision
{
    public bool Aceptar { get; set; }
    public string Motivo { get; set; } = "";
}

public sealed record BanciCambioVista(string Carpeta, string Delito, string Victima, string Campo, string? Anterior, string? Nuevo);

public sealed class BanciPeriodo
{
    public int Anio { get; set; }
    public int Mes { get; set; }
}
