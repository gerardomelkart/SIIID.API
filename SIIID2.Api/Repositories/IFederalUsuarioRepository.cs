using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IFederalUsuarioRepository
{
    Task<List<FederalUsuarioDetalle>> ObtenerUsuariosAsync(bool incluirInactivos);
    Task<FederalUsuarioDetalle?> ObtenerDetalleAsync(int idUsuario);
    Task<int> GuardarAsync(string operacion, int idUsuario, FederalUsuarioDatos datos, int? idRol, string? passwordHash, int idAdministrador);
}
