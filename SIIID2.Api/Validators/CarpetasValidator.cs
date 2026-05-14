using System.Globalization;
using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

// Validador específico para el archivo de carpetas/expedientes.
// Aquí viven las reglas propias de carpetas, no las reglas generales de carga.
public class CarpetasValidator : IArchivoCargaValidator
{
    public string NombreArchivo => "carpetas";
    // Todas estas columnas deben existir en el archivo.
    // Que la columna exista no significa que su valor deba venir lleno.
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
        // Si el archivo no tiene registros, se devuelve error general del archivo.
        if (filas.Count == 0)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = NombreArchivo,
                Fila = null,
                Columna = "",
                Campo = "",
                Valor = null,
                Mensaje = "El archivo de carpetas no contiene registros para validar."
            });

            return errores;
        }
        // Primero se valida estructura: columnas esperadas.
        ValidarColumnasObligatorias(filas, errores);
        // Si faltan columnas, no continuamos con validaciones de valores.
        if (errores.Count > 0) 
        {
            return errores;
        }
        // Validación de duplicidad dentro del mismo archivo.
        ValidarDuplicidadIdCi(filas, errores);
        // Validación campo por campo.
        foreach (var fila in filas)
        {
            // Valores obligatorios.
            ValidarTextoObligatorio(fila, "id_ci", errores, 250);
            ValidarTextoObligatorio(fila, "ntra_ci", errores, 250);
            ValidarFechaInicioObligatoria(fila, "fha_de_ini", errores);
            // Valores opcionales: la columna debe existir, pero el valor puede venir vacío.
            ValidarHoraOpcional(fila, "hra_de_ini", errores);
            ValidarTextoOpcional(fila, "rmen_de_hchos", errores);
        }
        return errores;
    }
    private void ValidarColumnasObligatorias(List<ArchivoFila> filas, List<CargaValidacionError> errores)
    {
        // Junta todas las columnas encontradas en el archivo.
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
                    Mensaje = $"El archivo de carpetas no contiene la columna obligatoria \"{columna}\"."
                });
            }
        }
    }
    private void ValidarDuplicidadIdCi(List<ArchivoFila> filas, List<CargaValidacionError> errores)
    {
        // Agrupa por ID_CI y detecta los identificadores repetidos.
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
                    $"El identificador único de carpeta ID_CI \"{grupo.Key}\" está duplicado en el archivo. Filas: {filasDuplicadas}.");
            }
        }
    }
    private void ValidarTextoObligatorio(
        ArchivoFila fila,
        string columna,
        List<CargaValidacionError> errores,
        int longitudMaxima)
    {
        var valor = ObtenerValor(fila, columna);

        if (string.IsNullOrWhiteSpace(valor))
        {
            AgregarError(errores, fila, columna, $"El campo {columna} es obligatorio.");
            return;
        }

        if (valor.Length > longitudMaxima)
        {
            AgregarError(
                errores,
                fila,
                columna,
                $"El campo {columna} excede la longitud máxima permitida de {longitudMaxima} caracteres.");
        }
    }
    private void ValidarTextoOpcional(ArchivoFila fila, string columna, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, columna);

        // Por ahora RMEN_DE_HCHOS puede venir vacío y no limitamos longitud porque va a TEXT.
        if (string.IsNullOrWhiteSpace(valor))
        {
            return;
        }
    }
    private void ValidarFechaInicioObligatoria(ArchivoFila fila, string columna, List<CargaValidacionError> errores)
    {
        var valor = ObtenerValor(fila, columna);
        if (string.IsNullOrWhiteSpace(valor))
        {
            AgregarError(errores, fila, columna, $"El campo {columna} es obligatorio.");
            return;
        }
        // Se intenta convertir la fecha aunque venga en formatos distintos de Excel.
        if (!IntentarConvertirFecha(valor, out var fechaInicio))
        {
            AgregarError(errores, fila, columna, $"El campo {columna} no pudo interpretarse como una fecha válida.");
            return;
        }
        // Para carga nueva, la fecha debe pertenecer al mes inmediato anterior.
        ValidarMesInmediatoAnterior(fila, columna, fechaInicio, errores);
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
            AgregarError(errores, fila, columna, $"El campo {columna} debe corresponder al mes inmediato anterior a la carga. " +
                $"Periodo esperado: {mesInmediatoAnterior:yyyy-MM}.");
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
        // Si viene llena, debe poder interpretarse como hora.
        if (!IntentarConvertirHora(valor, out _))
        {
            AgregarError(errores, fila, columna, $"El campo {columna} no pudo interpretarse como una hora válida.");
        }
    }
    private string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }
    private void AgregarError(List<CargaValidacionError> errores, ArchivoFila fila, string columna, string mensaje)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        errores.Add(new CargaValidacionError
        {
            Archivo = NombreArchivo,
            Fila = fila.NumeroFila,
            Columna = columna,
            Campo = columna,
            Valor = valor,
            Mensaje = mensaje
        });
    }
    private bool IntentarConvertirFecha(string valor, out DateTime fecha)
    {
        fecha = default;
        valor = valor.Trim();
        var fechaCarga = DateTime.Today;
        var mesInmediatoAnterior = fechaCarga.AddMonths(-1);
        // Formatos comunes que pueden llegar desde Excel, CSV o captura manual.
        var formatos = new[]
        {
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "dd/MM/yyyy",
            "d/M/yyyy",
            "dd-MM-yyyy",
            "d-M-yyyy",
            "MM/dd/yyyy",
            "M/d/yyyy",
            "MM-dd-yyyy",
            "M-d-yyyy",
            "yyyyMMdd"
        };
        var posiblesFechas = new List<DateTime>();
        foreach (var formato in formatos)
        {
            if (DateTime.TryParseExact(valor, formato, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaParseada))
            {
                posiblesFechas.Add(fechaParseada.Date);
            }
        }
        // Cultura mexicana: útil para fechas tipo dd/MM/yyyy.
        if (DateTime.TryParse(valor, new CultureInfo("es-MX"), DateTimeStyles.None, out var fechaMx))
        {
            posiblesFechas.Add(fechaMx.Date);
        }
        // Cultura en-US: útil cuando Excel manda fechas tipo M/d/yyyy.
        if (DateTime.TryParse(valor, new CultureInfo("en-US"), DateTimeStyles.None, out var fechaUs))
        {
            posiblesFechas.Add(fechaUs.Date);
        }

        // Número serial de Excel. Ejemplo: 45383.
        if (double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeroExcel))
        {
            if (numeroExcel > 0 && numeroExcel < 60000)
            {
                try
                {
                    var fechaExcel = DateTime.FromOADate(numeroExcel).Date;
                    posiblesFechas.Add(fechaExcel);
                }
                catch
                {
                    // Si no se puede convertir, se ignora y se sigue con otros intentos.
                }
            }
        }

        posiblesFechas = posiblesFechas.Distinct().ToList();

        if (posiblesFechas.Count == 0) 
        {
            return false;
        }
        // Si hay fechas ambiguas, se prefiere la que caiga en el mes inmediato anterior.
        var fechaMesAnterior = posiblesFechas.FirstOrDefault(f => f.Year == mesInmediatoAnterior.Year && f.Month == mesInmediatoAnterior.Month);
        if (fechaMesAnterior != default)
        {
            fecha = fechaMesAnterior;
            return true;
        }
        // Si ninguna cae en el periodo esperado, se devuelve la primera.
        // Después ValidarMesInmediatoAnterior marcará el error correspondiente.
        fecha = posiblesFechas.First();
        return true;
    }
    private bool IntentarConvertirHora(string valor, out TimeSpan hora)
    {
        hora = default;
        valor = valor.Trim();
        // Formatos comunes de hora. Se aceptan horas con uno o dos dígitos.
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
        // Intento general por si el formato no coincide exactamente con los anteriores.
        if (TimeSpan.TryParse(valor, CultureInfo.InvariantCulture, out var horaGeneral))
        {
            hora = horaGeneral;
            return true;
        }
        // Hora como decimal de Excel: 0.5 = 12:00:00, 0.25 = 06:00:00.
        if (double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeroExcel))
        {
            if (numeroExcel >= 0 && numeroExcel < 1)
            {
                hora = TimeSpan.FromDays(numeroExcel);
                return true;
            }
        }
        // Cultura mexicana por si el decimal viene con coma.
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
}
