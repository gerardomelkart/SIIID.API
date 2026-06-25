using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IInformeRepository
{
    // Obtiene la última carga o actualización confirmada por entidad y periodo.
    Task<List<InformeEnvioItem>> ObtenerEnviosAsync( bool esSuperUsuario, int? idEntidadFederativaUsuario, int? idEntidadFederativa, int? mesCorte, int? anioCorte);

    // Obtiene la carga confirmada usada para descargar archivos del informe.
    Task<InformeArchivoCargaInfo?> ObtenerCargaConfirmadaParaArchivosAsync(string codigoReferencia);

    // Reconstruye carpetas activas del periodo confirmado.
    Task<List<IDictionary<string, object?>>> ObtenerCarpetasConfirmadasPeriodoAsync(InformeArchivoCargaInfo carga);

    // Reconstruye delitos activos del periodo confirmado.
    Task<List<IDictionary<string, object?>>> ObtenerDelitosConfirmadosPeriodoAsync(InformeArchivoCargaInfo carga);

    // Reconstruye víctimas activas del periodo confirmado.
    Task<List<IDictionary<string, object?>>> ObtenerVictimasConfirmadasPeriodoAsync(InformeArchivoCargaInfo carga);

    // Obtiene reporte de intentos/cargas por entidad y periodo.
    // Solo lo consume SUPER_USUARIO.
    Task<List<InformeReporteCargaItem>> ObtenerReporteCargasAsync(int? idEntidadFederativa, int? mesCorte, int? anioCorte);

    // Sábanas estadísticas anuales.
    // Solo SUPER_USUARIO.
    Task<List<IDictionary<string, object?>>> ObtenerSabanaEstatalDelitosAsync(int anioCorte);

    Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalDelitosAsync(int anioCorte);

    Task<List<IDictionary<string, object?>>> ObtenerSabanaEstatalVictimasAsync(int anioCorte);

    Task<List<IDictionary<string, object?>>> ObtenerSabanaMunicipalVictimasAsync(int anioCorte);

    Task<InformeSabanaFirma> ObtenerFirmaSabanaAsync(int anioCorte);

    Task<List<IDictionary<string, object?>>> ObtenerCarpetasPendientesAsync(long idCarga);

    Task<List<IDictionary<string, object?>>> ObtenerDelitosPendientesAsync(long idCarga);

    Task<List<IDictionary<string, object?>>> ObtenerVictimasPendientesAsync(long idCarga);
}