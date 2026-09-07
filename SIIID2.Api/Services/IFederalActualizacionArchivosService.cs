using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IFederalActualizacionArchivosService
{
    Task<CargaValidacionResponse> ValidarActualizacionAsync(IFormCollection form, int idUsuarioCarga);
    Task<ActualizacionDiferenciasResponse> ObtenerDiferenciasAsync(string codigoReferencia, int idUsuarioConsulta, int limitePorSeccion, bool incluirResumen = true);
    Task<ConfirmarCargaResponse> ConfirmarActualizacionAsync(ConfirmarCargaRequest request, int idUsuarioConfirmacion);
    Task<ActualizacionPeriodoResponse> ConsultarPeriodoAsync(int mesCorte, int anioCorte, int idUsuarioConsulta);
    Task<List<ActualizacionAnioDisponibleItem>> ObtenerPeriodosDisponiblesAsync(int idUsuarioConsulta);
}