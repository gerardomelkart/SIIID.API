namespace SIIID2.Api.Repositories;

public interface ICatalogoRepository
{
    // Valida si existe una clave numérica activa dentro de una tabla catálogo.
    Task<bool> ExisteClaveNumericaAsync(string tabla, string columnaClave, int clave);
    // Valida si existe una clave de texto activa dentro de una tabla catálogo.
    Task<bool> ExisteClaveTextoAsync(string tabla, string columnaClave, string clave);
    // Obtiene todas las claves numéricas activas de un catálogo.
    Task<HashSet<int>> ObtenerClavesNumericasActivasAsync(string tabla, string columnaClave);
    // Obtiene todas las claves de texto activas de un catálogo.
    Task<HashSet<string>> ObtenerClavesTextoActivasAsync(string tabla, string columnaClave);
}