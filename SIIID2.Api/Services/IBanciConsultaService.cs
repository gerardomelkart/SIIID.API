using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IBanciConsultaService
{
    Task<BanciConsultaOpciones> ObtenerOpcionesAsync(int idUsuario);
    Task<BanciConsultaResultado> ConsultarAsync(int idUsuario, BanciConsultaFiltro filtro);
    Task<BanciConsultaDetalle?> ObtenerDetalleAsync(int idUsuario, long idCarpeta);
}
