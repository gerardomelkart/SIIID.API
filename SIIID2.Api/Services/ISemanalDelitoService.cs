using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface ISemanalDelitoService
{
    Task<ConfiguracionDelitosSemanalesResponse> ObtenerConfiguracionAsync(int idUsuario);
    Task<DelitosSemanalesHabilitadosResponse> ObtenerDelitosHabilitadosAsync();
    Task<ConfiguracionDelitosSemanalesResponse> GuardarConfiguracionAsync(ActualizarConfiguracionDelitosSemanalesRequest request, int idUsuario);
}