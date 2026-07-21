using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ISemanalCargaRepository
{
    Task<UsuarioCargaInfo?> ObtenerUsuarioCargaAsync(int idUsuario);
    Task<SemanalCargaExistenteInfo?> ObtenerCargaActivaAsync(int idEntidadFederativa, SemanalPeriodoCarga periodo);
    Task<long> GuardarIntentoCargaAsync(SemanalCargaPersistencia carga);
    Task<ConfirmarCargaResponse> ConfirmarCargaAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion);
}
