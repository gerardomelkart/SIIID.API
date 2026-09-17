using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IBanciConsultaRepository
{
    Task<System.Data.DataSet> ObtenerExcelAsync(BanciConsultaFiltro filtro, int? idEntidadAlcance);
    Task<BanciConsultaOpciones> ObtenerOpcionesAsync(int? idEntidadAlcance);
    Task<BanciConsultaResultado> ConsultarAsync(BanciConsultaFiltro filtro, int? idEntidadAlcance);
    Task<BanciConsultaDetalle?> ObtenerDetalleAsync(long idCarpeta, int? idEntidadAlcance);
}
