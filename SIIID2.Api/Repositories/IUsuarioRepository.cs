using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IUsuarioRepository
{
    // Obtiene el usuario autenticado con su rol, entidad y permisos de carga.
    Task<UsuarioCargaInfo?> ObtenerUsuarioCargaAsync(int idUsuario);

    // Obtiene datos del usuario para login.
    Task<UsuarioAuthInfo?> ObtenerUsuarioAuthAsync(string usuario);

    // Revisa si ya existe usuario, correo, RFC o CURP.
    Task<List<UsuarioValidacionError>> ObtenerDuplicadosUsuarioAsync(string usuario, string correoElectronico, string rfc, string curp);

    // Obtiene el id del rol por nombre.
    Task<int?> ObtenerIdRolActivoAsync(string rol);

    // Valida que exista una entidad federativa activa.
    Task<bool> ExisteEntidadActivaAsync(int idEntidadFederativa);

    // Registra usuario y sus permisos de carga/modificación.
    Task<int> CrearUsuarioAsync(CrearUsuarioRequest request, int idRol, string passwordHash, int idUsuarioAlta);
    // Lista usuarios para la tabla administrativa.
    Task<List<UsuarioListadoItem>> ObtenerUsuariosAsync(bool incluirInactivos);

    // Obtiene detalle completo de un usuario para edición.
    Task<UsuarioDetalle?> ObtenerUsuarioDetalleAsync(int idUsuario);

    // Revisa si existe un usuario activo por id.
    Task<bool> ExisteUsuarioActivoAsync(int idUsuario);

    // Revisa duplicados excluyendo al usuario que se está editando.
    Task<List<UsuarioValidacionError>> ObtenerDuplicadosUsuarioEdicionAsync(int idUsuario, string usuario, string correoElectronico, string rfc, string curp);

    // Edita usuario y permisos.
    Task EditarUsuarioAsync(int idUsuario, EditarUsuarioRequest request, int idRol, string? passwordHash, int idUsuarioModificacion);

    // Baja lógica de usuario.
    Task DesactivarUsuarioAsync(int idUsuario, int idUsuarioModificacion);

    // Actualiza permisos de carga/modificación para todos los usuarios activos.
    Task<int> ActualizarPermisosGlobalesAsync(bool habilitaCarga, bool habilitaModificacion);

    // Revisa si existe un usuario por id, sin importar si está activo o inactivo.
    Task<bool> ExisteUsuarioAsync(int idUsuario);

    // Reactiva usuario y permisos.
    Task ReactivarUsuarioAsync(int idUsuario, ReactivarUsuarioRequest request, int idUsuarioModificacion);

    // Obtiene contraseña y estado de cambio obligatorio de un usuario activo.
    Task<UsuarioPasswordInfo?> ObtenerUsuarioPasswordAsync(int idUsuario);

    // Obtiene únicamente la bandera para bloquear solicitudes del usuario.
    Task<bool?> ObtenerRequiereCambioPasswordAsync(int idUsuario);

    // Cambia la contraseña del propio usuario y desactiva la bandera.
    Task<bool> ActualizarPasswordPropioAsync(int idUsuario, string passwordHash);

    Task ActualizarPermisosSemanalesAsync(int idUsuario, ActualizarPermisosSemanalesRequest request, int idUsuarioModificacion);
}