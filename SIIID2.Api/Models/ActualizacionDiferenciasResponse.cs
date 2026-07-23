using System.Text.Json.Serialization;

namespace SIIID2.Api.Models;

public class ActualizacionDiferenciasResumen
{
    public int Nuevos { get; set; }
    public int Modificados { get; set; }
    public int Eliminados { get; set; }
}

public class ActualizacionDiferenciasResponse
{
    [JsonIgnore]
    public long IdCarga { get; set; }

    public bool EsValido { get; set; }
    public string CodigoReferencia { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public int TotalCarpetas { get; set; }
    public int TotalDelitos { get; set; }
    public int TotalVictimas { get; set; }
    public int TotalDiferencias { get; set; }
    public int LimitePorSeccion { get; set; }
    public bool DetalleLimitado { get; set; }
    public ActualizacionDiferenciasResumen ResumenCarpetas { get; set; } = new();
    public ActualizacionDiferenciasResumen ResumenDelitos { get; set; } = new();
    public ActualizacionDiferenciasResumen ResumenVictimas { get; set; } = new();
    public ActualizacionDiferenciasResumen ResumenTotal { get; set; } = new();
    public List<ActualizacionDiferenciaRegistro> Carpetas { get; set; } = new();
    public List<ActualizacionDiferenciaRegistro> Delitos { get; set; } = new();
    public List<ActualizacionDiferenciaRegistro> Victimas { get; set; } = new();
}