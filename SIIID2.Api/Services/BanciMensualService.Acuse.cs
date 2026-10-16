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
        return GenerarAcuse(op, libro, fecha, catalogos);
    }

    public static byte[] GenerarAcuse(BanciOperacion op, BanciLibro libro, DateTime fechaUtc,
        IReadOnlyDictionary<string, Dictionary<string, string>> catalogos)
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
            pagina.Size(PageSizes.Letter.Landscape());
            pagina.Margin(28);
            pagina.DefaultTextStyle(t => t.FontSize(8).FontFamily("Noto Sans"));
            pagina.Header().PaddingBottom(12).Column(col =>
            {
                col.Item().Text("CENTRO NACIONAL DE INFORMACIÓN").FontSize(10).Bold();
                col.Item().Text(confirmado ? "BANCI · Acuse de información confirmada" : "BANCI · Acuse de envío a revisión").FontSize(16).Bold().FontColor("#6f2c91");
                col.Item().Text($"{op.EntidadNombre} · {new DateTime(op.Anio, op.Mes, 1).ToString("MMMM yyyy", cultura)} · {op.Tipo}");
                col.Item().Text($"Referencia: {op.Referencia} · Usuario: {op.NombreUsuario}");
                col.Item().Text($"Estado: {op.Estado.Replace('_', ' ')} · Fecha: {fecha:dd/MM/yyyy HH:mm:ss} (Ciudad de México)");
            });
            pagina.Content().Column(col =>
            {
                col.Spacing(10);
                if(!confirmado)
                    col.Item().Text("Envío recibido, pendiente de aprobación administrativa. Los folios de carpetas nuevas se asignan al aprobar. Este documento no acredita su integración definitiva.").Bold();
                col.Item().Text($"Resumen del envío: {libro.Carpetas.Count:N0} carpetas, {libro.Delitos.Count:N0} delitos y {libro.Victimas.Count:N0} víctimas.").Bold();
                foreach(var grupo in libro.Victimas.GroupBy(v => Catalogo(v, "localizado_o_no_localizado")))
                    col.Item().Text($"{grupo.Key}: {grupo.Count():N0} víctimas.");
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
                            header.Cell().Background("#6f2c91").Padding(5).Text(titulo).FontColor(Colors.White).Bold();
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
                            tabla.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(valor);
                    }
                });
                if(libro.Actualizacion && libro.Cambios.Count > 0)
                {
                    col.Item().Text("Resumen de cambios reportados").Bold();
                    foreach(var cambio in libro.Cambios)
                        col.Item().Text($"Carpeta {cambio.Carpeta} · Delito {cambio.Delito} · Víctima {cambio.Victima} · {cambio.Campo}: {cambio.Anterior ?? "(vacío)"} → {cambio.Nuevo ?? "(vacío)"}");
                }
            });
            pagina.Footer().AlignRight().Text(t =>
            {
                t.Span("BANCI · Página ");
                t.CurrentPageNumber();
                t.Span(" de ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }
}
