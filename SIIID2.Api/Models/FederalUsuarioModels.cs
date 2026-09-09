using System.ComponentModel.DataAnnotations;

namespace SIIID2.Api.Models;

public class FederalUsuarioDatos
{
    [Required, StringLength(50)] public string Usuario { get; set; } = string.Empty;
    [Required, StringLength(50)] public string Nombre { get; set; } = string.Empty;
    [Required, StringLength(50)] public string PrimerApellido { get; set; } = string.Empty;
    [StringLength(50)] public string? SegundoApellido { get; set; }
    [Required, StringLength(100)] public string CorreoElectronico { get; set; } = string.Empty;
    [StringLength(13)] public string? Rfc { get; set; }
    [StringLength(18)] public string? Curp { get; set; }
    [StringLength(15)] public string? TelefonoContacto { get; set; }
    [Required] public string Rol { get; set; } = string.Empty;
    public bool HabilitaFederal { get; set; } = true;
    public bool HabilitaCarga { get; set; }
    public bool HabilitaModificacion { get; set; }
}

public class CrearUsuarioFederalRequest : FederalUsuarioDatos
{
    [Required] public string Password { get; set; } = string.Empty;
}

public class EditarUsuarioFederalRequest : FederalUsuarioDatos
{
    public string? NuevaPassword { get; set; }
}

public class ReactivarUsuarioFederalRequest
{
    public bool HabilitaFederal { get; set; } = true;
    public bool HabilitaCarga { get; set; }
    public bool HabilitaModificacion { get; set; }
}

public class FederalUsuarioDetalle : FederalUsuarioDatos
{
    public int IdUsuario { get; set; }
    public int IdRol { get; set; }
    public string NombreCompleto => string.Join(" ", new[] { Nombre, PrimerApellido, SegundoApellido }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public bool Activo { get; set; }
    public bool ActivoCuenta { get; set; }
    public bool TieneOtrosModulos { get; set; }
    public DateTime FechaAlta { get; set; }
    public DateTime FechaModificacion { get; set; }
}

public class FederalUsuarioDetalleResponse
{
    public bool EsValido { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public FederalUsuarioDetalle? Usuario { get; set; }
}
