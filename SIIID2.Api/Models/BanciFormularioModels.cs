namespace SIIID2.Api.Models;

public class BanciFormularioRequest
{
    public int? IdEntidadFederativa { get; set; }
    public Dictionary<string, string?> Carpeta { get; set; } = [];
    public List<BanciFormularioDelito> Delitos { get; set; } = [];
}

public class BanciFormularioDelito
{
    public Dictionary<string, string?> Datos { get; set; } = [];
    public List<Dictionary<string, string?>> Victimas { get; set; } = [];
}

public class BanciFormularioOpcion
{
    public string Campo { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public int? IdEntidadFederativa { get; set; }
}

public class BanciFormularioOpciones
{
    public bool EsSuperUsuario { get; set; }
    public int? IdEntidadFederativa { get; set; }
    public IReadOnlyList<BanciFormularioOpcion> Catalogos { get; set; } = [];
}
