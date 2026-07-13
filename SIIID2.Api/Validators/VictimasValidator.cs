using System.Globalization;
using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

public class VictimasValidator
{
    public string NombreArchivo => "victimas";

    // Todas estas columnas deben existir en el archivo.
    // Algunas pueden venir vacías según el tipo de víctima.
    private readonly string[] _columnasObligatorias =
    {
        "id_ci",
        "id_delito",
        "id_vicf",
        "id_tv",
        "id_tpm",
        "sexo",
        "genero",
        "pob",
        "disc",
        "fha_nac",
        "edad",
        "nacional"
    };

    public IReadOnlyCollection<string> ColumnasObligatorias => _columnasObligatorias;

    public List<CargaValidacionError> Validar(List<ArchivoFila> filas)
    {
        var errores = new List<CargaValidacionError>();

        // Si no hay filas, el archivo no tiene registros útiles.
        if (filas.Count == 0)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = NombreArchivo,
                Fila = null,
                Columna = "",
                Campo = "",
                Valor = null,
                Codigo = "VICTIMAS_SIN_REGISTROS",
                DescripcionResumen = "Total de registros en el archivo de víctimas",
                Mensaje = "El archivo de víctimas no contiene registros para validar."
            });

            return errores;
        }

        // Primero validamos que existan las columnas esperadas.
        ValidarColumnasObligatorias(filas, errores);

        // Si faltan columnas, ya no seguimos validando filas.
        if (errores.Count > 0)
            return errores;

        // Evita víctimas repetidas bajo el mismo delito.
        ValidarDuplicidadVictima(filas, errores);

        foreach (var fila in filas)
        {
            // Campos base obligatorios.
            ValidarTextoObligatorio(
                fila,
                "id_ci",
                errores,
                250,
                "VICTIMAS_ID_CI_SIN_INFORMACION",
                "\"ID_CI\" sin información");

            ValidarTextoObligatorio(
                fila,
                "id_delito",
                errores,
                250,
                "VICTIMAS_ID_DELITO_SIN_INFORMACION",
                "\"ID_DELITO\" sin información");

            ValidarTextoObligatorio(
                fila,
                "id_vicf",
                errores,
                250,
                "VICTIMAS_ID_VICF_SIN_INFORMACION",
                "\"ID_VICF\" sin información");

            // ID_TV define el tipo de víctima.
            // Catálogo actual:
            // 1 = Persona Física
            // 2 = Persona Moral
            // 3 = Otro
            // 4 = No identificado
            var idTvValido = ValidarEnteroObligatorio(
                fila,
                "id_tv",
                errores,
                "VICTIMAS_ID_TV_SIN_INFORMACION",
                "Tipo de víctima sin información",
                out var idTv);

            if (!idTvValido) 
            {
                continue;
            }

            if (idTv == 2 && TieneVariablesPersonaFisicaInformadas(fila))
            {
                AgregarError(
                    errores,
                    fila,
                    "id_tv",
                    "VICTIMAS_ID_TV_NO_CORRESPONDE_VARIABLES_PERSONA_FISICA",
                    "Tipo de víctima no corresponde con variables de persona física",
                    "El tipo de víctima (ID_TV) no corresponde con las variables de persona física (SEXO, GENERO, POB, DISC, FHA_NAC, EDAD y/o NACIONAL).");

                continue;
            }

            if (idTv == 1)
            {
                ValidarPersonaFisica(fila, errores);
            }
            else if (idTv == 2)
            {
                ValidarPersonaMoral(fila, errores);
            }
            else if (idTv == 3 || idTv == 4)
            {
                ValidarVictimaOtroONoIdentificado(fila, idTv, errores);
            }
            else
            {
                AgregarError(
                    errores,
                    fila,
                    "id_tv",
                    "VICTIMAS_ID_TV_NO_VALIDO",
                    "Tipo de víctima no válido",
                    "El campo id_tv debe existir en el catálogo de tipo de víctima.");
            }
        }

        return errores;
    }

    private bool TieneVariablesPersonaFisicaInformadas(ArchivoFila fila)
    {
        return TieneValorPersonaFisica(fila, "sexo") ||
               TieneValorPersonaFisica(fila, "genero") ||
               TieneValorPersonaFisica(fila, "fha_nac") ||
               TieneValorPersonaFisica(fila, "edad") ||
               TieneValorPersonaFisica(fila, "nacional") ||
               TieneValorPersonaFisica(fila, "pob", aceptaNoAplica: true) ||
               TieneValorPersonaFisica(fila, "disc", aceptaNoAplica: true);
    }

    private bool TieneValorPersonaFisica(ArchivoFila fila, string columna, bool aceptaNoAplica = false)
    {
        var valor = ObtenerValor(fila, columna);

        if (EsValorVacioOCero(valor))
        {
            return false;
        }

        valor = valor!.Trim();

        // Para POB y DISC, 4 representa No aplica.
        // No debe contarse como dato de persona física en una persona moral.
        if (aceptaNoAplica && valor == "4")
        {
            return false;
        }

        return true;
    }

    private void ValidarColumnasObligatorias(List<ArchivoFila> filas, List<CargaValidacionError> errores)
    {
        // Obtenemos todas las columnas que llegaron en el archivo.
        var columnasArchivo = filas
            .SelectMany(f => f.Columnas.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var columna in _columnasObligatorias)
        {
            if (!columnasArchivo.Contains(columna))
            {
                errores.Add(new CargaValidacionError
                {
                    Archivo = NombreArchivo,
                    Fila = 1,
                    Columna = columna,
                    Campo = columna,
                    Valor = null,
                    Codigo = "VICTIMAS_COLUMNA_OBLIGATORIA_NO_ENCONTRADA",
                    DescripcionResumen = "Columna obligatoria no encontrada",
                    Mensaje = $"El archivo de víctimas no contiene la columna obligatoria \"{columna}\"."
                });
            }
        }
    }

    private void ValidarDuplicidadVictima(List<ArchivoFila> filas, List<CargaValidacionError> errores)
    {
        // Regla base: no duplicar la misma víctima bajo el mismo delito.
        var gruposDuplicados = filas
            .Select(fila => new
            {
                Fila = fila,
                IdCi = ObtenerValor(fila, "id_ci")?.Trim(),
                IdDelito = ObtenerValor(fila, "id_delito")?.Trim(),
                IdVicf = ObtenerValor(fila, "id_vicf")?.Trim()
            })
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.IdCi) &&
                !string.IsNullOrWhiteSpace(x.IdDelito) &&
                !string.IsNullOrWhiteSpace(x.IdVicf))
            .GroupBy(x =>
                $"{x.IdCi}|{x.IdDelito}|{x.IdVicf}",
                StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var grupo in gruposDuplicados)
        {
            var filasDuplicadas = string.Join(", ", grupo.Select(x => x.Fila.NumeroFila));

            foreach (var item in grupo)
            {
                AgregarError(
                    errores,
                    item.Fila,
                    "id_ci+id_delito+id_vicf",
                    "VICTIMAS_DUPLICIDAD_VICTIMA_DELITO",
                    "Víctimas duplicadas bajo el mismo delito",
                    "Existe una víctima duplicada bajo el mismo delito. " +
                    $"La combinación ID_CI + ID_DELITO + ID_VICF se repite. Filas: {filasDuplicadas}.");
            }
        }
    }

    private void ValidarPersonaFisica(ArchivoFila fila, List<CargaValidacionError> errores)
    {
        // Para persona física, ID_TPM puede venir vacío, 0 o 6 No identificada.
        ValidarIdTpmParaPersonaFisica(fila, errores);

        // Catálogos de persona física.
        // Por ahora solo validamos que sean enteros.
        // La existencia real contra catálogo se validará después con base de datos.
        ValidarEnteroObligatorio(
            fila,
            "sexo",
            errores,
            "VICTIMAS_SEXO_SIN_INFORMACION",
            "Sexo sin información",
            out _);

        ValidarEnteroObligatorio(
            fila,
            "genero",
            errores,
            "VICTIMAS_GENERO_SIN_INFORMACION",
            "Género sin información",
            out _);

        ValidarEnteroObligatorio(
            fila,
            "pob",
            errores,
            "VICTIMAS_POB_SIN_INFORMACION",
            "Población indígena sin información",
            out _);

        ValidarEnteroObligatorio(
            fila,
            "disc",
            errores,
            "VICTIMAS_DISC_SIN_INFORMACION",
            "Discapacidad sin información",
            out _);

        ValidarEnteroObligatorio(
            fila,
            "nacional",
            errores,
            "VICTIMAS_NACIONAL_SIN_INFORMACION",
            "Nacionalidad sin información",
            out _);

        // Fecha de nacimiento opcional.
        ValidarFechaOpcional(fila, "fha_nac", errores);

        // Edad opcional.
        ValidarEdadOpcional(fila, errores);
    }

    private void ValidarIdTpmParaPersonaFisica(ArchivoFila fila, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, "id_tpm");

        // Vacío o 0 se acepta como sin información.
        if (EsValorVacioOCero(valor))
            return;

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idTpm))
        {
            AgregarError(
                errores,
                fila,
                "id_tpm",
                "VICTIMAS_FISICA_ID_TPM_FORMATO_INCORRECTO",
                "Persona física con tipo de persona moral inválido",
                "El campo id_tpm debe ser un número entero.");

            return;
        }

        // 6 = No identificada.
        // Para persona física también se acepta.
        if (idTpm == 6)
            return;

        AgregarError(
            errores,
            fila,
            "id_tpm",
            "VICTIMAS_FISICA_ID_TPM_CON_INFORMACION",
            "Persona física con tipo de persona moral",
            "Para persona física, id_tpm debe venir vacío, 0 o 6 No identificada.");
    }

    private void ValidarPersonaMoral(ArchivoFila fila, List<CargaValidacionError> errores)
    {
        // Para persona moral, ID_TPM sí es obligatorio.
        // Catálogo actual de persona moral:
        // 1 = Sociedad mercantil
        // 2 = Sociedad civil
        // 3 = Asociación civil
        // 4 = Institución gubernamental
        // 5 = Otra
        // 6 = No identificada
        ValidarEnteroObligatorio(
            fila,
            "id_tpm",
            errores,
            "VICTIMAS_ID_TPM_SIN_INFORMACION",
            "Tipo de persona moral sin información",
            out _);

        // Persona moral no debe traer datos demográficos de persona física.
        // Vacío o 0 se acepta como sin información.
        ValidarCampoVacio(
            fila,
            "sexo",
            errores,
            "VICTIMAS_MORAL_SEXO_CON_INFORMACION",
            "Persona moral con sexo informado");

        ValidarCampoVacio(
            fila,
            "genero",
            errores,
            "VICTIMAS_MORAL_GENERO_CON_INFORMACION",
            "Persona moral con género informado");

        ValidarCampoVacio(
            fila,
            "fha_nac",
            errores,
            "VICTIMAS_MORAL_FHA_NAC_CON_INFORMACION",
            "Persona moral con fecha de nacimiento informada");

        ValidarCampoVacio(
            fila,
            "edad",
            errores,
            "VICTIMAS_MORAL_EDAD_CON_INFORMACION",
            "Persona moral con edad informada");

        ValidarCampoVacio(
            fila,
            "nacional",
            errores,
            "VICTIMAS_MORAL_NACIONAL_CON_INFORMACION",
            "Persona moral con nacionalidad informada");

        // Para persona moral, población indígena y discapacidad deben venir como 4: No aplica.
        ValidarEnteroIgualA(
            fila,
            "pob",
            4,
            errores,
            "VICTIMAS_MORAL_POB_NO_APLICA_INVALIDO",
            "Persona moral con población indígena distinta a No aplica");

        ValidarEnteroIgualA(
            fila,
            "disc",
            4,
            errores,
            "VICTIMAS_MORAL_DISC_NO_APLICA_INVALIDO",
            "Persona moral con discapacidad distinta a No aplica");
    }

    private void ValidarVictimaOtroONoIdentificado(ArchivoFila fila, int idTv, List<CargaValidacionError> errores)
    {
        // Para ID_TV = 3 Otro o ID_TV = 4 No identificado,
        // no aplicamos reglas estrictas de persona física ni persona moral.
        // Solo validamos formato si algunos campos vienen llenos.

        // ID_TPM puede venir vacío, 0 o 6 No identificada.
        ValidarIdTpmParaOtroONoIdentificado(fila, idTv, errores);

        ValidarEnteroOpcional(
            fila,
            "sexo",
            errores,
            "VICTIMAS_SEXO_FORMATO_INCORRECTO",
            "Sexo con formato incorrecto");

        ValidarEnteroOpcional(
            fila,
            "genero",
            errores,
            "VICTIMAS_GENERO_FORMATO_INCORRECTO",
            "Género con formato incorrecto");

        ValidarEnteroOpcional(
            fila,
            "pob",
            errores,
            "VICTIMAS_POB_FORMATO_INCORRECTO",
            "Población indígena con formato incorrecto");

        ValidarEnteroOpcional(
            fila,
            "disc",
            errores,
            "VICTIMAS_DISC_FORMATO_INCORRECTO",
            "Discapacidad con formato incorrecto");

        ValidarEnteroOpcional(
            fila,
            "nacional",
            errores,
            "VICTIMAS_NACIONAL_FORMATO_INCORRECTO",
            "Nacionalidad con formato incorrecto");

        ValidarFechaOpcional(fila, "fha_nac", errores);

        ValidarEnteroOpcional(
            fila,
            "edad",
            errores,
            "VICTIMAS_EDAD_FORMATO_INCORRECTO",
            "Edad con formato incorrecto");
    }

    private void ValidarIdTpmParaOtroONoIdentificado(ArchivoFila fila, int idTv, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, "id_tpm");

        // Vacío o 0 se acepta como sin información.
        if (EsValorVacioOCero(valor))
        {
            return;
        }

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idTpm))
        {
            AgregarError(
                errores,
                fila,
                "id_tpm",
                "VICTIMAS_ID_TPM_FORMATO_INCORRECTO",
                "Tipo de persona moral con formato incorrecto",
                "El campo id_tpm debe ser un número entero.");

            return;
        }

        // 6 = No identificada.
        if (idTpm == 6)
        {
            return;
        }
        //3 = otro
        if (idTv == 3)
        {
            return;
        }

        // Regla general:
        // id_tv = 4 No identificado permite id_tpm = 5 Otro.
        if (idTv == 4 && idTpm == 5)
        {
            return;
        }

        AgregarError(
            errores,
            fila,
            "id_tpm",
            "VICTIMAS_ID_TPM_NO_APLICA",
            "Tipo de persona moral no aplica para este tipo de víctima",
            "Para tipo de víctima Otro, id_tpm puede venir vacío, 0, 5 Otro o 6 No identificada. Para tipo de víctima No identificado, id_tpm debe venir vacío, 0 o 6 No identificada.");
    }

    private void ValidarTextoObligatorio(ArchivoFila fila, string columna, List<CargaValidacionError> errores, int longitudMaxima, string codigo, string descripcionResumen)
    {
        var valor = ObtenerValor(fila, columna);

        if (string.IsNullOrWhiteSpace(valor))
        {
            AgregarError(
                errores,
                fila,
                columna,
                codigo,
                descripcionResumen,
                $"El campo {columna} es obligatorio.");

            return;
        }

        if (valor.Length > longitudMaxima)
        {
            AgregarError(
                errores,
                fila,
                columna,
                $"{codigo}_LONGITUD_EXCEDIDA",
                $"{descripcionResumen} con longitud excedida",
                $"El campo {columna} excede la longitud máxima permitida de {longitudMaxima} caracteres.");
        }
    }

    private bool ValidarEnteroObligatorio(ArchivoFila fila, string columna, List<CargaValidacionError> errores, string codigoVacio, string descripcionVacio, out int numero)
    {
        numero = default;

        var valor = ObtenerValor(fila, columna);

        if (string.IsNullOrWhiteSpace(valor))
        {
            AgregarError(
                errores,
                fila,
                columna,
                codigoVacio,
                descripcionVacio,
                $"El campo {columna} es obligatorio.");

            return false;
        }

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out numero))
        {
            AgregarError(
                errores,
                fila,
                columna,
                $"{codigoVacio}_FORMATO_INCORRECTO",
                $"{descripcionVacio} con formato incorrecto",
                $"El campo {columna} debe ser un número entero.");

            return false;
        }

        return true;
    }

    private void ValidarEnteroOpcional(ArchivoFila fila, string columna, List<CargaValidacionError> errores, string codigoFormato, string descripcionResumen)
    {
        var valor = ObtenerValor(fila, columna);

        // Vacío o 0 se acepta como sin información en campos opcionales.
        if (EsValorVacioOCero(valor))
            return;

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            AgregarError(
                errores,
                fila,
                columna,
                codigoFormato,
                descripcionResumen,
                $"El campo {columna} debe ser un número entero.");
        }
    }

    private void ValidarEnteroIgualA(ArchivoFila fila, string columna, int esperado, List<CargaValidacionError> errores, string codigo, string descripcionResumen)
    {
        var valor = ObtenerValor(fila, columna);

        if (string.IsNullOrWhiteSpace(valor))
        {
            AgregarError(
                errores,
                fila,
                columna,
                codigo,
                descripcionResumen,
                $"El campo {columna} debe venir con el valor {esperado}.");

            return;
        }

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero))
        {
            AgregarError(
                errores,
                fila,
                columna,
                $"{codigo}_FORMATO_INCORRECTO",
                $"{descripcionResumen} con formato incorrecto",
                $"El campo {columna} debe ser un número entero.");

            return;
        }

        if (numero != esperado)
        {
            AgregarError(
                errores,
                fila,
                columna,
                codigo,
                descripcionResumen,
                $"El campo {columna} debe venir con el valor {esperado}.");
        }
    }

    private void ValidarCampoVacio(ArchivoFila fila, string columna, List<CargaValidacionError> errores, string codigo, string descripcionResumen)
    {
        var valor = ObtenerValor(fila, columna);

        // Vacío o 0 se toma como sin información.
        if (EsValorVacioOCero(valor))
        {
            return;
        }
            

        AgregarError(
            errores,
            fila,
            columna,
            codigo,
            descripcionResumen,
            $"El campo {columna} debe venir vacío o en 0 para este tipo de víctima.");
    }

    private void ValidarFechaOpcional(ArchivoFila fila, string columna, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, columna);

        // Vacío o 0 se acepta como sin información.
        if (EsValorVacioOCero(valor))
        {
            return;
        }   

        if (!IntentarConvertirFecha(valor, out _))
        {
            AgregarError(
                errores,
                fila,
                columna,
                "VICTIMAS_FHA_NAC_FORMATO_INCORRECTO",
                "Fecha de nacimiento con formato incorrecto",
                $"El campo {columna} no pudo interpretarse como una fecha válida.");
        }
    }

    private bool EsValorVacioOCero(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return true;
        }   

        valor = valor.Trim();

        // Acepta 0, 00, 000, etc. como sin información.
        return valor.All(c => c == '0');
    }

    private bool IntentarConvertirFecha(string valor, out DateTime fecha)
    {
        fecha = default;

        valor = valor.Trim();

        // Fechas textuales en formato mexicano.
        // Ejemplo: 04/02/2026 = 4 de febrero de 2026.
        // No usamos MM/dd/yyyy ni cultura en-US para evitar ambigüedad.
        var formatos = new[]
        {
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyyMMdd"
    };

        foreach (var formato in formatos)
        {
            if (DateTime.TryParseExact(
                    valor,
                    formato,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var fechaParseada))
            {
                fecha = fechaParseada.Date;
                return true;
            }
        }

        if (DateTime.TryParse(
                valor,
                new CultureInfo("es-MX"),
                DateTimeStyles.None,
                out var fechaMx))
        {
            fecha = fechaMx.Date;
            return true;
        }

        if (double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeroExcel))
        {
            if (numeroExcel > 0 && numeroExcel < 60000)
            {
                try
                {
                    fecha = DateTime.FromOADate(numeroExcel).Date;
                    return true;
                }
                catch
                {
                    // Si no se puede convertir, se ignora.
                }
            }
        }

        if (double.TryParse(valor, NumberStyles.Any, new CultureInfo("es-MX"), out var numeroExcelMx))
        {
            if (numeroExcelMx > 0 && numeroExcelMx < 60000)
            {
                try
                {
                    fecha = DateTime.FromOADate(numeroExcelMx).Date;
                    return true;
                }
                catch
                {
                    // Si no se puede convertir, se ignora.
                }
            }
        }

        return false;
    }

    private string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }

    private void AgregarError(List<CargaValidacionError> errores, ArchivoFila fila, string columna, string codigo, string descripcionResumen, string mensaje)
    {
        fila.Columnas.TryGetValue(columna, out var valor);

        errores.Add(new CargaValidacionError
        {
            Archivo = NombreArchivo,
            Fila = fila.NumeroFila,
            Columna = columna,
            Campo = columna,
            Valor = valor,
            Codigo = codigo,
            DescripcionResumen = descripcionResumen,
            Mensaje = mensaje
        });
    }

    private void ValidarEdadOpcional(ArchivoFila fila, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, "edad");

        // Vacío o 0 se toma como sin información.
        if (EsValorVacioOCero(valor))
        {
            return;
        }

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var edad))
        {
            AgregarError(
                errores,
                fila,
                "edad",
                "VICTIMAS_EDAD_FORMATO_INCORRECTO",
                "Edad con formato incorrecto",
                "El campo edad debe ser un número entero.");

            return;
        }

        // 999 se acepta como No identificado.
        if (edad == 999)
        {
            return;
        }

        if (edad < 0 || edad > 120)
        {
            AgregarError(
                errores,
                fila,
                "edad",
                "VICTIMAS_EDAD_FUERA_RANGO",
                "Edad fuera de rango",
                "El campo edad debe estar entre 0 y 120, o venir como 999 para No identificado.");
        }
    }
}