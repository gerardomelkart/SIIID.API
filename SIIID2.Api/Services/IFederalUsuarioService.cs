using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IFederalUsuarioService
{
    Task<List<FederalUsuarioDetalle>> ObtenerUsuariosAsync(int idAdministrador, bool incluirInactivos);
    Task<FederalUsuarioDetalleResponse> ObtenerDetalleAsync(int idAdministrador, int idUsuario);
    Task<UsuarioOperacionResponse> CrearAsync(int idAdministrador, CrearUsuarioFederalRequest request);
    Task<UsuarioOperacionResponse> EditarAsync(int idAdministrador, int idUsuario, EditarUsuarioFederalRequest request);
    Task<UsuarioOperacionResponse> DesactivarAsync(int idAdministrador, int idUsuario);
    Task<UsuarioOperacionResponse> ReactivarAsync(int idAdministrador, int idUsuario, ReactivarUsuarioFederalRequest request);
    Task<UsuarioOperacionResponse> ActualizarPermisosAsync(int idAdministrador, int idUsuario, ActualizarPermisosFederalRequest request);
    Task<UsuarioOperacionResponse> ActualizarPermisosGlobalesAsync(int idAdministrador, PermisosGlobalesFederalRequest request);
}
