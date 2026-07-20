namespace SIIID2.Api.Models;

public class EditarUsuarioRequest
{
    public string Usuario { get; set; } = string.Empty;
    public string? NuevaPassword { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string PrimerApellido { get; set; } = string.Empty;
    public string? SegundoApellido { get; set; }
    public string CorreoElectronico { get; set; } = string.Empty;
    public string Rfc { get; set; } = string.Empty;
    public string Curp { get; set; } = string.Empty;
    public string? TelefonoContacto { get; set; }
    public int? IdEntidadFederativa { get; set; }
    public string Rol { get; set; } = string.Empty;
    public bool HabilitaCarga { get; set; }
    public bool HabilitaModificacion { get; set; }
    public bool? HabilitaSemanal { get; set; }
    public bool? HabilitaCargaSemanal { get; set; }
    public bool? HabilitaModificacionSemanal { get; set; }
    public bool? AdministraDelitosSemanal { get; set; }
}