using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ISemanalDelitoRepository
{
    Task<bool> PuedeAdministrarDelitosAsync(int idUsuario);
    Task<List<ConfiguracionModalidadSemanalItem>> ObtenerConfiguracionAsync();
    Task GuardarConfiguracionAsync(List<ConfiguracionModalidadSemanalItem> modalidades, int idUsuarioModificacion);
}