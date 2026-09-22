using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using SIIID2.Api.Models;

namespace SIIID2.Api.Readers;

public static class BanciActualizacionReader
{
    internal static readonly string[] Campos = ["identificador", "no_banci", "id_delito", "id_vicf", "folio_rnpdno", "pro_apellido", "sdo_apellido", "nomb", "entidad_nacimiento", "estado_migratorio", "curp", "rfc", "localizado_o_no_localizado", "con_o_sin_vida", "fecha_localizacion", "voluntaria_o_fue_delito", "delito", "acciones_busqueda", "obs"];
    private static string Canonico(string texto)
    {
        var normal = new string(texto.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        normal = Regex.Replace(normal, "[^a-z0-9]+", "_").Trim('_');
        return BanciArchivoReader.AliasVictimas.GetValueOrDefault(normal, normal);
    }

    public static List<Dictionary<string, string?>> Leer(IFormFile archivo)
    {
        if (archivo == null || archivo.Length is <= 0 or > 52428800 || !Path.GetExtension(archivo.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Cargue un archivo Excel .xlsx de hasta 50 MB.");
        using var stream = archivo.OpenReadStream();
        using var libro = new XLWorkbook(stream);
        var candidatos = new List<(IXLWorksheet Hoja, int Fila, Dictionary<int, string> Mapa)>();
        foreach (var hoja in libro.Worksheets)
        {
            var ultimaColumna = hoja.LastColumnUsed(XLCellsUsedOptions.Contents)?.ColumnNumber() ?? 0;
            var ultimaFila = hoja.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 0;
            if (ultimaColumna > 200 || ultimaFila > 20200) throw new ArgumentException("El archivo excede 200 columnas o 20000 registros más encabezados.");
            for (var i = 1; i <= Math.Min(ultimaFila, 200); i++)
            {
                var mapa = Enumerable.Range(1, ultimaColumna).Select(c => (Col: c, Campo: Canonico(hoja.Cell(i, c).GetString()))).Where(x => Campos.Contains(x.Campo)).ToList();
                if (!mapa.Any(x => x.Campo is "identificador" or "no_banci" or "curp" or "folio_rnpdno") || mapa.Count < 2) continue;
                if (mapa.Select(x => x.Campo).Distinct().Count() != mapa.Count) throw new ArgumentException($"La hoja {hoja.Name}, fila {i}, contiene encabezados oficiales repetidos.");
                candidatos.Add((hoja, i, mapa.ToDictionary(x => x.Col, x => x.Campo)));
                break;
            }
        }
        if (candidatos.Count != 1) throw new ArgumentException("Debe existir una sola hoja de actualización con encabezados oficiales y un identificador (NO_BANCI, CURP o folio RNPDNO).");
        var (datos, encabezado, columnas) = candidatos[0];
        var resultado = new List<Dictionary<string, string?>>();
        var ultima = datos.LastRowUsed(XLCellsUsedOptions.Contents)!.RowNumber();
        for (var i = encabezado + 1; i <= ultima; i++)
        {
            var fila = new Dictionary<string, string?>();
            foreach (var (col, campo) in columnas)
            {
                var celda = datos.Cell(i, col);
                var valor = celda.GetFormattedString().Trim();
                if (campo == "fecha_localizacion" && celda.DataType == XLDataType.DateTime) valor = celda.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                else if (campo == "fecha_localizacion" && celda.DataType == XLDataType.Number && celda.GetDouble() is > 0 and < 100000) valor = DateTime.FromOADate(celda.GetDouble()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                fila[campo] = string.IsNullOrWhiteSpace(valor) ? null : valor;
            }
            if (fila.Values.All(string.IsNullOrWhiteSpace)) continue;
            fila["fila"] = i.ToString(CultureInfo.InvariantCulture);
            resultado.Add(fila);
        }
        if (resultado.Count is < 1 or > 20000) throw new ArgumentException("El archivo debe contener entre 1 y 20000 víctimas.");
        return resultado;
    }

    public static byte[] Plantilla()
    {
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add("Actualizacion");
        for (var i = 0; i < Campos.Length; i++) hoja.Cell(1, i + 1).Value = Campos[i];
        hoja.Row(1).Style.Font.Bold = true;
        hoja.SheetView.FreezeRows(1);
        hoja.Columns(1, Campos.Length).Width = 24;
        hoja.Columns(1, Campos.Length).Style.NumberFormat.Format = "@";
        var instrucciones = libro.Worksheets.Add("Instrucciones");
        instrucciones.Cell(1, 1).Value = "Una fila por víctima. Use NO_BANCI + ID_DELITO + ID_VICF. Puede buscar por identificador (NO_BANCI, CURP o RNPDNO); si hay varias coincidencias debe completar la llave.";
        instrucciones.Cell(2, 1).Value = "Los campos vacíos conservan el valor previo. RNPDNO debe estar informado en el registro final. CURP opcional, validada contra RENAPO si se proporciona.";
        instrucciones.Cell(3, 1).Value = "Fecha: yyyy-MM-dd. Voluntaria_o_fue_delito: 1 Voluntaria, 2 Delito, 3 No identificado. Delito es opcional y usa el catálogo Consolidado.";
        instrucciones.Column(1).Width = 110;
        instrucciones.Column(1).Style.Alignment.WrapText = true;
        instrucciones.Rows(1, 3).Height = 60;
        using var salida = new MemoryStream();
        libro.SaveAs(salida);
        return salida.ToArray();
    }
}
