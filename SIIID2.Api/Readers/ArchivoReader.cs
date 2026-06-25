using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using SIIID2.Api.Models;

namespace SIIID2.Api.Readers;

// Lector general de archivos de carga.
// Detecta la extensión del archivo y lo convierte a una lista de ArchivoFila.
public class ArchivoReader : IArchivoReader
{
    public async Task<List<ArchivoFila>> LeerAsync(IFormFile archivo)
    {
        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        // Se centraliza aquí la decisión de cómo leer cada formato.
        return extension switch
        {
            ".csv" => await LeerCsvAsync(archivo),
            ".xlsx" => await LeerExcelAsync(archivo),
            _ => throw new ArgumentException($"La extensión {extension} no es compatible para lectura.")
        };
    }

    private async Task<List<ArchivoFila>> LeerCsvAsync(IFormFile archivo)
    {
        var filas = new List<ArchivoFila>();
        await using var stream = archivo.OpenReadStream();
        // detectEncodingFromByteOrderMarks ayuda cuando el CSV trae BOM de UTF-8.
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        // Configuración flexible: no truena si encuentra campos faltantes o headers no validados.
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim
        };
        using var csv = new CsvReader(reader, config);
        // Primera lectura: encabezados.
        await csv.ReadAsync();
        csv.ReadHeader();
        // Normalizamos encabezados para aceptar ID_CI, id ci, id-ci, etc.
        var encabezados = csv.HeaderRecord?.Select(NormalizarNombreColumna).ToList() ?? new List<string>();
        // Cada registro del CSV se transforma a ArchivoFila.
        while (await csv.ReadAsync())
        {
            var numeroFila = csv.Context?.Parser?.Row ?? 0;
            var fila = new ArchivoFila
            {
                NumeroFila = numeroFila
            };
            for (var i = 0; i < encabezados.Count; i++)
            {
                var columna = encabezados[i];
                // Ignora encabezados vacíos.
                if (string.IsNullOrWhiteSpace(columna)) 
                {
                    continue;
                }
                var valor = NormalizarValorLeido(columna, csv.GetField(i));
                fila.Columnas[columna] = valor;
            }
            filas.Add(fila);
        }
        return filas;
    }

    private async Task<List<ArchivoFila>> LeerExcelAsync(IFormFile archivo)
    {
        var filas = new List<ArchivoFila>();
        await using var stream = archivo.OpenReadStream();
        // ClosedXML abre el XLSX completo y permite leer celdas por hoja/fila/columna.
        using var workbook = new XLWorkbook(stream);
        // Por ahora tomamos la primera hoja del archivo.
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet == null)
        {
            return filas;
        }
        var primeraFilaUsada = worksheet.FirstRowUsed();
        if (primeraFilaUsada == null)
        {
            return filas;
        }
        var ultimaFilaUsada = worksheet.LastRowUsed();
        var ultimaColumnaUsada = worksheet.LastColumnUsed();
        if (ultimaFilaUsada == null || ultimaColumnaUsada == null) 
        {
            return filas;
        }
        var numeroFilaEncabezado = primeraFilaUsada.RowNumber();
        var numeroUltimaFila = ultimaFilaUsada.RowNumber();
        var numeroUltimaColumna = ultimaColumnaUsada.ColumnNumber();
        var encabezados = new List<string>();
        // La primera fila usada se considera encabezado.
        for (var col = 1; col <= numeroUltimaColumna; col++)
        {
            var encabezado = worksheet.Cell(numeroFilaEncabezado, col).GetString();
            encabezados.Add(NormalizarNombreColumna(encabezado));
        }
        // Las filas posteriores al encabezado se consideran datos.
        for (var row = numeroFilaEncabezado + 1; row <= numeroUltimaFila; row++)
        {
            var fila = new ArchivoFila
            {
                NumeroFila = row
            };
            var filaVacia = true;
            for (var col = 1; col <= numeroUltimaColumna; col++)
            {
                var columna = encabezados[col - 1];
                // Ignora columnas sin encabezado.
                if (string.IsNullOrWhiteSpace(columna)) 
                {
                    continue;
                }
                // GetFormattedString respeta lo que Excel muestra al usuario.
                // Esto ayuda con fechas/horas que Excel guarda internamente como números.
                var celda = worksheet.Cell(row, col);
                var valor = NormalizarValorLeido(columna, ObtenerValorCeldaExcel(columna, celda));

                if (!string.IsNullOrWhiteSpace(valor))
                {
                    filaVacia = false;
                }

                fila.Columnas[columna] = valor;
            }
            // No se agregan filas completamente vacías.
            if (!filaVacia) 
            {
                filas.Add(fila);
            }
        }
        return filas;
    }

    private static string? ObtenerValorCeldaExcel(string columna, IXLCell celda)
    {
        // Si la celda está vacía, no regresamos texto.
        if (celda.IsEmpty())
        {
            return null;
        }

        // Para clasf_de_dto necesitamos el valor real de la celda.
        // Si Excel muestra 7.1 pero internamente trae 7.12, aquí recuperamos 7.12.
        // Si internamente trae 7.10, se conserva como 7.10.
        if (columna == "clasf_de_dto" && celda.DataType == XLDataType.Number)
        {
            var numero = celda.GetDouble();
            return numero.ToString("0.00", CultureInfo.InvariantCulture);
        }

        // Si Excel reconoce la celda como fecha/hora real,
        // la convertimos nosotros a formato mexicano estable.
        //
        // Esto evita que ClosedXML entregue fechas como "4/1/2026"
        // cuando el archivo muestra "01/04/2026".
        if (celda.DataType == XLDataType.DateTime)
        {
            var fecha = celda.GetDateTime();

            // Si trae hora diferente de 00:00:00, conservamos fecha y hora.
            if (fecha.TimeOfDay != TimeSpan.Zero)
            {
                return fecha.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            }

            return fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        // Si la celda es numérica pero Excel le aplicó formato de fecha,
        // también intentamos convertirla como fecha serial.
        //
        // Esto cubre casos donde ClosedXML no marca DataType como DateTime,
        // pero el valor sigue siendo una fecha de Excel.
        if (celda.DataType == XLDataType.Number && EsFormatoFechaExcel(celda))
        {
            var numero = celda.GetDouble();

            try
            {
                var fecha = DateTime.FromOADate(numero);

                if (fecha.TimeOfDay != TimeSpan.Zero)
                {
                    return fecha.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                }

                return fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            }
            catch
            {
                // Si no se puede convertir como fecha, cae al formato normal.
            }
        }

        // Para todo lo demás usamos el texto formateado.
        // Esto mantiene ceros, descripciones y textos tal como vienen en Excel.
        return celda.GetFormattedString();
    }

    private static bool EsFormatoFechaExcel(IXLCell celda)
    {
        var formato = celda.Style.DateFormat.Format;

        if (string.IsNullOrWhiteSpace(formato))
        {
            return false;
        }

        formato = formato.ToLowerInvariant();

        // Detecta formatos de fecha comunes en Excel.
        return formato.Contains("d") ||
               formato.Contains("m") ||
               formato.Contains("y") ||
               formato.Contains("a");
    }

    private static string NormalizarNombreColumna(string columna)
    {
        if (string.IsNullOrWhiteSpace(columna))
        {
            return string.Empty;
        }
        // 1. Quita espacios extremos, pasa a minúsculas y separa acentos.
        var texto = columna.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        // 2. Elimina acentos y marcas diacríticas.
        var caracteres = texto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();
        texto = new string(caracteres).Normalize(NormalizationForm.FormC);
        // 3. Cambia cualquier separador raro por guion bajo.
        texto = System.Text.RegularExpressions.Regex.Replace(texto, @"[^a-z0-9]+", "_");
        // 4. Evita dobles guiones bajos y limpia extremos.
        texto = System.Text.RegularExpressions.Regex.Replace(texto, @"_+", "_");
        return texto.Trim('_');
    }

    private static string? NormalizarValorLeido(string columna, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        valor = valor.Trim();

        // Si clasf_de_dto viene como texto simple con un solo punto,
        // lo formateamos a dos decimales:
        // 7.1  -> 7.10
        // 7.12 -> 7.12
        // 1.03 -> 1.03
        //
        // No tocamos claves con más de un punto:
        // 4.01.02.01 se queda igual.
        if (columna == "clasf_de_dto" &&
            valor.Contains('.') &&
            valor.IndexOf('.') == valor.LastIndexOf('.') &&
            decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var claveDecimal))
        {
            return claveDecimal.ToString("0.00", CultureInfo.InvariantCulture);
        }

        return valor;
    }

    public async Task<List<string>> LeerEncabezadosAsync(IFormFile archivo)
    {
        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();

        return extension switch
        {
            ".csv" => await LeerEncabezadosCsvAsync(archivo),
            ".xlsx" => await LeerEncabezadosExcelAsync(archivo),
            _ => throw new ArgumentException($"La extensión {extension} no es compatible para lectura.")
        };
    }

    private async Task<List<string>> LeerEncabezadosCsvAsync(IFormFile archivo)
    {
        await using var stream = archivo.OpenReadStream();

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim
        };

        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync())
        {
            return new List<string>();
        }

        csv.ReadHeader();

        return csv.HeaderRecord?
            .Select(NormalizarNombreColumna)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }

    private async Task<List<string>> LeerEncabezadosExcelAsync(IFormFile archivo)
    {
        await using var stream = archivo.OpenReadStream();

        using var workbook = new XLWorkbook(stream);

        var worksheet = workbook.Worksheets.FirstOrDefault();

        if (worksheet == null)
        {
            return new List<string>();
        }

        var primeraFilaUsada = worksheet.FirstRowUsed();
        var ultimaColumnaUsada = worksheet.LastColumnUsed();

        if (primeraFilaUsada == null || ultimaColumnaUsada == null)
        {
            return new List<string>();
        }

        var numeroFilaEncabezado = primeraFilaUsada.RowNumber();
        var numeroUltimaColumna = ultimaColumnaUsada.ColumnNumber();

        var encabezados = new List<string>();

        for (var col = 1; col <= numeroUltimaColumna; col++)
        {
            var encabezado = worksheet.Cell(numeroFilaEncabezado, col).GetString();
            var encabezadoNormalizado = NormalizarNombreColumna(encabezado);

            if (!string.IsNullOrWhiteSpace(encabezadoNormalizado))
            {
                encabezados.Add(encabezadoNormalizado);
            }
        }

        return encabezados
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
