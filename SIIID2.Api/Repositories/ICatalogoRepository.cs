namespace SIIID2.Api.Repositories;

public interface ICatalogoRepository
{
    // Valida si existe una clave numérica activa dentro de una tabla catálogo.
    Task<bool> ExisteClaveNumericaAsync(string tabla, string columnaClave, int clave);

    // Valida si existe una clave de texto activa dentro de una tabla catálogo.
    Task<bool> ExisteClaveTextoAsync(string tabla, string columnaClave, string clave);
}