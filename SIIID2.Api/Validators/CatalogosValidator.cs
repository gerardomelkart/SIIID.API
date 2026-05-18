using System.Globalization;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Validators;

public class CatalogosValidator
{
    private readonly ICatalogoRepository _catalogoRepository;

    public CatalogosValidator(ICatalogoRepository catalogoRepository)
    {
        _catalogoRepository = catalogoRepository;
    }

    public async Task<List<CargaValidacionError>> ValidarAsync(List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas)
    {
        var errores = new List<CargaValidacionError>();

        // Primero validamos catálogos del archivo de víctimas.
        await ValidarCatalogosVictimasAsync(filasVictimas, errores);
        // Después agregamos delitos.
        // Lo dejamos separado para no mezclar demasiadas reglas en un solo paso.
        return errores;
    }

    private async Task ValidarCatalogosVictimasAsync( List<ArchivoFila> filasVictimas, List<CargaValidacionError> errores)
    {
        // Cargamos cada catálogo una sola vez.
        var tiposVictima = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_tipo_victima", "clave");

        var tiposPersonaMoral = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_tipo_victima_moral", "clave");

        var sexos = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_sexo", "clave");

        var generos = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_genero", "clave");

        var poblacionIndigena = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_pertenece_poblacion_indigena", "clave");

        var discapacidades = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_presenta_discapacidad", "clave");

        var nacionalidades = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_nacionalidad", "clave");

        foreach (var fila in filasVictimas)
        {
            ValidarClaveCatalogoOpcional(
                fila,
                "id_tv",
                tiposVictima,
                errores,
                "VICTIMAS_ID_TV_NO_EXISTE_CATALOGO",
                "Tipo de víctima no válido según el catálogo",
                "El tipo de víctima no existe o no está activo en el catálogo.");

            ValidarIdTpmContraCatalogo(fila, tiposPersonaMoral, errores);

            ValidarClaveCatalogoOpcional(
                fila,
                "sexo",
                sexos,
                errores,
                "VICTIMAS_SEXO_NO_EXISTE_CATALOGO",
                "Sexo no válido según el catálogo",
                "El sexo no existe o no está activo en el catálogo.");

            ValidarClaveCatalogoOpcional(
                fila,
                "genero",
                generos,
                errores,
                "VICTIMAS_GENERO_NO_EXISTE_CATALOGO",
                "Género no válido según el catálogo",
                "El género no existe o no está activo en el catálogo.");

            ValidarClaveCatalogoOpcional(
                fila,
                "pob",
                poblacionIndigena,
                errores,
                "VICTIMAS_POB_NO_EXISTE_CATALOGO",
                "Población indígena no válida según el catálogo",
                "La clave de pertenencia a población indígena no existe o no está activa en el catálogo.");

            ValidarClaveCatalogoOpcional(
                fila,
                "disc",
                discapacidades,
                errores,
                "VICTIMAS_DISC_NO_EXISTE_CATALOGO",
                "Discapacidad no válida según el catálogo",
                "La clave de discapacidad no existe o no está activa en el catálogo.");

            ValidarClaveCatalogoOpcional(
                fila,
                "nacional",
                nacionalidades,
                errores,
                "VICTIMAS_NACIONAL_NO_EXISTE_CATALOGO",
                "Nacionalidad no válida según el catálogo",
                "La nacionalidad no existe o no está activa en el catálogo.");
        }
    }

    private void ValidarIdTpmContraCatalogo(ArchivoFila fila, HashSet<int> tiposPersonaMoral, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, "id_tpm");

        // Vacío o 0 se toma como sin información.
        // La regla de obligatoriedad ya la valida VictimasValidator.
        if (EsValorVacioOCero(valor))
            return;

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clave))
            return;

        if (tiposPersonaMoral.Contains(clave))
            return;

        AgregarError(
            errores,
            "victimas",
            fila,
            "id_tpm",
            "VICTIMAS_ID_TPM_NO_EXISTE_CATALOGO",
            "Tipo de persona moral no válido según el catálogo",
            "El tipo de persona moral no existe o no está activo en el catálogo.");
    }

    private void ValidarClaveCatalogoOpcional(ArchivoFila fila, string columna, HashSet<int> catalogo, List<CargaValidacionError> errores, string codigo, string descripcionResumen, string mensaje)
    {
        var valor = ObtenerValor(fila, columna);

        // Vacío o 0 se toma como sin información.
        // La obligatoriedad ya se validó antes en VictimasValidator.
        if (EsValorVacioOCero(valor))
        {
            return;
        }

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clave))
        {
            return;
        }

        if (catalogo.Contains(clave)) 
        {
            return;
        }
            
        AgregarError(errores,"victimas", fila, columna, codigo, descripcionResumen, mensaje);
    }

    private static bool EsValorVacioOCero(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return true;
        }    
        valor = valor.Trim();
        return valor.All(c => c == '0');
    }

    private static string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }

    private static void AgregarError(List<CargaValidacionError> errores, string archivo, ArchivoFila fila, string columna, string codigo, string descripcionResumen, string mensaje)
    {
        fila.Columnas.TryGetValue(columna, out var valor);

        errores.Add(new CargaValidacionError
        {
            Archivo = archivo,
            Fila = fila.NumeroFila,
            Columna = columna,
            Campo = columna,
            Valor = valor,
            Codigo = codigo,
            DescripcionResumen = descripcionResumen,
            Mensaje = mensaje
        });
    }
}