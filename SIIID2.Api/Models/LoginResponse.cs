namespace SIIID2.Api.Models;

public class LoginResponse
{
    public bool EsValido { get; set; }
    public string Mensaje { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public int ExpiraEnMinutos { get; set; }
    public UsuarioLoginInfo? Usuario { get; set; }
}

public class UsuarioLoginInfo
{
    public int IdUsuario { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public string NombreCompleto { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public int? IdEntidadFederativa { get; set; }
    public string? EntidadFederativa { get; set; }
    public bool HabilitaCarga { get; set; }
    public bool HabilitaModificacion { get; set; }
    public bool RequiereCambioPassword { get; set; }
}