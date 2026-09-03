using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ISemanalCargaRepository
{
    Task<UsuarioCargaInfo?> ObtenerUsuarioCargaAsync(int idUsuario);
    Task<SemanalCargaAcuseInfo?> ObtenerCargaParaAcuseAsync(string codigoReferencia);
    Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseAsync(long idSemanalCarga, int? anioCorte = null, int? mesCorte = null);
    Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseConfirmadoAsync(long idSemanalCarga, int? anioCorte = null, int? mesCorte = null);
    Task<SemanalDatosComparacion> ObtenerDatosComparacionAsync(long idSemanalCarga, int idEntidadFederativa);
    Task<SemanalSemanaEstadoInfo> ObtenerEstadoSemanaAsync(int idEntidadFederativa, int idUsuarioCarga, int anioSemana, int numeroSemana);
    Task<List<SemanalCargaBloqueConfirmado>> ObtenerBloquesConfirmadosAsync(int idEntidadFederativa, int idDelito, DateTime fechaInicio, DateTime fechaFin);
    Task<List<SemanalCargaBloquePendiente>> ObtenerBloquesPendientesAsync(int idEntidadFederativa, int idDelito, DateTime fechaInicio, DateTime fechaFin);
    Task<SemanalCargaDiferenciasInfo?> ObtenerCargaParaDiferenciasAsync(string codigoReferencia);
    Task<long?> GuardarIntentoCargaAsync(SemanalCargaPersistencia carga);
    Task<ConfirmarCargaResponse> ConfirmarCargaAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion);
    Task<ConfirmarCargaResponse> AprobarCargaPendienteAsync(string codigoReferencia, int idUsuarioAprobacion);
    Task<ConfirmarCargaResponse> RechazarCargaPendienteAsync(string codigoReferencia, int idUsuarioRechazo, string motivo);
}