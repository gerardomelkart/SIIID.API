namespace SIIID2.Api.Models;

public class UsuarioListadoItem
{
    public int IdUsuario { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string CorreoElectronico { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public int? IdEntidadFederativa { get; set; }
    public string? EntidadFederativa { get; set; }
    public bool HabilitaMensual { get; set; }
    public bool HabilitaCarga { get; set; }
    public bool HabilitaModificacion { get; set; }
    public bool HabilitaSemanal { get; set; }
    public bool HabilitaCargaSemanal { get; set; }
    public bool HabilitaModificacionSemanal { get; set; }
    public bool AdministraDelitosSemanal { get; set; }
    public bool Activo { get; set; }
}