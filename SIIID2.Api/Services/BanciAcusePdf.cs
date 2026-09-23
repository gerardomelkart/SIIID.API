using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public static class BanciAcusePdf
{
    public static byte[] Generar(BanciCargaValidacionResponse carga, string entidad, string usuario, string raiz)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var previo = carga.Estado == "VALIDADO_PENDIENTE";
        var logo = Path.Combine(raiz, "wwwroot", "images", "logo_acuses.png");
        var mujer = Path.Combine(raiz, "wwwroot", "images", "mujer_acuses.png");
        var pie = Path.Combine(raiz, "wwwroot", "images", "pie_pagina_acuses.png");
        return Document.Create(documento => documento.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.MarginTop(12);
            page.MarginHorizontal(20);
            page.MarginBottom(8);
            page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Noto Sans"));
            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    if (File.Exists(logo)) row.RelativeItem(3).Height(38).Image(logo).FitArea();
                    if (File.Exists(mujer)) row.RelativeItem().Height(42).AlignRight().Image(mujer).FitArea();
                });
                col.Item().PaddingTop(10).AlignCenter().Text("BASE NACIONAL DE CARPETAS DE INVESTIGACIÓN").Bold().FontSize(12);
                col.Item().AlignCenter().Text("BANCI · Desaparición Forzada de Personas y Desaparición Cometida por Particulares");
                col.Item().PaddingTop(8).AlignCenter().Text(previo ? "INFORME PREVIO — SIN INTEGRAR" : "ACUSE DE INTEGRACIÓN").Bold().FontColor("#702b91").FontSize(15);
            });
            page.Content().PaddingTop(16).Column(col =>
            {
                col.Spacing(10);
                col.Item().Text(previo ? "La validación terminó. Este informe no acredita la integración de datos. La persona que realizó la carga debe aceptar o rechazar la operación." : "La carga fue aceptada por la persona que la registró y quedó integrada en BANCI.");
                col.Item().Text($"Entidad: {entidad}");
                col.Item().Text($"Usuario que realizó la carga: {usuario}");
                col.Item().Text($"Número de carga: {carga.IdBanciCarga}");
                col.Item().Text($"Fecha de {(previo ? "registro" : "integración")}: {(previo ? carga.FechaCarga : carga.FechaConfirmacion)?.ToString("dd/MM/yyyy HH:mm:ss") ?? "No disponible"}");
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns => { columns.RelativeColumn(3); columns.RelativeColumn(); });
                    table.Header(header => { header.Cell().Background("#702b91").Padding(8).Text("Registros").FontColor(Colors.White).Bold(); header.Cell().Background("#702b91").Padding(8).Text("Cantidad").FontColor(Colors.White).Bold(); });
                    foreach (var item in new[] { ("Carpetas", carga.TotalCarpetas), ("Delitos", carga.TotalDelitos), ("Víctimas", carga.TotalVictimas) })
                    {
                        table.Cell().BorderBottom(1).BorderColor("#dddddd").Padding(8).Text(item.Item1);
                        table.Cell().BorderBottom(1).BorderColor("#dddddd").Padding(8).Text(item.Item2.ToString());
                    }
                });
                col.Item().Text($"Advertencias registradas: {carga.TotalAdvertencias}");
                if (previo && carga.TotalAdvertencias > 0) col.Item().Text("Revise las advertencias en pantalla antes de aceptar. Este PDF no sustituye su revisión.");
                if (!previo) col.Item().Text("Los identificadores asignados se consultan en el Excel de integración disponible junto con este acuse.");
                col.Item().Text("La carga inicial registra carpetas nuevas. La actualización de víctimas se realiza en sus opciones manual y masiva.");
            });
            page.Footer().Column(col =>
            {
                if (File.Exists(pie)) col.Item().Height(54).Image(pie).FitArea();
                col.Item().AlignRight().Text(text => { text.Span("Página "); text.CurrentPageNumber(); text.Span(" de "); text.TotalPages(); });
            });
        })).GeneratePdf();
    }
}
