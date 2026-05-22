using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IUsuarioRepository
{
    // Obtiene el usuario autenticado con su rol, entidad y permisos de carga.
    Task<UsuarioCargaInfo?> ObtenerUsuarioCargaAsync(int idUsuario);

    // Obtiene datos del usuario para login.
    Task<UsuarioAuthInfo?> ObtenerUsuarioAuthAsync(string usuario);
}