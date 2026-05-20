namespace SIIID2.Api.Models;

public class UsuarioCargaInfo
{
    public int IdUsuario { get; set; }

    // Entidad asignada al usuario.
    // Si es SUPER_USUARIO puede venir nula.
    public int? IdEntidadFederativa { get; set; }

    // Rol del usuario: SUPER_USUARIO, ADMIN, USUARIO, etc.
    public string Rol { get; set; } = string.Empty;

    // Indica si el usuario tiene permiso para cargar información.
    public bool HabilitaCarga { get; set; }

    // Indica si el usuario tiene permiso para modificar información.
    public bool HabilitaModificacion { get; set; }

    // El SUPER_USUARIO puede cargar información de cualquier entidad.
    public bool EsSuperUsuario =>
        string.Equals(Rol, "SUPER_USUARIO", StringComparison.OrdinalIgnoreCase);
}