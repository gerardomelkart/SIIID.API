using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IUsuarioService
{
    Task<CrearUsuarioResponse> CrearUsuarioAsync(CrearUsuarioRequest request, int idUsuarioAlta);
}