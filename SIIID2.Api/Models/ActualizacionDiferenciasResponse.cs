namespace SIIID2.Api.Models;

public class ActualizacionDiferenciasResponse
{
    public bool EsValido { get; set; }

    public string CodigoReferencia { get; set; } = string.Empty;

    public string Mensaje { get; set; } = string.Empty;

    public int TotalCarpetas { get; set; }

    public int TotalDelitos { get; set; }

    public int TotalVictimas { get; set; }

    public int TotalDiferencias { get; set; }

    public int LimitePorSeccion { get; set; }

    public bool DetalleLimitado { get; set; }

    public List<ActualizacionDiferenciaRegistro> Carpetas { get; set; } = new();

    public List<ActualizacionDiferenciaRegistro> Delitos { get; set; } = new();

    public List<ActualizacionDiferenciaRegistro> Victimas { get; set; } = new();
}