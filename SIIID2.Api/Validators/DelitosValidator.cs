using System.Globalization;
using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

public class DelitosValidator : IArchivoCargaValidator
{
    public string NombreArchivo => "delitos";

    // Todas estas columnas deben existir en el archivo.
    // Algunas columnas pueden existir aunque su valor venga vacío.
    private readonly string[] _columnasObligatorias =
    {
        "id_ci",
        "id_delito",
        "dto",
        "moda_dto",
        "forma_acc",
        "fha_de_hchos",
        "hra_de_hchos",
        "emto_com_dto",
        "grdo_cons",
        "clasf_de_dto",
        "nom_ent_hchos",
        "id_ent_hchos",
        "nom_mun_hchos",
        "id_mun_hchos",
        "nom_loc_hchos",
        "id_loc_hchos",
        "nom_col_hchos",
        "id_col_hchos",
        "cp",
        "coord_x",
        "coord_y",
        "dom_hchos"
    };

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
                Codigo = "DELITOS_SIN_REGISTROS",
                DescripcionResumen = "Total de registros en el archivo de delitos",
                Mensaje = "El archivo de delitos no contiene registros para validar."
            });

            return errores;
        }

        // Primero validamos que existan las columnas esperadas.
        ValidarColumnasObligatorias(filas, errores);

        // Si faltan columnas, ya no seguimos validando filas.
        if (errores.Count > 0)
            return errores;

        // Validamos duplicidad compuesta según la documentación del sistema anterior.
        ValidarDuplicidadCompuesta(filas, errores);

        foreach (var fila in filas)
        {
            // Identificadores base.
            ValidarTextoObligatorio(
                fila,
                "id_ci",
                errores,
                250,
                "DELITOS_ID_CI_SIN_INFORMACION",
                "\"ID_CI\" sin información");

            ValidarTextoObligatorio(
                fila,
                "id_delito",
                errores,
                250,
                "DELITOS_ID_DELITO_SIN_INFORMACION",
                "\"ID_DELITO\" sin información");

            // Descripción del delito y modalidad.
            ValidarTextoObligatorio(
                fila,
                "dto",
                errores,
                250,
                "DELITOS_DTO_SIN_INFORMACION",
                "Falta la descripción del delito");

            ValidarTextoObligatorio(
                fila,
                "moda_dto",
                errores,
                250,
                "DELITOS_MODA_DTO_SIN_INFORMACION",
                "Falta la descripción de la modalidad del delito");

            // Claves numéricas de catálogos.
            // La existencia real contra catálogo se validará después con base de datos.
            ValidarEnteroObligatorio(
                fila,
                "forma_acc",
                errores,
                "DELITOS_FORMA_ACC_SIN_INFORMACION",
                "Clave de forma de acción sin información");

            ValidarEnteroObligatorio(
                fila,
                "emto_com_dto",
                errores,
                "DELITOS_EMTO_COM_DTO_SIN_INFORMACION",
                "Clave de elemento de comisión sin información");

            ValidarEnteroObligatorio(
                fila,
                "grdo_cons",
                errores,
                "DELITOS_GRDO_CONS_SIN_INFORMACION",
                "Clave de grado de consumación sin información");

            // CLASF_DE_DTO no es entero.
            // Viene como clave tipo 7.01.02, 3.01, 5.01, etc.
            ValidarTextoObligatorio(
                fila,
                "clasf_de_dto",
                errores,
                20,
                "DELITOS_CLASF_DE_DTO_SIN_INFORMACION",
                "Clave de clasificación del delito sin información");

            // Fecha y hora de hechos.
            ValidarFechaHechosObligatoria(fila, "fha_de_hchos", errores);

            // La hora puede venir vacía, pero si viene debe ser válida.
            ValidarHoraOpcional(fila, "hra_de_hchos", errores);

            // Geografía.
            // ID_ENT_HCHOS e ID_MUN_HCHOS sí son numéricos.
            ValidarEnteroObligatorio(
                fila,
                "id_ent_hchos",
                errores,
                "DELITOS_ID_ENT_HCHOS_SIN_INFORMACION",
                "Clave de entidad federativa sin información");

            ValidarEnteroObligatorio(
                fila,
                "id_mun_hchos",
                errores,
                "DELITOS_ID_MUN_HCHOS_SIN_INFORMACION",
                "Clave de municipio sin información");

            // ID_LOC_HCHOS puede venir como 090110001.
            // Lo validamos como texto para no perder ceros a la izquierda.
            ValidarTextoObligatorio(
                fila,
                "id_loc_hchos",
                errores,
                20,
                "DELITOS_ID_LOC_HCHOS_SIN_INFORMACION",
                "Clave de localidad sin información");

            // CP no es obligatorio.
            // Si viene vacío, en ceros o inválido, no bloqueamos la carga.
            ValidarCodigoPostalOpcional(fila, "cp", errores);

            // Coordenadas obligatorias dentro de los límites de México.
            ValidarCoordenadaOpcional(
                fila,
                "coord_x",
                errores,
                minimo: -118,
                maximo: -86,
                codigoFormato: "DELITOS_COORD_X_FORMATO_INCORRECTO",
                codigoRango: "DELITOS_COORD_X_FUERA_RANGO",
                descripcionFormato: "Formato coordenada X incorrecto",
                descripcionRango: "Coordenada X fuera de rango");

            ValidarCoordenadaOpcional(
                fila,
                "coord_y",
                errores,
                minimo: 13,
                maximo: 34,
                codigoFormato: "DELITOS_COORD_Y_FORMATO_INCORRECTO",
                codigoRango: "DELITOS_COORD_Y_FUERA_RANGO",
                descripcionFormato: "Formato coordenada Y incorrecto",
                descripcionRango: "Coordenada Y fuera de rango");

            // Campos descriptivos/opcionales.
            // De momento no bloquean la carga porque lo fuerte se resolverá por claves/catálogos.
            ValidarTextoOpcional(fila, "nom_ent_hchos", errores);
            ValidarTextoOpcional(fila, "nom_mun_hchos", errores);
            ValidarTextoOpcional(fila, "nom_loc_hchos", errores);
            ValidarTextoOpcional(fila, "nom_col_hchos", errores);
            ValidarTextoOpcional(fila, "id_col_hchos", errores);
            ValidarTextoOpcional(fila, "dom_hchos", errores);
        }

        return errores;
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
                    Codigo = "DELITOS_COLUMNA_OBLIGATORIA_NO_ENCONTRADA",
                    DescripcionResumen = "Columna obligatoria no encontrada",
                    Mensaje = $"El archivo de delitos no contiene la columna obligatoria \"{columna}\"."
                });
            }
        }
    }

    private void ValidarDuplicidadCompuesta(List<ArchivoFila> filas, List<CargaValidacionError> errores)
    {
        // Regla del sistema anterior:
        // ID_CI + ID_DELITO + MODA_DTO + FORMA_ACC + GRDO_CONS + CLASF_DE_DTO
        var gruposDuplicados = filas
            .Select(fila => new
            {
                Fila = fila,
                IdCi = ObtenerValor(fila, "id_ci")?.Trim(),
                IdDelito = ObtenerValor(fila, "id_delito")?.Trim(),
                ModaDto = ObtenerValor(fila, "moda_dto")?.Trim(),
                FormaAcc = ObtenerValor(fila, "forma_acc")?.Trim(),
                GrdoCons = ObtenerValor(fila, "grdo_cons")?.Trim(),
                ClasfDeDto = ObtenerValor(fila, "clasf_de_dto")?.Trim()
            })
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.IdCi) &&
                !string.IsNullOrWhiteSpace(x.IdDelito) &&
                !string.IsNullOrWhiteSpace(x.ModaDto) &&
                !string.IsNullOrWhiteSpace(x.FormaAcc) &&
                !string.IsNullOrWhiteSpace(x.GrdoCons) &&
                !string.IsNullOrWhiteSpace(x.ClasfDeDto))
            .GroupBy(x =>
                $"{x.IdCi}|{x.IdDelito}|{x.ModaDto}|{x.FormaAcc}|{x.GrdoCons}|{x.ClasfDeDto}",
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
                    "id_ci+id_delito+moda_dto+forma_acc+grdo_cons+clasf_de_dto",
                    "DELITOS_DUPLICIDAD_COMPUESTA",
                    "Registros duplicados por combinación de campos",
                    "Existe duplicidad compuesta en el archivo de delitos. " +
                    "La combinación ID_CI + ID_DELITO + MODA_DTO + FORMA_ACC + GRDO_CONS + CLASF_DE_DTO se repite. " +
                    $"Filas: {filasDuplicadas}.");
            }
        }
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

    private void ValidarTextoOpcional(ArchivoFila fila, string columna, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, columna);

        if (string.IsNullOrWhiteSpace(valor))
            return;

        // Campo opcional. Por ahora no se valida longitud.
    }

    private void ValidarEnteroObligatorio(ArchivoFila fila, string columna, List<CargaValidacionError> errores, string codigoVacio, string descripcionVacio)
    {
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

            return;
        }

        if (!long.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            AgregarError(
                errores,
                fila,
                columna,
                $"{codigoVacio}_FORMATO_INCORRECTO",
                $"{descripcionVacio} con formato incorrecto",
                $"El campo {columna} debe ser un número entero.");
        }
    }

    private void ValidarCodigoPostalOpcional(ArchivoFila fila, string columna, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, columna);

        // El CP puede venir vacío.
        if (string.IsNullOrWhiteSpace(valor))
        {
            return;
        }    

        valor = valor.Trim();

        // Algunas entidades mandan 0, 00, 00000, etc.
        // Como CP no es obligatorio, se trata como "sin información".
        if (valor.All(c => c == '0'))
        {
            return;
        }

        // Si no tiene 5 dígitos, se ignora por ahora.
        // Después, al mapear a base, se podrá tratar como NULL.
        if (valor.Length != 5)
        {
            return;
        }

        // Si trae letras u otros caracteres, se ignora por ahora.
        if (!valor.All(char.IsDigit))
        {
            return;
        } 
        // Si llegó aquí, el CP tiene 5 dígitos y es utilizable.
    }

    private void ValidarFechaHechosObligatoria(ArchivoFila fila, string columna, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, columna);
        if (string.IsNullOrWhiteSpace(valor))
        {
            AgregarError(
                errores,
                fila,
                columna,
                "DELITOS_FHA_DE_HCHOS_SIN_INFORMACION",
                "Fecha de hechos sin información",
                $"El campo {columna} es obligatorio.");

            return;
        }

        if (!IntentarConvertirFecha(valor, out _))
        {
            AgregarError(
                errores,
                fila,
                columna,
                "DELITOS_FHA_DE_HCHOS_FORMATO_INCORRECTO",
                "Fecha de hechos con formato incorrecto",
                $"El campo {columna} no pudo interpretarse como una fecha válida.");
        }
    }

    private void ValidarHoraOpcional(ArchivoFila fila, string columna, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, columna);

        // La hora puede venir vacía.
        if (string.IsNullOrWhiteSpace(valor))
        {
            return;
        }    

        if (!IntentarConvertirHora(valor, out _))
        {
            AgregarError(
                errores,
                fila,
                columna,
                "DELITOS_HRA_DE_HCHOS_FORMATO_INCORRECTO",
                "Hora de hechos con formato incorrecto",
                $"El campo {columna} no pudo interpretarse como una hora válida.");
        }
    }

    private void ValidarCoordenadaOpcional(ArchivoFila fila, string columna, List<CargaValidacionError> errores, decimal minimo, decimal maximo, string codigoFormato, string codigoRango, string descripcionFormato, string descripcionRango)
    {
        var valor = ObtenerValor(fila, columna);
        // La coordenada puede venir vacía.
        if (string.IsNullOrWhiteSpace(valor))
        {
            return;
        } 

        valor = valor.Trim();

        // Si viene solo en ceros, lo tratamos como "sin información".
        if (valor.All(c => c == '0' || c == '.' || c == ',' || c == '-'))
        {
            return;
        } 

        if (!IntentarConvertirDecimal(valor, out var coordenada))
        {
            AgregarError(
                errores,
                fila,
                columna,
                codigoFormato,
                descripcionFormato,
                $"El campo {columna} debe ser un número decimal válido.");

            return;
        }

        if (coordenada < minimo || coordenada > maximo)
        {
            AgregarError(
                errores,
                fila,
                columna,
                codigoRango,
                descripcionRango,
                $"El campo {columna} debe estar entre {minimo} y {maximo}.");
        }
    }

    private bool IntentarConvertirFecha(string valor, out DateTime fecha)
    {
        fecha = default;

        valor = valor.Trim();

        var formatos = new[]
        {
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "yyyyMMdd",

            "dd/MM/yyyy",
            "d/M/yyyy",
            "dd-MM-yyyy",
            "d-M-yyyy",

            "MM/dd/yyyy",
            "M/d/yyyy",
            "MM-dd-yyyy",
            "M-d-yyyy"
        };

        foreach (var formato in formatos)
        {
            if (DateTime.TryParseExact(valor, formato, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaParseada))
            {
                fecha = fechaParseada.Date;
                return true;
            }
        }

        if (DateTime.TryParse(valor, new CultureInfo("es-MX"), DateTimeStyles.None, out var fechaMx))
        {
            fecha = fechaMx.Date;
            return true;
        }

        if (DateTime.TryParse(valor, new CultureInfo("en-US"), DateTimeStyles.None, out var fechaUs))
        {
            fecha = fechaUs.Date;
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

    private bool IntentarConvertirHora(string valor, out TimeSpan hora)
    {
        hora = default;

        valor = valor.Trim();

        var formatos = new[]
        {
            @"hh\:mm\:ss",
            @"h\:mm\:ss",
            @"hh\:mm",
            @"h\:mm",

            "HH:mm:ss",
            "H:mm:ss",
            "HH:mm",
            "H:mm"
        };

        foreach (var formato in formatos)
        {
            if (TimeSpan.TryParseExact(valor, formato, CultureInfo.InvariantCulture, out var horaParseada))
            {
                hora = horaParseada;
                return true;
            }
        }

        if (TimeSpan.TryParse(valor, CultureInfo.InvariantCulture, out var horaGeneral))
        {
            hora = horaGeneral;
            return true;
        }

        if (double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeroExcel))
        {
            if (numeroExcel >= 0 && numeroExcel < 1)
            {
                hora = TimeSpan.FromDays(numeroExcel);
                return true;
            }
        }

        if (double.TryParse(valor, NumberStyles.Any, new CultureInfo("es-MX"), out var numeroExcelMx))
        {
            if (numeroExcelMx >= 0 && numeroExcelMx < 1)
            {
                hora = TimeSpan.FromDays(numeroExcelMx);
                return true;
            }
        }

        return false;
    }

    private bool IntentarConvertirDecimal(string valor, out decimal numero)
    {
        numero = default;

        valor = valor.Trim();

        if (decimal.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out numero))
            return true;

        if (decimal.TryParse(valor, NumberStyles.Any, new CultureInfo("es-MX"), out numero))
            return true;

        return false;
    }

    private string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }

    private void AgregarError(
        List<CargaValidacionError> errores,
        ArchivoFila fila,
        string columna,
        string codigo,
        string descripcionResumen,
        string mensaje)
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
}