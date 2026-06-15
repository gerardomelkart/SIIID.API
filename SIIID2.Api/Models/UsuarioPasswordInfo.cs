namespace SIIID2.Api.Models;

public class UsuarioPasswordInfo
{
    public int IdUsuario { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool RequiereCambioPassword { get; set; }
}