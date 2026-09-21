using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IBanciUsuarioRepository
{
    Task<List<BanciUsuarioDetalle>> ObtenerUsuariosAsync(bool incluirInactivos);
    Task<BanciUsuarioDetalle?> ObtenerDetalleAsync(int idUsuario);
    Task<int> GuardarAsync(string operacion, int idUsuario, BanciUsuarioDatos datos, int? idRol, string? passwordHash, int idAdministrador);
}
