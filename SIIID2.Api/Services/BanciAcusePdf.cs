using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SIIID2.Api.Controllers;

namespace SIIID2.Api.Services;

public static class BanciAcusePdf
{
    public static byte[] Generar(IReadOnlyList<BanciResumenController.BanciResumenRegistro> filas, string referencia)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(documento => documento.Page(page =>
        {
            page.Size(PageSizes.A3.Landscape());
            page.Margin(18);
            page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Noto Sans"));
            page.Content().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    foreach (var ancho in new float[] { 1.2f, 2, 1.2f, 2.5f, 1, 1, 1.5f, 1.8f, 1.7f, 2.5f }) columns.RelativeColumn(ancho);
                });
                table.Header(header =>
                {
                    foreach (var titulo in new[] { "Entidad", "NO_BANCI", "ID_CI", "NTRA_CI", "ID_DELITO", "ID_VICF", "FOLIO_RNPDNO", "Resultado", "FechaIntegracion", "Referencia" }) header.Cell().Background("#702b91").Padding(5).Text(titulo).FontColor(Colors.White).Bold();
                });
                foreach (var fila in filas)
                {
                    var valores = new[] { fila.Entidad, fila.NoBanci, fila.IdCi, fila.NtraCi, fila.IdDelito, fila.IdVicf, fila.FolioRnpdno ?? "", fila.Resultado, fila.FechaIntegracion?.ToString("yyyy-MM-ddTHH:mm:ss") ?? "", referencia };
                    for (var i = 0; i < valores.Length; i++)
                    {
                        var celda = table.Cell().BorderBottom(0.5f).BorderColor("#dddddd").Padding(5);
                        if (i == 1) celda.Text(valores[i]).Bold(); else celda.Text(valores[i]);
                    }
                }
            });
        })).GeneratePdf();
    }
}
