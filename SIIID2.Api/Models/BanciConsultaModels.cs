using System.ComponentModel.DataAnnotations;

namespace SIIID2.Api.Models;

public class BanciConsultaFiltro
{
    [Range(1900, 9998)] public int Anio { get; set; } = DateTime.Today.Year;
    [Range(1, 12)] public int? Mes { get; set; }
    [Range(1, 32)] public int? IdEntidadFederativa { get; set; }
    [StringLength(250)] public string? Busqueda { get; set; }
    [Range(1, 1000000)] public int Pagina { get; set; } = 1;
    [Range(1, 100)] public int TamanoPagina { get; set; } = 25;
}

public class BanciConsultaOpciones
{
    public bool AlcanceNacional { get; set; }
    public List<int> Anios { get; set; } = [];
    public List<BanciConsultaEntidad> Entidades { get; set; } = [];
}

public class BanciConsultaEntidad
{
    public int IdEntidadFederativa { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

public class BanciConsultaResultado
{
    public long TotalCarpetas { get; set; }
    public long TotalDelitos { get; set; }
    public long TotalVictimas { get; set; }
    public int Pagina { get; set; }
    public int TamanoPagina { get; set; }
    public long TotalPaginas => (TotalCarpetas + TamanoPagina - 1) / Math.Max(1, TamanoPagina);
    public List<BanciConsultaCarpeta> Carpetas { get; set; } = [];
}

public class BanciConsultaCarpeta
{
    public string NoBanci { get; set; } = string.Empty;
    public long IdBanciCarpetaInvestigacion { get; set; }
    public int IdEntidadFederativa { get; set; }
    public string Entidad { get; set; } = string.Empty;
    public string IdCi { get; set; } = string.Empty;
    public string NtraCi { get; set; } = string.Empty;
    public DateTime FechaInicio { get; set; }
    public long TotalDelitos { get; set; }
    public long TotalVictimas { get; set; }
}

public class BanciCampoConsulta
{
    public string Nombre { get; set; } = string.Empty;
    public string? Valor { get; set; }
}

public class BanciConsultaDetalle
{
    public List<BanciCampoConsulta> Carpeta { get; set; } = [];
    public List<BanciConsultaDelito> Delitos { get; set; } = [];
}

public class BanciConsultaDelito
{
    public long Id { get; set; }
    public List<BanciCampoConsulta> Campos { get; set; } = [];
    public List<List<BanciCampoConsulta>> Victimas { get; set; } = [];
}
