using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IInformeRepository
{
    // Obtiene la última carga o actualización confirmada por entidad y periodo.
    Task<List<InformeEnvioItem>> ObtenerEnviosAsync( bool esSuperUsuario, int? idEntidadFederativaUsuario, int? idEntidadFederativa, int? mesCorte, int? anioCorte);
}