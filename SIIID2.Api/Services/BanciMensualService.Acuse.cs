using System.Globalization;
using Dapper;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public sealed partial class BanciMensualService
{
    public async Task<byte[]> AcuseAsync(int usuario, Guid referencia)
    {
        using var db = factory.CrearConexion();
        var op = await ObtenerAsync(db, referencia);
        await AccesoAsync(usuario, op.Entidad);
        if(op.Estado is not ("PENDIENTE_APROBACION" or "CONFIRMADO" or "CONFIRMADO_ACTUALIZACION"))
            throw new InvalidOperationException("El acuse está disponible después de confirmar el envío.");
        var libro = await DetalleAsync(usuario, referencia);
        var fecha = await db.QuerySingleAsync<DateTime>("SELECT COALESCE(fecha_aprobacion,fecha_confirmacion,fecha_validacion) FROM dbo.banci_carga WHERE id_banci_carga=@Id;", op);
        var catalogos = new Dictionary<string, Dictionary<string, string>>();
        foreach(var (campo, tabla) in new[]
        {
            ("localizado_o_no_localizado", "banci_catalogo_localizacion"),
            ("con_o_sin_vida", "banci_catalogo_condicion_vida")
        })
            catalogos[campo] = (await db.QueryAsync<(string Clave, string Descripcion)>($"SELECT CONVERT(nvarchar(20),clave),descripcion FROM dbo.{tabla};")).ToDictionary(f => f.Clave, f => f.Descripcion);
        return GenerarAcuse(op, libro, fecha, catalogos, ambiente.WebRootPath ?? Path.Combine(ambiente.ContentRootPath, "wwwroot"));
    }

    public static byte[] GenerarAcuse(BanciOperacion op, BanciLibro libro, DateTime fechaUtc,
        IReadOnlyDictionary<string, Dictionary<string, string>> catalogos, string webRootPath)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var confirmado = op.Estado.StartsWith("CONFIRMADO", StringComparison.Ordinal);
        var cultura = CultureInfo.GetCultureInfo("es-MX");
        var fecha = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(fechaUtc, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"));
        string Catalogo(ArchivoFila fila, string campo)
        {
            var clave = Valor(fila, campo);
            return catalogos.TryGetValue(campo, out var opciones) && opciones.TryGetValue(clave, out var nombre) ? nombre : clave == "" ? "No aplica" : clave;
        }
        var carpetas = libro.Carpetas.ToDictionary(f => Valor(f, "id_ci"));
        return Document.Create(documento => documento.Page(pagina =>
        {
            pagina.Size(PageSizes.Letter);
            pagina.MarginTop(12);
            pagina.MarginLeft(20);
            pagina.MarginRight(20);
            pagina.MarginBottom(8);
            pagina.DefaultTextStyle(t => t.FontSize(8).FontFamily("Noto Sans"));
            if(!confirmado)
                pagina.Background().AlignCenter().AlignMiddle().Rotate(-35).Text("PREVIO").FontSize(72).Bold().FontColor(Colors.Red.Lighten4);
            pagina.Header().Repeat().Element(c => AcusePdfService.ConstruirEncabezadoInstitucional(c, fecha, webRootPath));
            pagina.Content().Column(col =>
            {
                col.Spacing(8);
                col.Item().PaddingTop(4).AlignCenter().Text($"ACUSE DE {(libro.Actualizacion ? "ACTUALIZACIÓN" : "ENTREGA")} DE INFORMACIÓN BANCI DEL ESTADO DE {op.EntidadNombre.ToUpper(cultura)}").FontSize(12).Bold();
                col.Item().Text(text =>
                {
                    text.Justify();
                    text.DefaultTextStyle(t => t.FontSize(8.5f));
                    text.Span("El presente acuse de recepción hace constar que la información BANCI reportada por la ");
                    text.Span($"Fiscalía General del Estado de {op.EntidadNombre}").Bold();
                    text.Span(confirmado ? " ha sido integrada satisfactoriamente mediante la plataforma web, el día " : " ha sido enviada a revisión administrativa mediante la plataforma web, el día ");
                    text.Span(fecha.ToString("dd 'de' MMMM 'de' yyyy", cultura)).Bold();
                    text.Span(" a las "); text.Span(fecha.ToString("HH:mm:ss", cultura)).Bold();
                    text.Span(" horas (Tiempo del centro de México).");
                });
                col.Item().Text($"Mes de corte estadístico: {new DateTime(op.Anio, op.Mes, 1).ToString("MMMM 'de' yyyy", cultura)}").FontSize(8.5f);
                col.Item().Text($"Referencia: {op.Referencia} · Usuario: {op.NombreUsuario} · Estado: {op.Estado.Replace('_', ' ')}");
                if(!confirmado)
                    col.Item().Text("Pendiente de aprobación administrativa. Los folios de carpetas nuevas se asignan al aprobar; este documento no acredita su integración definitiva.").Bold();
                col.Item().Table(tabla =>
                {
                    tabla.ColumnsDefinition(cols => { cols.RelativeColumn(4); cols.RelativeColumn(2); });
                    tabla.Cell().ColumnSpan(2).Element(TituloAcuseBanci).Text("Detalle de registros suministrados");
                    tabla.Cell().Element(EncabezadoAcuseBanci).Text("Descripción");
                    tabla.Cell().Element(EncabezadoAcuseBanci).Text("Total");
                    foreach(var (nombre, total) in new[] { ("Carpetas de Investigación:", libro.Carpetas.Count), ("Delitos:", libro.Delitos.Count), ("Víctimas:", libro.Victimas.Count) })
                    {
                        tabla.Cell().Element(CeldaAcuseBanci).AlignRight().Text(nombre).Bold();
                        tabla.Cell().Element(CeldaAcuseBanci).AlignCenter().Text(total.ToString("N0", cultura));
                    }
                });
                col.Item().Text("Registros reportados · El no_banci identifica la carpeta y se repite para cada víctima vinculada.").Bold();
                col.Item().Table(tabla =>
                {
                    tabla.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(1.8f);
                        cols.RelativeColumn(1.6f);
                        cols.RelativeColumn();
                        cols.RelativeColumn();
                        cols.RelativeColumn(2);
                        cols.RelativeColumn(1.4f);
                        cols.RelativeColumn(1.4f);
                    });
                    tabla.Header(header =>
                    {
                        foreach(var titulo in new[] { "no_banci", "Carpeta / nomenclatura", "ID delito", "ID víctima", "Nombre / FUB", "Localización", "Condición de vida" })
                            header.Cell().Element(EncabezadoAcuseBanci).Text(titulo);
                    });
                    foreach(var victima in libro.Victimas)
                    {
                        carpetas.TryGetValue(Valor(victima, "id_ci"), out var carpeta);
                        var folio = carpeta == null ? "" : Valor(carpeta, "no_banci");
                        var nombre = string.Join(' ', new[] { Valor(victima, "nomb"), Valor(victima, "pro_apellido"), Valor(victima, "sdo_apellido") }.Where(v => v != ""));
                        foreach(var valor in new[]
                        {
                            folio == "" ? "Pendiente de aprobación" : folio,
                            carpeta == null ? Valor(victima, "id_ci") : Valor(carpeta, "id_ci") + " / " + Valor(carpeta, "ntra_ci"),
                            Valor(victima, "id_delito"), Valor(victima, "id_vicf"),
                            nombre + (Valor(victima, "fub") == "" ? "" : " · FUB: " + Valor(victima, "fub")),
                            Catalogo(victima, "localizado_o_no_localizado"), Catalogo(victima, "con_o_sin_vida")
                        })
                            tabla.Cell().Element(CeldaAcuseBanci).Text(valor);
                    }
                });
                if(libro.Actualizacion && libro.Cambios.Count > 0)
                {
                    col.Item().Text("Resumen de cambios reportados").Bold();
                    foreach(var cambio in libro.Cambios)
                        col.Item().Text($"Carpeta {cambio.Carpeta} · Delito {cambio.Delito} · Víctima {cambio.Victima} · {cambio.Campo}: {cambio.Anterior ?? "(vacío)"} → {cambio.Nuevo ?? "(vacío)"}");
                }
            });
            pagina.Footer().Element(c => AcusePdfService.ConstruirPiePaginaInstitucional(c, webRootPath));
        })).GeneratePdf();
    }

    private static IContainer TituloAcuseBanci(IContainer c) => c.Border(0.5f).BorderColor(Colors.Black).Background("#7A1735").Padding(3).AlignCenter().DefaultTextStyle(t => t.FontSize(8).Bold().FontColor(Colors.White));
    private static IContainer EncabezadoAcuseBanci(IContainer c) => c.Border(0.5f).BorderColor(Colors.Black).Background("#B7B7B7").Padding(3).AlignCenter().AlignMiddle().DefaultTextStyle(t => t.FontSize(7.5f).Bold());
    private static IContainer CeldaAcuseBanci(IContainer c) => c.Border(0.5f).BorderColor(Colors.Black).PaddingHorizontal(3).PaddingVertical(2).AlignMiddle().DefaultTextStyle(t => t.FontSize(7.2f));
}
