using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ISemanalCargaRepository
{
    Task<UsuarioCargaInfo?> ObtenerUsuarioCargaAsync(int idUsuario);
    Task<SemanalCargaAcuseInfo?> ObtenerCargaParaAcuseAsync(string codigoReferencia);
    Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseAsync(long idSemanalCarga);
    Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseConfirmadoAsync(long idSemanalCarga);
    Task<SemanalDatosComparacion> ObtenerDatosComparacionAsync(int idEntidadFederativa, int mesCorte, int anioCorte);
    Task<long> GuardarIntentoCargaAsync(SemanalCargaPersistencia carga);
    Task<ConfirmarCargaResponse> ConfirmarCargaAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion);
}