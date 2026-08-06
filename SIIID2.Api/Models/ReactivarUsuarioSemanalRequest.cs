namespace SIIID2.Api.Models;

public class ReactivarUsuarioSemanalRequest
{
    public bool HabilitaSemanal { get; set; }
    public bool HabilitaCargaSemanal { get; set; }
    public bool AdministraDelitosSemanal { get; set; }
}