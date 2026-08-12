using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public interface ICatalogoRepository
{

    // Obtiene todas las claves numéricas activas de un catálogo.
    Task<HashSet<int>> ObtenerClavesNumericasActivasAsync(string tabla, string columnaClave);

    // Obtiene todas las claves de texto activas de un catálogo.
    Task<HashSet<string>> ObtenerClavesTextoActivasAsync(string tabla, string columnaClave);

    // Obtiene las combinaciones activas entidad + municipio.
    Task<HashSet<string>> ObtenerMunicipiosPorEntidadActivosAsync();

    // Obtiene las claves de localidades INEGI de una entidad federativa.
    Task<HashSet<string>> ObtenerClavesLocalidadesInegiPorEntidadAsync(int idEntidadFederativa);

    // Obtiene las entidades federativas activas para formularios del front.
    Task<List<EntidadFederativaCatalogoItem>> ObtenerEntidadesFederativasActivasAsync();

    // Obtiene los roles activos para formularios del front.
    Task<List<RolCatalogoItem>> ObtenerRolesActivosAsync();
}