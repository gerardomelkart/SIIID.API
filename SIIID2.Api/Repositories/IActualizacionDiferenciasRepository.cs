using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IActualizacionDiferenciasRepository
{
    // Obtiene el detalle de diferencias de una actualización pendiente.
    // Se usa para que el front muestre registros nuevos, modificados y eliminados
    // antes de confirmar la actualización.
    Task<ActualizacionDiferenciasResponse?> ObtenerDetalleDiferenciasActualizacionAsync(string codigoReferencia, int? idEntidadFederativaUsuario, bool esSuperUsuario);
}