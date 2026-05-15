using System.Globalization;
using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

// Validador específico para el archivo de carpetas/expedientes.
// Aquí viven las reglas propias de carpetas, no las reglas generales de carga.
public class CarpetasValidator : IArchivoCargaValidator
{
    public string NombreArchivo => "carpetas";

    // Todas estas columnas deben existir en el archivo.
    // Algunas columnas pueden existir aunque sus valores vengan vacíos.
    private readonly string[] _columnasObligatorias =
    {
        "id_ci",
        "ntra_ci",
        "fha_de_ini",
        "hra_de_ini",
        "rmen_de_hchos"
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
                Codigo = "CARPETAS_SIN_REGISTROS",
                DescripcionResumen = "Total de registros en el archivo de expedientes",
                Mensaje = "El archivo de carpetas no contiene registros para validar."
            });

            return errores;
        }

        // Primero validamos que existan las columnas esperadas.
        ValidarColumnasObligatorias(filas, errores);

        // Si faltan columnas, ya no seguimos validando filas.
        if (errores.Count > 0)
            return errores;

        // Validamos duplicados de ID_CI dentro del archivo.
        ValidarDuplicidadIdCi(filas, errores);

        // Validamos cada fila del archivo.
        foreach (var fila in filas)
        {
            ValidarTextoObligatorio(
                fila,
                "id_ci",
                errores,
                250,
                "CARPETAS_ID_CI_SIN_INFORMACION",
                "\"ID_CI\" sin información");

            ValidarTextoObligatorio(
                fila,
                "ntra_ci",
                errores,
                250,
                "CARPETAS_NTRA_CI_SIN_INFORMACION",
                "Falta la nomenclatura del expediente");

            ValidarFechaInicioObligatoria(fila, "fha_de_ini", errores);

            // La hora puede venir vacía, pero si viene debe ser válida.
            ValidarHoraOpcional(fila, "hra_de_ini", errores);

            // El resumen puede venir vacío.
            ValidarTextoOpcional(fila, "rmen_de_hchos", errores);
        }

        return errores;
    }

    private void ValidarColumnasObligatorias(
        List<ArchivoFila> filas,
        List<CargaValidacionError> errores)
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
                    Codigo = "CARPETAS_COLUMNA_OBLIGATORIA_NO_ENCONTRADA",
                    DescripcionResumen = "Columna obligatoria no encontrada",
                    Mensaje = $"El archivo de carpetas no contiene la columna obligatoria \"{columna}\"."
                });
            }
        }
    }

    private void ValidarDuplicidadIdCi(
        List<ArchivoFila> filas,
        List<CargaValidacionError> errores)
    {
        // Agrupamos por ID_CI para encontrar carpetas repetidas.
        var gruposDuplicados = filas
            .Select(fila => new
            {
                Fila = fila,
                IdCi = ObtenerValor(fila, "id_ci")?.Trim()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.IdCi))
            .GroupBy(x => x.IdCi!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var grupo in gruposDuplicados)
        {
            var filasDuplicadas = string.Join(", ", grupo.Select(x => x.Fila.NumeroFila));

            foreach (var item in grupo)
            {
                AgregarError(
                    errores,
                    item.Fila,
                    "id_ci",
                    "CARPETAS_ID_CI_DUPLICADO",
                    "\"ID_CI\" duplicados",
                    $"El identificador único de carpeta ID_CI \"{grupo.Key}\" está duplicado en el archivo. Filas: {filasDuplicadas}.");
            }
        }
    }

    private void ValidarTextoObligatorio(
        ArchivoFila fila,
        string columna,
        List<CargaValidacionError> errores,
        int longitudMaxima,
        string codigo,
        string descripcionResumen)
    {
        var valor = ObtenerValor(fila, columna);

        // Rechaza valores vacíos o en blanco.
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

        // Valida longitud máxima para que después pueda insertarse en base.
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

    private void ValidarTextoOpcional(
        ArchivoFila fila,
        string columna,
        List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, columna);

        if (string.IsNullOrWhiteSpace(valor))
            return;

        // RMEN_DE_HCHOS puede venir lleno o vacío.
        // No se valida longitud porque en la tabla destino será TEXT.
    }

    private void ValidarFechaInicioObligatoria(
        ArchivoFila fila,
        string columna,
        List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, columna);

        // La fecha de inicio sí es obligatoria.
        if (string.IsNullOrWhiteSpace(valor))
        {
            AgregarError(
                errores,
                fila,
                columna,
                "CARPETAS_FHA_DE_INI_SIN_INFORMACION",
                "Falta la fecha de inicio",
                $"El campo {columna} es obligatorio.");

            return;
        }

        // Intentamos interpretar la fecha aunque venga con formato raro de Excel.
        if (!IntentarConvertirFecha(valor, out var fechaInicio))
        {
            AgregarError(
                errores,
                fila,
                columna,
                "CARPETAS_FHA_DE_INI_FORMATO_INCORRECTO",
                "Fecha de inicio con formato incorrecto",
                $"El campo {columna} no pudo interpretarse como una fecha válida.");

            return;
        }

        // Para carga nueva debe pertenecer al mes inmediato anterior.
        ValidarMesInmediatoAnterior(fila, columna, fechaInicio, errores);
    }

    private void ValidarMesInmediatoAnterior(
        ArchivoFila fila,
        string columna,
        DateTime fechaInicio,
        List<CargaValidacionError> errores)
    {
        var fechaCarga = DateTime.Today;
        var mesInmediatoAnterior = fechaCarga.AddMonths(-1);

        var perteneceAlMesAnterior =
            fechaInicio.Year == mesInmediatoAnterior.Year &&
            fechaInicio.Month == mesInmediatoAnterior.Month;

        if (!perteneceAlMesAnterior)
        {
            AgregarError(
                errores,
                fila,
                columna,
                "CARPETAS_FHA_DE_INI_FUERA_RANGO",
                "Fecha de inicio fuera de rango",
                $"El campo {columna} debe corresponder al mes inmediato anterior a la carga. " +
                $"Periodo esperado: {mesInmediatoAnterior:yyyy-MM}.");
        }
    }

    private void ValidarHoraOpcional(
        ArchivoFila fila,
        string columna,
        List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, columna);

        // La hora puede venir vacía.
        if (string.IsNullOrWhiteSpace(valor))
            return;

        // Si viene llena, debe poder convertirse a hora.
        if (!IntentarConvertirHora(valor, out _))
        {
            AgregarError(
                errores,
                fila,
                columna,
                "CARPETAS_HRA_DE_INI_FORMATO_INCORRECTO",
                "Hora de inicio con formato incorrecto",
                $"El campo {columna} no pudo interpretarse como una hora válida.");
        }
    }

    private bool IntentarConvertirFecha(string valor, out DateTime fecha)
    {
        fecha = default;

        valor = valor.Trim();

        var fechaCarga = DateTime.Today;
        var mesInmediatoAnterior = fechaCarga.AddMonths(-1);

        // Formatos comunes que pueden venir desde CSV o Excel.
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

        var posiblesFechas = new List<DateTime>();

        foreach (var formato in formatos)
        {
            if (DateTime.TryParseExact(
                    valor,
                    formato,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var fechaParseada))
            {
                posiblesFechas.Add(fechaParseada.Date);
            }
        }

        // Intenta interpretar como fecha con cultura mexicana.
        if (DateTime.TryParse(
                valor,
                new CultureInfo("es-MX"),
                DateTimeStyles.None,
                out var fechaMx))
        {
            posiblesFechas.Add(fechaMx.Date);
        }

        // Intenta interpretar como fecha en formato estadounidense.
        if (DateTime.TryParse(
                valor,
                new CultureInfo("en-US"),
                DateTimeStyles.None,
                out var fechaUs))
        {
            posiblesFechas.Add(fechaUs.Date);
        }

        // Soporte para fechas como número serial de Excel.
        if (double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeroExcel))
        {
            if (numeroExcel > 0 && numeroExcel < 60000)
            {
                try
                {
                    posiblesFechas.Add(DateTime.FromOADate(numeroExcel).Date);
                }
                catch
                {
                    // Si no se puede convertir, se ignora.
                }
            }
        }

        // Soporte para número serial de Excel con cultura mexicana.
        if (double.TryParse(valor, NumberStyles.Any, new CultureInfo("es-MX"), out var numeroExcelMx))
        {
            if (numeroExcelMx > 0 && numeroExcelMx < 60000)
            {
                try
                {
                    posiblesFechas.Add(DateTime.FromOADate(numeroExcelMx).Date);
                }
                catch
                {
                    // Si no se puede convertir, se ignora.
                }
            }
        }

        posiblesFechas = posiblesFechas
            .Distinct()
            .ToList();

        if (posiblesFechas.Count == 0)
            return false;

        // Si hay ambigüedad, se prefiere la fecha que cae en el mes esperado.
        var fechaMesAnterior = posiblesFechas.FirstOrDefault(f =>
            f.Year == mesInmediatoAnterior.Year &&
            f.Month == mesInmediatoAnterior.Month);

        if (fechaMesAnterior != default)
        {
            fecha = fechaMesAnterior;
            return true;
        }

        // Si ninguna cae en el mes esperado, regresamos la primera.
        // La validación de rango se encargará de marcar error.
        fecha = posiblesFechas.First();
        return true;
    }

    private bool IntentarConvertirHora(string valor, out TimeSpan hora)
    {
        hora = default;

        valor = valor.Trim();

        // Formatos comunes de hora.
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
            if (TimeSpan.TryParseExact(
                    valor,
                    formato,
                    CultureInfo.InvariantCulture,
                    out var horaParseada))
            {
                hora = horaParseada;
                return true;
            }
        }

        // Intento general.
        if (TimeSpan.TryParse(valor, CultureInfo.InvariantCulture, out var horaGeneral))
        {
            hora = horaGeneral;
            return true;
        }

        // Hora como decimal de Excel. Ejemplo: 0.5 = 12:00:00.
        if (double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeroExcel))
        {
            if (numeroExcel >= 0 && numeroExcel < 1)
            {
                hora = TimeSpan.FromDays(numeroExcel);
                return true;
            }
        }

        // Hora como decimal de Excel con cultura mexicana.
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