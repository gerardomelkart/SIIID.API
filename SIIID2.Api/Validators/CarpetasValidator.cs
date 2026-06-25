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

    public IReadOnlyCollection<string> ColumnasObligatorias => _columnasObligatorias;

    public List<CargaValidacionError> Validar(List<ArchivoFila> filas)
    {
        return Validar(filas, validarMesInmediatoAnterior: true);
    }

    public List<CargaValidacionError> Validar(List<ArchivoFila> filas, bool validarMesInmediatoAnterior)
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

            ValidarFechaInicioObligatoria(fila, "fha_de_ini", errores, validarMesInmediatoAnterior);

            // La hora puede venir vacía, pero si viene debe ser válida.
            ValidarHoraOpcional(fila, "hra_de_ini", errores);

            // El resumen puede venir vacío.
            ValidarTextoOpcional(fila, "rmen_de_hchos", errores);
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
                    Codigo = "CARPETAS_COLUMNA_OBLIGATORIA_NO_ENCONTRADA",
                    DescripcionResumen = "Columna obligatoria no encontrada",
                    Mensaje = $"El archivo de carpetas no contiene la columna obligatoria \"{columna}\"."
                });
            }
        }
    }

    private void ValidarDuplicidadIdCi(List<ArchivoFila> filas, List<CargaValidacionError> errores)
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

    private void ValidarTextoObligatorio(ArchivoFila fila, string columna, List<CargaValidacionError> errores, int longitudMaxima, string codigo, string descripcionResumen)
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

    private void ValidarTextoOpcional(ArchivoFila fila, string columna, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, columna);

        if (string.IsNullOrWhiteSpace(valor))
        {
            return;
        }  
        // RMEN_DE_HCHOS puede venir lleno o vacío.
        // No se valida longitud porque en la tabla destino será TEXT.
    }

    private void ValidarFechaInicioObligatoria(ArchivoFila fila, string columna, List<CargaValidacionError> errores, bool validarMesInmediatoAnterior)
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

        // Esta regla solo aplica para carga normal.
        // En actualización, el usuario puede seleccionar un corte anterior.
        if (validarMesInmediatoAnterior)
        {
            ValidarMesInmediatoAnterior(fila, columna, fechaInicio, errores);
        }
    }

    private void ValidarMesInmediatoAnterior(ArchivoFila fila, string columna, DateTime fechaInicio, List<CargaValidacionError> errores)
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

    private void ValidarHoraOpcional(ArchivoFila fila, string columna, List<CargaValidacionError> errores)
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

        // En los archivos de fiscalías, las fechas textuales se interpretan en formato mexicano.
        // Ejemplo:
        // 01/04/2026 = 1 de abril de 2026
        //
        // No usamos formatos MM/dd/yyyy para evitar interpretar mal fechas ambiguas.
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

        // Intento general con cultura mexicana.
        if (DateTime.TryParse(
                valor,
                new CultureInfo("es-MX"),
                DateTimeStyles.None,
                out var fechaMx))
        {
            fecha = fechaMx.Date;
            return true;
        }

        // Soporte para fechas como número serial de Excel.
        // Ejemplo: 45300, 45678, etc.
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
                    // Si no se puede convertir, se ignora y se continúa.
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
}