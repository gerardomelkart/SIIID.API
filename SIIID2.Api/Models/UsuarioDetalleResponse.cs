namespace SIIID2.Api.Models;

public class UsuarioDetalleResponse
{
    public bool EsValido { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public UsuarioDetalle? Usuario { get; set; }
}

public class UsuarioDetalle
{
    public int IdUsuario { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string PrimerApellido { get; set; } = string.Empty;
    public string? SegundoApellido { get; set; }
    public string CorreoElectronico { get; set; } = string.Empty;
    public string Rfc { get; set; } = string.Empty;
    public string Curp { get; set; } = string.Empty;
    public string? TelefonoContacto { get; set; }
    public int? IdEntidadFederativa { get; set; }
    public string? EntidadFederativa { get; set; }
    public int IdRol { get; set; }
    public string Rol { get; set; } = string.Empty;
    public bool HabilitaCarga { get; set; }
    public bool HabilitaModificacion { get; set; }
    public bool HabilitaSemanal { get; set; }
    public bool HabilitaCargaSemanal { get; set; }
    public bool HabilitaModificacionSemanal { get; set; }
    public bool AdministraDelitosSemanal { get; set; }
    public DateTime FechaAlta { get; set; }
    public DateTime FechaModificacion { get; set; }
    public bool Activo { get; set; }
}