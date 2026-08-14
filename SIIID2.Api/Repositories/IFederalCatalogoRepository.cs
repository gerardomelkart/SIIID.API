using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface IFederalCatalogoRepository
{
    Task<FederalCatalogoResumen> ObtenerResumenAsync();
    Task<List<FederalBienJuridicoCatalogoItem>> ObtenerBienesJuridicosAsync();
    Task<List<FederalDelitoCatalogoItem>> ObtenerDelitosAsync(int? idBienJuridico);
    Task<List<FederalSubtipoDelitoCatalogoItem>> ObtenerSubtiposAsync(int? idDelito);
    Task<List<FederalModalidadDelitoCatalogoItem>> ObtenerModalidadesAsync(int? idSubtipoDelito);
}