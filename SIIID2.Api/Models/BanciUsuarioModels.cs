using System.ComponentModel.DataAnnotations;

namespace SIIID2.Api.Models;

public class BanciUsuarioDatos
{
    [Range(1, 32)] public int? IdEntidadFederativa { get; set; }
    [Required, StringLength(50)] public string Usuario { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Nombre { get; set; } = string.Empty;
    [Required, StringLength(50)] public string PrimerApellido { get; set; } = string.Empty;
    [StringLength(50)] public string? SegundoApellido { get; set; }
    [Required, StringLength(100)] public string CorreoElectronico { get; set; } = string.Empty;
    [StringLength(13)] public string? Rfc { get; set; }
    [StringLength(18)] public string? Curp { get; set; }
    [StringLength(15)] public string? TelefonoContacto { get; set; }
    [Required] public string Rol { get; set; } = string.Empty;
    public bool HabilitaBanci { get; set; } = true;
    public bool HabilitaCarga { get; set; }
    public bool HabilitaModificacion { get; set; }
}

public class CrearUsuarioBanciRequest : BanciUsuarioDatos
{
    [Required] public string Password { get; set; } = string.Empty;
}

public class EditarUsuarioBanciRequest : BanciUsuarioDatos
{
    public string? NuevaPassword { get; set; }
}

public class ReactivarUsuarioBanciRequest
{
    public bool HabilitaBanci { get; set; } = true;
    public bool HabilitaCarga { get; set; }
    public bool HabilitaModificacion { get; set; }
}

public class BanciUsuarioDetalle : BanciUsuarioDatos
{
    public int IdUsuario { get; set; }
    public string? EntidadFederativa { get; set; }
    public int IdRol { get; set; }
    public string NombreCompleto => string.Join(" ", new[] { Nombre, PrimerApellido, SegundoApellido }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public bool Activo { get; set; }
    public bool ActivoCuenta { get; set; }
    public bool TieneBanci { get; set; }
    public bool TieneOtrosModulos { get; set; }
    public DateTime FechaAlta { get; set; }
    public DateTime FechaModificacion { get; set; }
}

public class BanciUsuarioDetalleResponse
{
    public bool EsValido { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public BanciUsuarioDetalle? Usuario { get; set; }
}


public class PermisosGlobalesBanciRequest
{
    public bool HabilitaCarga { get; set; }
    public bool HabilitaModificacion { get; set; }
}

public class ActualizarPermisosBanciRequest : PermisosGlobalesBanciRequest
{
    public bool HabilitaBanci { get; set; }
}
