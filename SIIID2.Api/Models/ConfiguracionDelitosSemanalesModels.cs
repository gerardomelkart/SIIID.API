namespace SIIID2.Api.Models;

public class ConfiguracionDelitoSemanalItem
{
    public int IdDelito { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Delito { get; set; } = string.Empty;
    public string BienJuridico { get; set; } = string.Empty;
    public bool Seleccionado { get; set; }
    public bool EsObligatorio { get; set; }
    public bool ConservarEntrePeriodos { get; set; }
    public short Orden { get; set; }
}

public class ConfiguracionDelitoSemanalRequest
{
    public int IdDelito { get; set; }
    public bool Seleccionado { get; set; }
    public short Orden { get; set; }
}

public class ActualizarConfiguracionDelitosSemanalesRequest
{
    public List<ConfiguracionDelitoSemanalRequest> Delitos { get; set; } = new();
}

public class ConfiguracionDelitosSemanalesResponse
{
    public bool EsValido { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public int TotalSeleccionados { get; set; }
    public List<ConfiguracionDelitoSemanalItem> Delitos { get; set; } = new();
}