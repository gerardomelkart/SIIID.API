namespace SIIID2.Api.Models;

public class ActualizarPermisosSemanalesRequest
{
    public bool HabilitaSemanal { get; set; }
    public bool HabilitaCargaSemanal { get; set; }
    public bool HabilitaModificacionSemanal { get; set; }
    public bool AdministraDelitosSemanal { get; set; }
}