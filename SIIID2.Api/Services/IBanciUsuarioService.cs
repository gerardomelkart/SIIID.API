using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IBanciUsuarioService
{
    Task<List<BanciUsuarioDetalle>> ObtenerUsuariosAsync(int idAdministrador, bool incluirInactivos);
    Task<BanciUsuarioDetalleResponse> ObtenerDetalleAsync(int idAdministrador, int idUsuario);
    Task<UsuarioOperacionResponse> CrearAsync(int idAdministrador, CrearUsuarioBanciRequest request);
    Task<UsuarioOperacionResponse> EditarAsync(int idAdministrador, int idUsuario, EditarUsuarioBanciRequest request);
    Task<UsuarioOperacionResponse> DesactivarAsync(int idAdministrador, int idUsuario);
    Task<UsuarioOperacionResponse> ReactivarAsync(int idAdministrador, int idUsuario, ReactivarUsuarioBanciRequest request);
    Task<UsuarioOperacionResponse> ActualizarPermisosAsync(int idAdministrador, int idUsuario, ActualizarPermisosBanciRequest request);
    Task<UsuarioOperacionResponse> ActualizarPermisosGlobalesAsync(int idAdministrador, PermisosGlobalesBanciRequest request);
}
