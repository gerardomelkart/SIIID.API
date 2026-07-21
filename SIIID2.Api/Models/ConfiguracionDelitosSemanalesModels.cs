namespace SIIID2.Api.Models;

public class ConfiguracionModalidadSemanalItem
{
    public int IdBienJuridico { get; set; }
    public string ClaveBienJuridico { get; set; } = string.Empty;
    public string BienJuridico { get; set; } = string.Empty;
    public int IdDelito { get; set; }
    public string ClaveDelito { get; set; } = string.Empty;
    public string Delito { get; set; } = string.Empty;
    public int IdSubtipoDelito { get; set; }
    public string ClaveSubtipo { get; set; } = string.Empty;
    public string Subtipo { get; set; } = string.Empty;
    public int IdModalidadDelito { get; set; }
    public string ClaveModalidad { get; set; } = string.Empty;
    public string Modalidad { get; set; } = string.Empty;
    public bool Seleccionado { get; set; }
    public bool EsObligatorio { get; set; }
    public bool ConservarEntrePeriodos { get; set; }
    public short Orden { get; set; }
}

public class ConfiguracionModalidadSemanalRequest
{
    public int IdModalidadDelito { get; set; }
    public bool Seleccionado { get; set; }
}

public class ActualizarConfiguracionDelitosSemanalesRequest
{
    public List<ConfiguracionModalidadSemanalRequest> Modalidades { get; set; } = new();
}

public class ConfiguracionDelitosSemanalesResponse
{
    public bool EsValido { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public int TotalSeleccionados { get; set; }
    public List<ConfiguracionModalidadSemanalItem> Modalidades { get; set; } = new();
}