using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface ISemanalCargaService
{
    Task<SemanalCargaValidacionResponse> ValidarArchivosAsync(SemanalCargaValidacionRequest request, int idUsuarioCarga);
    Task<SemanalCargaValidacionResponse> ValidarCargaCeroAsync(SemanalCargaCeroRequest request, int idUsuarioCarga);
    Task<SemanalSemanaDisponibilidadResponse> ValidarSemanaAsync(string tipoCarga, int anioSemana, int numeroSemana, int? idEntidadFederativa, int idUsuario);
    Task<ActualizacionDiferenciasResponse> ObtenerDiferenciasAsync(string codigoReferencia, int idUsuarioConsulta, int limitePorSeccion, bool soloMuestra = false);
    Task<ConfirmarCargaResponse> ConfirmarCargaAsync(ConfirmarCargaRequest request, int idUsuarioConfirmacion);
}