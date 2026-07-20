namespace SIIID2.Api.Models;

public class ModuloUsuarioInfo
{
    public int IdModulo { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public bool HabilitaCarga { get; set; }
    public bool HabilitaModificacion { get; set; }
    public bool AdministraDelitos { get; set; }
}