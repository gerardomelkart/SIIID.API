using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IUsuarioRepository
{
    // Obtiene el usuario autenticado con su rol, entidad y permisos de carga.
    Task<UsuarioCargaInfo?> ObtenerUsuarioCargaAsync(int idUsuario);

    // Obtiene datos del usuario para login.
    Task<UsuarioAuthInfo?> ObtenerUsuarioAuthAsync(string usuario);

    // Revisa si ya existe usuario, correo, RFC o CURP.
    Task<string?> ObtenerDuplicadoUsuarioAsync(string usuario, string correoElectronico, string rfc, string curp);

    // Obtiene el id del rol por nombre.
    Task<int?> ObtenerIdRolActivoAsync(string rol);

    // Valida que exista una entidad federativa activa.
    Task<bool> ExisteEntidadActivaAsync(int idEntidadFederativa);

    // Registra usuario y sus permisos de carga/modificación.
    Task<int> CrearUsuarioAsync(CrearUsuarioRequest request, int idRol, string passwordHash, int idUsuarioAlta);
}