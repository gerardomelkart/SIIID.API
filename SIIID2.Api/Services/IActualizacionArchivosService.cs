using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IActualizacionArchivosService
{
    Task<CargaValidacionResponse> ValidarActualizacionAsync(IFormCollection form, int idUsuarioCarga);

    Task<ActualizacionDiferenciasResponse> ObtenerDetalleDiferenciasAsync(string codigoReferencia, int idUsuarioConsulta, int limitePorSeccion, bool incluirResumen = true);

    Task<ConfirmarCargaResponse> ConfirmarActualizacionAsync(ConfirmarCargaRequest request, int idUsuarioConfirmacion);

    Task<ActualizacionPeriodoResponse> ConsultarPeriodoActualizacionAsync(int mesCorte, int anioCorte, int idUsuarioConsulta, int? idEntidadFederativa = null);

    Task<List<ActualizacionAnioDisponibleItem>> ObtenerPeriodosDisponiblesActualizacionAsync(int idUsuarioConsulta, int? idEntidadFederativa = null);
}