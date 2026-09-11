namespace SIIID2.Api.Models;

public class UsuarioCargaInfo
{
    public int IdUsuario { get; set; }

    // Entidad asignada al usuario.
    // NULL representa alcance nacional para SUPER_USUARIO y CONSULTA.
    public int? IdEntidadFederativa { get; set; }

    // Rol del usuario: SUPER_USUARIO, ADMIN, USUARIO, etc.
    public string Rol { get; set; } = string.Empty;

    // Indica si el usuario tiene permiso para cargar información.
    public bool HabilitaCarga { get; set; }

    // Indica si el usuario tiene permiso para modificar información.
    public bool HabilitaModificacion { get; set; }

    public bool EsConsulta =>
        string.Equals(Rol, "CONSULTA", StringComparison.OrdinalIgnoreCase);

    // Alcance de lectura; no concede permisos de operación o administración.
    public bool TieneAlcanceNacionalConsulta => EsSuperUsuario || EsConsulta && !IdEntidadFederativa.HasValue;

    public bool PuedeConsultarEntidad(int? idEntidadFederativa) =>
        TieneAlcanceNacionalConsulta || IdEntidadFederativa.HasValue && IdEntidadFederativa == idEntidadFederativa;

    public bool PuedeConsultarOperacionSemanal(int idUsuarioCarga, int? idEntidadFederativa) =>
        EsSuperUsuario || (EsConsulta ? PuedeConsultarEntidad(idEntidadFederativa) : IdUsuario == idUsuarioCarga);

    // El SUPER_USUARIO puede cargar información de cualquier entidad.
    public bool EsSuperUsuario =>
        string.Equals(Rol, "SUPER_USUARIO", StringComparison.OrdinalIgnoreCase);
}