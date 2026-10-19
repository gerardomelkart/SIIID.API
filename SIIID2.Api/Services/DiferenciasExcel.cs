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

    public static Stream Crear(ActualizacionDiferenciasResponse diferencias)
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
                        escritor.WriteElement(new AutoFilter { Reference = $"A1:G{fila}" });
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
                        escritor.WriteElement(new SheetViews(new SheetView(new Pane { VerticalSplit = 1, TopLeftCell = "A2", State = PaneStateValues.Frozen }) { WorkbookViewId = 0 }));
                        escritor.WriteElement(new Columns(new Column { Min = 1, Max = 4, Width = 30, CustomWidth = true }, new Column { Min = 5, Max = 6, Width = 65, CustomWidth = true }, new Column { Min = 7, Max = 7, Width = 12, CustomWidth = true }));
                        escritor.WriteStartElement(new SheetData());
                        fila = 0;
                        EscribirFila(["Movimiento", "Campo identificador", "Identificador", "Campo", "Valor anterior", "Valor propuesto", "Parte"]);
                    }
                    void EscribirFila(string[] valores)
                    {
                        escritor!.WriteStartElement(new Row { RowIndex = ++fila });
                        foreach(var valor in valores)
                        {
                            // InlineString conserva identificadores, ceros iniciales y textos que empiezan con = como datos.
                            escritor.WriteElement(new Cell(new InlineString(new Text(valor) { Space = SpaceProcessingModeValues.Preserve })) { DataType = CellValues.InlineString });
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
                            EscribirFila([registro.TipoMovimiento, registro.CampoIdentificador, registro.IdentificadorFiscalia, campo.Campo, n < anteriores.Count ? anteriores[n] : "", n < nuevos.Count ? nuevos[n] : "", $"{n + 1}/{partes}"]);
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
