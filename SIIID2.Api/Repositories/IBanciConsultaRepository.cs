using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IBanciConsultaRepository
{
    Task<System.Data.DataSet> ObtenerExcelAsync(BanciConsultaFiltro filtro, int? idEntidadAlcance, int? usuarioFederal = null);
    Task<BanciConsultaOpciones> ObtenerOpcionesAsync(int? idEntidadAlcance, int? usuarioFederal = null);
    Task<BanciConsultaResultado> ConsultarAsync(BanciConsultaFiltro filtro, int? idEntidadAlcance, int? usuarioFederal = null);
    Task<BanciConsultaDetalle?> ObtenerDetalleAsync(long idCarpeta, int? idEntidadAlcance, int? usuarioFederal = null);
}
