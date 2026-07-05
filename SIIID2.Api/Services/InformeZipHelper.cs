using ClosedXML.Excel;
using System.IO.Compression;

namespace SIIID2.Api.Services;

public static class InformeZipHelper
{
    public static byte[] Generar(List<IDictionary<string, object?>> carpetas, List<IDictionary<string, object?>> delitos, List<IDictionary<string, object?>> victimas)
    {
        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AgregarExcel(archive, "carpetas.xlsx", "carpetas", carpetas);
            AgregarExcel(archive, "delitos.xlsx", "delitos", delitos);
            AgregarExcel(archive, "victimas.xlsx", "victimas", victimas);
        }

        return zipStream.ToArray();
    }

    private static void AgregarExcel(ZipArchive archive, string nombreArchivo, string nombreHoja, List<IDictionary<string, object?>> filas)
    {
        var entry = archive.CreateEntry(nombreArchivo, CompressionLevel.Fastest);
        using var entryStream = entry.Open();
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(nombreHoja);

        if (filas.Count == 0)
        {
            worksheet.Cell(1, 1).Value = "Sin registros";
            workbook.SaveAs(entryStream);
            return;
        }

        var columnas = filas.First().Keys.ToList();

        for (var columna = 0; columna < columnas.Count; columna++)
        {
            worksheet.Cell(1, columna + 1).Value = columnas[columna];
            worksheet.Cell(1, columna + 1).Style.Font.Bold = true;

            if (columnas[columna] == "Clave_Ent") worksheet.Column(columna + 1).Style.NumberFormat.Format = "@";
        }

        for (var fila = 0; fila < filas.Count; fila++)
        {
            for (var columna = 0; columna < columnas.Count; columna++)
            {
                var nombreColumna = columnas[columna];
                var valor = filas[fila].TryGetValue(nombreColumna, out var dato) ? dato : null;
                var celda = worksheet.Cell(fila + 2, columna + 1);

                if (nombreColumna == "Clave_Ent")
                {
                    celda.Style.NumberFormat.Format = "@";
                    celda.Value = valor?.ToString() ?? string.Empty;
                }
                else
                {
                    celda.Value = Convertir(valor);
                }
            }
        }

        for (var i = 0; i < columnas.Count; i++) worksheet.Column(i + 1).Width = 14;
        worksheet.SheetView.FreezeRows(1);
        workbook.SaveAs(entryStream);
    }

    private static XLCellValue Convertir(object? valor)
    {
        if (valor == null || valor == DBNull.Value) return string.Empty;

        return valor switch
        {
            DateTime fecha => fecha,
            int entero => entero,
            long enteroLargo => enteroLargo,
            decimal numeroDecimal => numeroDecimal,
            double numeroDouble => numeroDouble,
            float numeroFloat => numeroFloat,
            bool booleano => booleano,
            _ => valor.ToString() ?? string.Empty
        };
    }
}
