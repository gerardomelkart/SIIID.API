using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface ISemanalCargaService
{
    Task<SemanalCargaValidacionResponse> ValidarArchivosAsync(SemanalCargaValidacionRequest request, int idUsuarioCarga);
    Task<SemanalSemanaActualizacionResponse> ValidarSemanaActualizacionAsync(int anioSemana, int numeroSemana, int idUsuario);
    Task<ActualizacionDiferenciasResponse> ObtenerDiferenciasAsync(string codigoReferencia, int idUsuarioConsulta, int limitePorSeccion);
    Task<ConfirmarCargaResponse> ConfirmarCargaAsync(ConfirmarCargaRequest request, int idUsuarioConfirmacion);
}