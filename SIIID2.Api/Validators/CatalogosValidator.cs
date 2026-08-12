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

    public async Task<List<CargaValidacionError>> ValidarCoordenadasHomicidioDolosoSemanalAsync(List<ArchivoFila> filasDelitos)
    {
        var errores = new List<CargaValidacionError>();

        foreach (var fila in filasDelitos.Where(EsHomicidioDoloso))
        {
            var valorX = ObtenerValor(fila, "coord_x")?.Trim();
            var valorY = ObtenerValor(fila, "coord_y")?.Trim();

            if (EsCoordenadaSinInformacion(valorX) || EsCoordenadaSinInformacion(valorY))
            {
                errores.Add(new CargaValidacionError
                {
                    Archivo = "delitos",
                    Fila = fila.NumeroFila,
                    Columna = "coord_x+coord_y",
                    Campo = "coord_x+coord_y",
                    Valor = $"X={valorX}, Y={valorY}",
                    Codigo = "SEMANAL_HOMICIDIO_DOLOSO_COORDENADAS_SIN_INFORMACION",
                    DescripcionResumen = "Homicidio doloso sin coordenadas",
                    Mensaje = "Para homicidio doloso los campos COORD_X y COORD_Y deben contener información."
                });

                continue;
            }

            if (!IntentarConvertirCoordenada(valorX!, out var coordX) || !IntentarConvertirCoordenada(valorY!, out var coordY))
            {
                errores.Add(new CargaValidacionError
                {
                    Archivo = "delitos",
                    Fila = fila.NumeroFila,
                    Columna = "coord_x+coord_y",
                    Campo = "coord_x+coord_y",
                    Valor = $"X={valorX}, Y={valorY}",
                    Codigo = "SEMANAL_HOMICIDIO_DOLOSO_COORDENADAS_FORMATO_INCORRECTO",
                    DescripcionResumen = "Coordenadas de homicidio doloso con formato incorrecto",
                    Mensaje = "Los campos COORD_X y COORD_Y deben contener coordenadas numéricas válidas."
                });

                continue;
            }

            var valorEntidad = ObtenerValor(fila, "id_ent_hchos")?.Trim();
            var valorMunicipio = ObtenerValor(fila, "id_mun_hchos")?.Trim();

            if (!int.TryParse(valorEntidad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idEntidadFederativa) ||
                !int.TryParse(valorMunicipio, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idMunicipio))
            {
                continue;
            }

            var corresponde = await _catalogoRepository.CoordenadasCorrespondenMunicipioAsync(
                idEntidadFederativa,
                idMunicipio,
                coordX,
                coordY);

            if (corresponde) continue;

            errores.Add(new CargaValidacionError
            {
                Archivo = "delitos",
                Fila = fila.NumeroFila,
                Columna = "coord_x+coord_y",
                Campo = "coord_x+coord_y",
                Valor = $"X={valorX}, Y={valorY}",
                Codigo = "SEMANAL_HOMICIDIO_DOLOSO_COORDENADAS_NO_CORRESPONDEN_MUNICIPIO",
                DescripcionResumen = "Coordenadas de homicidio doloso fuera del municipio",
                Mensaje = $"Las coordenadas informadas no corresponden con la entidad {valorEntidad} y el municipio {valorMunicipio}."
            });
        }

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
                "victimas",
                compararComoNumero: true);
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

    private static bool EsCoordenadaSinInformacion(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return true;
        return IntentarConvertirCoordenada(valor, out var coordenada) && coordenada == 0;
    }

    private static bool IntentarConvertirCoordenada(string valor, out decimal coordenada)
    {
        coordenada = default;

        if (string.IsNullOrWhiteSpace(valor)) return false;

        valor = valor.Trim().Replace(',', '.');

        return decimal.TryParse(
            valor,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out coordenada);
    }

    private static bool EsHomicidioDoloso(ArchivoFila fila) => string.Equals(ObtenerValor(fila, "clasf_de_dto")?.Trim(), "1.01.01", StringComparison.OrdinalIgnoreCase);

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

    private void ValidarClaveCatalogoTextoOpcional(ArchivoFila fila, string columna, HashSet<string> catalogo, List<CargaValidacionError> errores, string codigo, string descripcionResumen, string mensaje, string archivo, bool compararComoNumero = false)
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

        // Algunos catálogos tienen claves numéricas guardadas como texto con ceros a la izquierda.
        // Ejemplo:
        // Excel puede traer 9 y el catálogo tener 09.
        // También debe respetar 174 como 174.
        if (compararComoNumero && int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valorNumerico))
        {
            var existeEquivalenteNumerico = catalogo.Any(claveCatalogo =>
                int.TryParse(claveCatalogo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var claveNumerica) &&
                claveNumerica == valorNumerico);

            if (existeEquivalenteNumerico)
            {
                return;
            }
        }

        AgregarError(errores, archivo, fila, columna, codigo, descripcionResumen, mensaje);
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