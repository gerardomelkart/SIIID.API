using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public interface IFederalEnviosService
{
    Task<List<InformePeriodoItem>> ObtenerPeriodosAsync(int idUsuarioConsulta);
    Task<List<InformeEnvioItem>> ObtenerEnviosAsync(int idUsuarioConsulta, int? mesCorte, int? anioCorte);
}