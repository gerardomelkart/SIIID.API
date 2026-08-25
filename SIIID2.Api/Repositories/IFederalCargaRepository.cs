using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IFederalCargaRepository
{
    Task<UsuarioCargaInfo?> ObtenerUsuarioCargaAsync(int idUsuario);
    Task<bool> ExisteCargaConfirmadaAsync(int mesCorte, int anioCorte);
    Task<CargaPendienteInfo?> ObtenerCodigoCargaPendienteAsync(int mesCorte, int anioCorte);
    Task<long> GuardarIntentoCargaAsync(int idUsuarioCarga, string codigoReferencia, int mesCorte, int anioCorte, int totalCarpetas, int totalDelitos, int totalVictimas, string estado, string? mensajeError, List<CargaValidacionError> advertencias, List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas);
    Task<ConfirmarCargaResponse> ConfirmarCargaAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion);
}
