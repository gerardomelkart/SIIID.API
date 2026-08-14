namespace SIIID2.Api.Models;

public class FederalCatalogoResumen
{
    public int BienesJuridicos { get; set; }
    public int Delitos { get; set; }
    public int Subtipos { get; set; }
    public int Modalidades { get; set; }
    public int ModalidadesComunes { get; set; }
    public int ModalidadesFederales { get; set; }
    public int CombinacionesSabana { get; set; }
    public int CombinacionesComunes { get; set; }
    public int CombinacionesFederales { get; set; }
}

public class FederalBienJuridicoCatalogoItem
{
    public int IdBienJuridico { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string BienJuridico { get; set; } = string.Empty;
}

public class FederalDelitoCatalogoItem
{
    public int IdDelito { get; set; }
    public int IdBienJuridico { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Delito { get; set; } = string.Empty;
}

public class FederalSubtipoDelitoCatalogoItem
{
    public int IdSubtipoDelito { get; set; }
    public int IdDelito { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string SubtipoDelito { get; set; } = string.Empty;
}

public class FederalModalidadDelitoCatalogoItem
{
    public int IdModalidadDelito { get; set; }
    public int IdSubtipoDelito { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string ModalidadDelito { get; set; } = string.Empty;
    public int IdAdmiteTentativa { get; set; }
    public bool EsFueroFederal { get; set; }
}