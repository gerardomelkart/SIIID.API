using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public static class DiferenciasExcel
{
    public const string Mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const int MaxFilas = 1_048_576;
    private const int MaxTexto = 30_000;

    public static Stream Crear(ActualizacionDiferenciasResponse diferencias, string modulo = "mensual")
    {
        if(!diferencias.EsValido || diferencias.DetalleLimitado)
            throw new InvalidOperationException("No se obtuvo el detalle completo de diferencias. Intente nuevamente.");
        var archivo = new FileStream(Path.Combine(Path.GetTempPath(), $"diferencias-{Guid.NewGuid():N}.xlsx"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.DeleteOnClose);
        try
        {
            using(var documento = SpreadsheetDocument.Create(archivo, SpreadsheetDocumentType.Workbook))
            {
                var libro = documento.AddWorkbookPart();
                libro.Workbook = new Workbook(new Sheets());
                var estilos = libro.AddNewPart<WorkbookStylesPart>();
                estilos.Stylesheet = new Stylesheet(
                    new Fonts(new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }), new Font(new Bold(), new FontSize { Val = 11 }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Calibri" })) { Count = 2 },
                    new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                        new Fill(new PatternFill(new ForegroundColor { Rgb = "FF" + ColoresExcelModulo.Principal(modulo) }) { PatternType = PatternValues.Solid }),
                        new Fill(new PatternFill(new ForegroundColor { Rgb = "FF" + ColoresExcelModulo.Claro(modulo) }) { PatternType = PatternValues.Solid })) { Count = 4 },
                    new Borders(new Border()) { Count = 1 },
                    new CellStyleFormats(new CellFormat()) { Count = 1 },
                    new CellFormats(new CellFormat { FontId = 0, FillId = 0, BorderId = 0 },
                        new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Center }) { FontId = 1, FillId = 2, BorderId = 0, ApplyFill = true, ApplyFont = true, ApplyAlignment = true },
                        new CellFormat { FontId = 0, FillId = 3, BorderId = 0, ApplyFill = true }) { Count = 3 });
                estilos.Stylesheet.Save();
                uint id = 0;
                foreach(var (nombre, registros) in new[] { ("Carpetas", diferencias.Carpetas), ("Delitos", diferencias.Delitos), ("Victimas", diferencias.Victimas) })
                {
                    var parte = 0;
                    OpenXmlWriter? escritor = null;
                    uint fila = 0;
                    void CerrarHoja()
                    {
                        if(escritor == null) return;
                        escritor.WriteEndElement(); // SheetData
                        escritor.WriteElement(new AutoFilter { Reference = $"A1:F{fila}" });
                        escritor.WriteEndElement(); // Worksheet
                        escritor.Dispose();
                    }
                    void NuevaHoja()
                    {
                        CerrarHoja();
                        var hoja = libro.AddNewPart<WorksheetPart>();
                        libro.Workbook.GetFirstChild<Sheets>()!.Append(new Sheet
                        {
                            Id = libro.GetIdOfPart(hoja), SheetId = ++id,
                            Name = ++parte == 1 ? nombre : $"{nombre}_{parte}"
                        });
                        escritor = OpenXmlWriter.Create(hoja);
                        escritor.WriteStartElement(new Worksheet());
                        escritor.WriteElement(new SheetProperties(new TabColor { Rgb = "FF" + ColoresExcelModulo.Principal(modulo) }));
                        escritor.WriteElement(new SheetViews(new SheetView(new Pane { VerticalSplit = 1, TopLeftCell = "A2", State = PaneStateValues.Frozen }) { WorkbookViewId = 0 }));
                        escritor.WriteElement(new Columns(new Column { Min = 1, Max = 4, Width = 30, CustomWidth = true }, new Column { Min = 5, Max = 6, Width = 65, CustomWidth = true }));
                        escritor.WriteStartElement(new SheetData());
                        fila = 0;
                        EscribirFila(["Movimiento", "Campo identificador", "Identificador", "Campo", "Valor anterior", "Valor propuesto"]);
                    }
                    void EscribirFila(string[] valores)
                    {
                        escritor!.WriteStartElement(new Row { RowIndex = ++fila, Height = fila == 1 ? 32 : 16, CustomHeight = true });
                        foreach(var valor in valores)
                        {
                            // InlineString conserva identificadores, ceros iniciales y textos que empiezan con = como datos.
                            escritor.WriteElement(new Cell(new InlineString(new Text(valor) { Space = SpaceProcessingModeValues.Preserve })) { DataType = CellValues.InlineString, StyleIndex = fila == 1 ? 1U : fila % 2 == 0 ? 2U : 0U });
                        }
                        escritor.WriteEndElement();
                    }
                    NuevaHoja();
                    foreach(var registro in registros)
                    foreach(var campo in registro.CamposModificados.DefaultIfEmpty(new ActualizacionCampoDiferencia()))
                    {
                        var anteriores = Partir(campo.ValorAnterior ?? "");
                        var nuevos = Partir(campo.ValorNuevo ?? "");
                        var partes = Math.Max(anteriores.Count, nuevos.Count);
                        for(var n = 0; n < partes; n++)
                        {
                            if(fila == MaxFilas) NuevaHoja();
                            EscribirFila([registro.TipoMovimiento, registro.CampoIdentificador, registro.IdentificadorFiscalia, campo.Campo + (n > 0 ? " (continuación)" : ""), n < anteriores.Count ? anteriores[n] : "", n < nuevos.Count ? nuevos[n] : ""]);
                        }
                    }
                    CerrarHoja();
                }
                libro.Workbook.Save();
            }
            archivo.Position = 0;
            return archivo;
        }
        catch
        {
            archivo.Dispose();
            throw;
        }
    }
    private static List<string> Partir(string texto)
    {
        var partes = new List<string>();
        for(var inicio = 0; inicio < texto.Length;)
        {
            var longitud = Math.Min(MaxTexto, texto.Length - inicio);
            if(inicio + longitud < texto.Length && char.IsHighSurrogate(texto[inicio + longitud - 1])) longitud--;
            partes.Add(texto.Substring(inicio, longitud));
            inicio += longitud;
        }
        if(partes.Count == 0) partes.Add("");
        return partes;
    }

}
