using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IUsuarioService
{
    Task<List<UsuarioListadoItem>> ObtenerUsuariosAsync(bool incluirInactivos);

    Task<UsuarioDetalleResponse> ObtenerUsuarioDetalleAsync(int idUsuario);

    Task<CrearUsuarioResponse> CrearUsuarioAsync(CrearUsuarioRequest request, int idUsuarioAlta);

    Task<UsuarioOperacionResponse> EditarUsuarioAsync(int idUsuario, EditarUsuarioRequest request, int idUsuarioModificacion);

    Task<UsuarioOperacionResponse> DesactivarUsuarioAsync(int idUsuario, int idUsuarioModificacion);

    Task<UsuarioOperacionResponse> ActualizarPermisosGlobalesAsync(PermisosGlobalesUsuariosRequest request, int idUsuarioModificacion);

    Task<UsuarioOperacionResponse> ReactivarUsuarioAsync(int idUsuario, ReactivarUsuarioRequest request, int idUsuarioModificacion);

    Task<UsuarioOperacionResponse> ValidarSuperUsuarioAsync(int idUsuario);
}