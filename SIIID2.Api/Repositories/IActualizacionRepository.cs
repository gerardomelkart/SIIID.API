using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IActualizacionRepository
{
    // Confirma o rechaza una actualización validada.
    // Si acepta, aplica los cambios en tablas finales y genera históricos.
    // Si rechaza, solo actualiza estados de carga y staging.
    Task<ConfirmarCargaResponse> ConfirmarActualizacionAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion);
}