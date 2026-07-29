namespace SIIID2.Api.Models;

public class ReactivarUsuarioRequest
{
    public bool HabilitaMensual { get; set; } = true;
    public bool HabilitaCarga { get; set; }
    public bool HabilitaModificacion { get; set; }
}