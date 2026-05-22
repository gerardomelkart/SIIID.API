using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IUsuarioService
{
    Task<List<UsuarioListadoItem>> ObtenerUsuariosAsync(bool incluirInactivos);

    Task<UsuarioDetalleResponse> ObtenerUsuarioDetalleAsync(int idUsuario);

    Task<CrearUsuarioResponse> CrearUsuarioAsync(CrearUsuarioRequest request, int idUsuarioAlta);
}