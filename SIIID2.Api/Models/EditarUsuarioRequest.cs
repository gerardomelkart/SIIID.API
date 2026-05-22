namespace SIIID2.Api.Models;

public class EditarUsuarioRequest
{
    public string Usuario { get; set; } = string.Empty;

    // Si viene null o vacío, NO se cambia la contraseña.
    // Si viene con valor, se hashea y reemplaza la anterior.
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
}