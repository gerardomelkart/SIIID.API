using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IBanciConsultaService
{
    Task<(byte[] Archivo, string NombreArchivo)> DescargarExcelAsync(int idUsuario, BanciConsultaFiltro filtro);
    Task<BanciConsultaOpciones> ObtenerOpcionesAsync(int idUsuario);
    Task<BanciConsultaResultado> ConsultarAsync(int idUsuario, BanciConsultaFiltro filtro);
    Task<BanciConsultaDetalle?> ObtenerDetalleAsync(int idUsuario, long idCarpeta);
}
