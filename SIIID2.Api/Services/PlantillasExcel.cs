using ClosedXML.Excel;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;

public static class PlantillasExcel
{
    public static byte[] Crear(string modulo, string archivo, CarpetasValidator carpetas, DelitosValidator delitos, VictimasValidator victimas)
    {
        // Conserva las columnas de las plantillas oficiales anteriores; no incluye registros de ejemplo.
        var columnas = archivo switch
        {
            "carpetas" => carpetas.ColumnasObligatorias.ToArray(),
            "delitos" => delitos.ColumnasObligatorias.ToArray(),
            "victimas" => victimas.ColumnasObligatorias.Concat(["nombre_vicfem", "1apellido_vicfem", "2apellido_vicfem", "curp_vicfem"]).ToArray(),
            _ => throw new ArgumentException("Tipo de archivo no válido.")
        };
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet(archivo);
        var color = XLColor.FromHtml("#" + ColoresExcelModulo.Principal(modulo));
        hoja.TabColor = color;
        hoja.Columns(1, columnas.Length).Width = 25;
        hoja.Columns(1, columnas.Length).Style.NumberFormat.Format = "@";
        hoja.Columns(1, columnas.Length).Style.Font.FontName = "Calibri";
        for(var i = 0; i < columnas.Length; i++)
        {
            hoja.Cell(1, i + 1).Value = columnas[i];
            if(columnas[i] is "rmen_de_hchos" or "dom_hchos" or "dto" or "moda_dto") hoja.Column(i + 1).Width = 48;
        }
        var encabezado = hoja.Range(1, 1, 1, columnas.Length);
        encabezado.Style.Fill.BackgroundColor = color;
        encabezado.Style.Font.FontColor = XLColor.White;
        encabezado.Style.Font.Bold = true;
        encabezado.Style.Alignment.WrapText = true;
        encabezado.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        hoja.Row(1).Height = 34;
        hoja.SheetView.FreezeRows(1);
        encabezado.SetAutoFilter();
        using var salida = new MemoryStream();
        libro.SaveAs(salida);
        return salida.ToArray();
    }
}
