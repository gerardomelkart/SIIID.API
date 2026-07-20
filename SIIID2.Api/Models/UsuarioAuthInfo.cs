namespace SIIID2.Api.Models;

public class UsuarioAuthInfo
{
    public int IdUsuario { get; set; }

    public string Usuario { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool RequiereCambioPassword { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public string PrimerApellido { get; set; } = string.Empty;

    public string? SegundoApellido { get; set; }

    public string Rol { get; set; } = string.Empty;

    public int? IdEntidadFederativa { get; set; }

    public string? EntidadFederativa { get; set; }

    public bool HabilitaCarga { get; set; }

    public bool HabilitaModificacion { get; set; }

    public List<ModuloUsuarioInfo> Modulos { get; set; } = new();

    public string NombreCompleto
    {
        get
        {
            return string.Join(" ", new[]
            {
                Nombre,
                PrimerApellido,
                SegundoApellido
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
    }
}