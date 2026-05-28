using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IAcuseRepository
{
    // Obtiene los datos principales de una carga o actualización
    // para generar acuses previos o confirmados.
    Task<CargaAcuseInfo?> ObtenerCargaParaAcuseAsync(string codigoReferencia);

    // Obtiene el resumen desde staging.
    // Se usa para acuse previo de carga inicial y acuse previo de actualización.
    Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseAsync(long idCarga);

    // Obtiene el resumen desde tablas finales.
    // Se usa para acuse confirmado de carga inicial.
    Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseConfirmadoAsync(long idCarga);

    // Obtiene el resumen confirmado de una actualización.
    // Se usa para el acuse final de actualización.
    Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseConfirmadoActualizacionAsync(long idCargaActualizacion);
}