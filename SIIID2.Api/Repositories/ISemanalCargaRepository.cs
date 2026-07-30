using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ISemanalCargaRepository
{
    Task<UsuarioCargaInfo?> ObtenerUsuarioCargaAsync(int idUsuario);
    Task<SemanalCargaAcuseInfo?> ObtenerCargaParaAcuseAsync(string codigoReferencia);
    Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseAsync(long idSemanalCarga);
    Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseConfirmadoAsync(long idSemanalCarga);
    Task<SemanalDatosComparacion> ObtenerDatosComparacionAsync(int idEntidadFederativa, int mesCorte, int anioCorte);
    Task<SemanalSemanaEstadoInfo> ObtenerEstadoSemanaAsync(int idEntidadFederativa, int anioSemana, int numeroSemana);
    Task<List<SemanalCargaBloqueConfirmado>> ObtenerBloquesConfirmadosAsync(int idEntidadFederativa, DateTime fechaInicio, DateTime fechaFin);
    Task<List<SemanalCargaBloquePendiente>> ObtenerBloquesPendientesAsync(int idEntidadFederativa, DateTime fechaInicio, DateTime fechaFin);
    Task<SemanalCargaDiferenciasInfo?> ObtenerCargaParaDiferenciasAsync(string codigoReferencia);
    Task<long> GuardarIntentoCargaAsync(SemanalCargaPersistencia carga);
    Task<ConfirmarCargaResponse> ConfirmarCargaAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion);
    Task<ConfirmarCargaResponse> AprobarCargaPendienteAsync(string codigoReferencia, int idUsuarioAprobacion);
    Task<ConfirmarCargaResponse> RechazarCargaPendienteAsync(string codigoReferencia, int idUsuarioRechazo, string motivo);
}