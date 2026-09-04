using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IFederalEnviosRepository
{
    Task<List<InformePeriodoItem>> ObtenerPeriodosAsync();
    Task<List<InformeEnvioItem>> ObtenerEnviosAsync(int? mesCorte, int? anioCorte);
}