using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ISemanalDelitoRepository
{
    Task<bool> PuedeAdministrarDelitosAsync(int idUsuario);
    Task<List<ConfiguracionDelitoSemanalItem>> ObtenerConfiguracionAsync();
    Task GuardarConfiguracionAsync(List<ConfiguracionDelitoSemanalItem> delitos, int idUsuarioModificacion);
}