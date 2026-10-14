using System.Globalization;

namespace SIIID2.Api.Validators;
/// <summary>Interpreta POB/DISC omitidos o con cero como No identificado en BANCI.</summary>
public static class BanciPobDiscNormalizer
{
    public static string Normalizar(string? valor)
    {
        var texto = valor?.Trim();
        // La clave 3 conserva su validación contra el catálogo activo antes de guardar.
        if(string.IsNullOrEmpty(texto)
            || (int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clave)
            && clave == 0))
            return "3";
        return texto;
    }
}
