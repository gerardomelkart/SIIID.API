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
        // Validamos catálogos del archivo de delitos.
        await ValidarCatalogosDelitosAsync(filasDelitos, errores);
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

        var nacionalidades = await _catalogoRepository.ObtenerClavesTextoActivasAsync("catalogo_nacionalidad", "clave");

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

            ValidarClaveCatalogoTextoOpcional(
                fila,
                "nacional",
                nacionalidades,
                errores,
                "VICTIMAS_NACIONAL_NO_EXISTE_CATALOGO",
                "Nacionalidad no válida según el catálogo",
                "La nacionalidad no existe o no está activa en el catálogo.",
                "victimas");
        }
    }

    private async Task ValidarCatalogosDelitosAsync(List<ArchivoFila> filasDelitos, List<CargaValidacionError> errores)
    {
        // Cargamos cada catálogo una sola vez.
        var formasAccion = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_forma_accion", "clave");

        var instrumentosComision = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_instrumento_comision", "clave");

        var gradosConsumacion = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_grado_consumacion", "clave");

        var modalidadesDelito = await _catalogoRepository.ObtenerClavesTextoActivasAsync("catalogo_modalidad_delito", "clave4");

        var idsEntidadesFederativas = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_entidad_federativa", "id_entidad_federativa");

        var clavesEntidadesFederativas = await _catalogoRepository.ObtenerClavesTextoActivasAsync("catalogo_entidad_federativa", "clave");

        var municipiosPorEntidad = await _catalogoRepository.ObtenerMunicipiosPorEntidadActivosAsync();

        foreach (var fila in filasDelitos)
        {
            ValidarClaveCatalogoNumericaOpcional(
                fila,
                "forma_acc",
                formasAccion,
                errores,
                "DELITOS_FORMA_ACC_NO_EXISTE_CATALOGO",
                "Clave de forma de acción del delito no válida según el catálogo",
                "La clave de forma de acción no existe o no está activa en el catálogo.");

            ValidarClaveCatalogoNumericaOpcional(
                fila,
                "emto_com_dto",
                instrumentosComision,
                errores,
                "DELITOS_EMTO_COM_DTO_NO_EXISTE_CATALOGO",
                "Clave de elemento de comisión del delito no válida según el catálogo",
                "La clave de elemento de comisión no existe o no está activa en el catálogo.");

            ValidarClaveCatalogoNumericaOpcional(
                fila,
                "grdo_cons",
                gradosConsumacion,
                errores,
                "DELITOS_GRDO_CONS_NO_EXISTE_CATALOGO",
                "Clave de grado de consumación del delito no válida según el catálogo",
                "La clave de grado de consumación no existe o no está activa en el catálogo.");

            ValidarClaveCatalogoTextoOpcional(
                fila,
                "clasf_de_dto",
                modalidadesDelito,
                errores,
                "DELITOS_CLASF_DE_DTO_NO_EXISTE_CATALOGO",
                "Clave de modalidad del delito no válida según el catálogo",
                "La clave de modalidad del delito no existe o no está activa en el catálogo.",
                "delitos");

            ValidarEntidadFederativa(
                fila,
                idsEntidadesFederativas,
                clavesEntidadesFederativas,
                errores);

            ValidarMunicipio(
                fila,
                idsEntidadesFederativas,
                clavesEntidadesFederativas,
                municipiosPorEntidad,
                errores);
        }
    }

    private void ValidarIdTpmContraCatalogo(ArchivoFila fila, HashSet<int> tiposPersonaMoral, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, "id_tpm");

        // Vacío o 0 se toma como sin información.
        // La regla de obligatoriedad ya la valida VictimasValidator.
        if (EsValorVacioOCero(valor))
        {
            return;
        }

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clave))
        {
            return;
        }

        if (tiposPersonaMoral.Contains(clave))
        {
            return;
        }    

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

    private void ValidarClaveCatalogoNumericaOpcional(ArchivoFila fila, string columna, HashSet<int> catalogo, List<CargaValidacionError> errores, string codigo, string descripcionResumen, string mensaje)
    {
        var valor = ObtenerValor(fila, columna);

        // Vacío o 0 se toma como sin información.
        // La obligatoriedad y el formato ya se validaron antes.
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

        AgregarError(errores, "delitos", fila, columna, codigo, descripcionResumen, mensaje);
    }

    private void ValidarClaveCatalogoTextoOpcional(ArchivoFila fila, string columna, HashSet<string> catalogo, List<CargaValidacionError> errores, string codigo, string descripcionResumen, string mensaje, string archivo)
    {
        var valor = ObtenerValor(fila, columna);

        // Vacío o 0 se toma como sin información.
        // La obligatoriedad ya se validó antes.
        if (EsValorVacioOCero(valor))
        {
            return;
        }

        valor = valor!.Trim();

        if (catalogo.Contains(valor))
        {
            return;
        }

        AgregarError(errores, archivo, fila, columna, codigo, descripcionResumen, mensaje);
    }

    private void ValidarCodigoPostalContraCatalogo(ArchivoFila fila, HashSet<string> codigosPostales, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, "cp");

        // CP no es obligatorio.
        if (string.IsNullOrWhiteSpace(valor))
        {
            return;
        }

        valor = valor.Trim();

        // Si viene en ceros, se toma como sin información.
        if (valor.All(c => c == '0'))
        {
            return;
        }

        // Si no es numérico, no se valida contra catálogo.
        // El CP no debe bloquear la carga por formato.
        if (!valor.All(char.IsDigit))
        {
            return;
        }

        // Excel puede quitar ceros a la izquierda.
        // Ejemplo: 01234 puede llegar como 1234.
        var codigoPostalNormalizado = valor.PadLeft(5, '0');

        // Si después de normalizar supera 5 dígitos, no es un CP utilizable.
        if (codigoPostalNormalizado.Length > 5)
        {
            return;
        }

        if (codigosPostales.Contains(codigoPostalNormalizado))
        {
            return;
        }

        AgregarError(
            errores,
            "delitos",
            fila,
            "cp",
            "DELITOS_CP_NO_EXISTE_CATALOGO",
            "Código postal no válido según el catálogo",
            "El código postal no existe o no está activo en el catálogo.");
    }

    private void ValidarEntidadFederativa(ArchivoFila fila, HashSet<int> idsEntidadesFederativas, HashSet<string> clavesEntidadesFederativas, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, "id_ent_hchos");

        // La obligatoriedad ya se valida antes en DelitosValidator.
        if (string.IsNullOrWhiteSpace(valor))
        {
            return;
        }

        valor = valor.Trim();

        // Caso 1: viene como id de tabla. Ejemplo: 9.
        if (int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idEntidad))
        {
            if (idsEntidadesFederativas.Contains(idEntidad))
            {
                return;
            }

            // Caso 2: viene como número, pero puede representar una clave con cero a la izquierda.
            // Ejemplo: 9 puede representar "09".
            var claveConDosDigitos = idEntidad.ToString("00");

            if (clavesEntidadesFederativas.Contains(claveConDosDigitos))
            {
                return;
            }
        }

        // Caso 3: viene directamente como clave. Ejemplo: "09" o "00".
        if (clavesEntidadesFederativas.Contains(valor))
        {
            return;
        }

        AgregarError(
            errores,
            "delitos",
            fila,
            "id_ent_hchos",
            "DELITOS_ID_ENT_HCHOS_NO_EXISTE_CATALOGO",
            "Clave de entidad federativa no válida según el catálogo",
            "La entidad federativa no existe o no está activa en el catálogo.");
    }

    private void ValidarMunicipio(ArchivoFila fila, HashSet<int> idsEntidadesFederativas, HashSet<string> clavesEntidadesFederativas, HashSet<string> municipiosPorEntidad, List<CargaValidacionError> errores)
    {
        var valorEntidad = ObtenerValor(fila, "id_ent_hchos");
        var valorMunicipio = ObtenerValor(fila, "id_mun_hchos");

        // La obligatoriedad ya se valida antes en DelitosValidator.
        if (string.IsNullOrWhiteSpace(valorEntidad) || string.IsNullOrWhiteSpace(valorMunicipio))
        {
            return;
        }

        if (!IntentarObtenerIdEntidad(valorEntidad, idsEntidadesFederativas, clavesEntidadesFederativas, out var idEntidad))
        {
            return;
        }

        var claveMunicipio = NormalizarClaveMunicipio(valorMunicipio);

        var llave = $"{idEntidad}|{claveMunicipio}";

        if (municipiosPorEntidad.Contains(llave))
        {
            return;
        }

        AgregarError(
            errores,
            "delitos",
            fila,
            "id_mun_hchos",
            "DELITOS_ID_MUN_HCHOS_NO_EXISTE_CATALOGO",
            "Clave de municipio no válida según el catálogo",
            "La combinación entidad federativa + municipio no existe o no está activa en el catálogo.");
    }

    private bool IntentarObtenerIdEntidad(string valorEntidad, HashSet<int> idsEntidadesFederativas, HashSet<string> clavesEntidadesFederativas, out int idEntidad)
    {
        idEntidad = default;

        valorEntidad = valorEntidad.Trim();

        if (int.TryParse(valorEntidad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idEntidadParseado))
        {
            if (idsEntidadesFederativas.Contains(idEntidadParseado))
            {
                idEntidad = idEntidadParseado;
                return true;
            }

            var claveConDosDigitos = idEntidadParseado.ToString("00");

            if (clavesEntidadesFederativas.Contains(claveConDosDigitos))
            {
                idEntidad = idEntidadParseado;
                return true;
            }
        }

        if (clavesEntidadesFederativas.Contains(valorEntidad))
        {
            if (int.TryParse(valorEntidad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idDesdeClave))
            {
                idEntidad = idDesdeClave;
                return true;
            }
        }

        return false;
    }

    private string NormalizarClaveMunicipio(string valorMunicipio)
    {
        valorMunicipio = valorMunicipio.Trim();

        if (int.TryParse(valorMunicipio, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeroMunicipio))
        {
            return numeroMunicipio.ToString("000");
        }

        return valorMunicipio;
    }
}