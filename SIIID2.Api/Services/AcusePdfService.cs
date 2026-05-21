using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class AcusePdfService : IAcusePdfService
{
    private const string RutaLogoAcuse = "wwwroot/images/logo_acuses.png";
    private const string RutaMujerAcuse = "wwwroot/images/mujer_acuses.png";
    private const string RutaPiePaginaAcuse = "wwwroot/images/pie_pagina_acuses.png";

    private readonly ICargaRepository _cargaRepository;
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IWebHostEnvironment _webHostEnvironment;

    public AcusePdfService(ICargaRepository cargaRepository, IUsuarioRepository usuarioRepository, IWebHostEnvironment webHostEnvironment)
    {
        _cargaRepository = cargaRepository;
        _usuarioRepository = usuarioRepository;
        _webHostEnvironment = webHostEnvironment;
    }

    public async Task<byte[]> GenerarAcusePrevioAsync(string codigoReferencia, int idUsuarioConsulta)
    {
        // QuestPDF requiere declarar licencia.
        QuestPDF.Settings.License = LicenseType.Community;

        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            throw new UnauthorizedAccessException("El usuario autenticado no existe o no está activo.");
        }

        var carga = await _cargaRepository.ObtenerCargaParaAcuseAsync(codigoReferencia);

        if (carga == null)
        {
            throw new InvalidOperationException("No se encontró la carga solicitada.");
        }

        // El acuse previo solo aplica para cargas validadas y pendientes de confirmar.
        if (!string.Equals(carga.Estado, "VALIDADO_PENDIENTE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El acuse previo solo puede generarse para cargas en estado VALIDADO_PENDIENTE.");
        }

        // Usuario normal solo puede consultar acuses de su entidad.
        if (!usuarioConsulta.EsSuperUsuario &&
            usuarioConsulta.IdEntidadFederativa.HasValue &&
            carga.IdEntidadFederativa.HasValue &&
            usuarioConsulta.IdEntidadFederativa.Value != carga.IdEntidadFederativa.Value)
        {
            throw new UnauthorizedAccessException("El usuario no tiene permiso para consultar el acuse de esta entidad.");
        }

        var resumen = await _cargaRepository.ObtenerResumenAcuseAsync(carga.IdCarga);

        return GenerarPdf(carga, resumen);
    }

    private byte[] GenerarPdf(CargaAcuseInfo carga, List<CargaAcuseResumenItem> resumen)
    {
        var totalDelitos = resumen.Sum(x => x.TotalDelitos);
        var totalVictimas = resumen.Sum(x => x.TotalVictimas);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);

                // Márgenes compactos para que la tabla se parezca más al acuse anterior.
                page.MarginTop(20);
                page.MarginLeft(20);
                page.MarginRight(20);
                page.MarginBottom(15);

                page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                // Marca de agua en todas las páginas.
                page.Background().Element(contenedor => ConstruirMarcaAgua(contenedor));

                page.Header().Element(header => ConstruirEncabezado(header));

                page.Content().Column(column =>
                {
                    column.Spacing(6);

                    column.Item()
                        .PaddingTop(4)
                        .AlignCenter()
                        .Text("ACUSE PREVIO")
                        .FontSize(17)
                        .Bold();

                    // Datos generales del acuse.
                    column.Item().Element(contenedor => ConstruirDatosGenerales(contenedor, carga));

                    column.Item().Element(contenedor => ConstruirDetalleRegistros(contenedor, carga));

                    column.Item().Element(contenedor => ConstruirTablaResumen(contenedor, resumen));

                    column.Item()
                        .AlignRight()
                        .PaddingTop(2)
                        .Text(text =>
                        {
                            text.Span("Total: ").Bold();
                            text.Span($"{totalDelitos}    {totalVictimas}").FontSize(12);
                        });
                });

                page.Footer().Element(footer => ConstruirPiePagina(footer));
            });
        }).GeneratePdf();
    }

    private void ConstruirMarcaAgua(IContainer container)
    {
        // Marca de agua PREVIO.
        // Se baja más para que no quede tan alta en la primera hoja.
        container
            .AlignCenter()
            .AlignMiddle()
            .TranslateY(55)
            .Rotate(-35)
            .Text("PREVIO")
            .FontSize(72)
            .Bold()
            .FontColor(Colors.Red.Lighten4);
    }

    private void ConstruirEncabezado(IContainer container)
    {
        var rutaLogo = ObtenerRutaArchivo(RutaLogoAcuse);
        var rutaMujer = ObtenerRutaArchivo(RutaMujerAcuse);

        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                // Logo institucional principal.
                row.RelativeItem(3)
                    .Height(55)
                    .AlignLeft()
                    .Image(rutaLogo)
                    .FitArea();

                // Imagen superior derecha.
                row.RelativeItem()
                    .Height(65)
                    .AlignRight()
                    .Image(rutaMujer)
                    .FitArea();
            });

            column.Item().PaddingTop(2).Row(row =>
            {
                row.RelativeItem();

                row.RelativeItem(1.7f).Column(right =>
                {
                    right.Item().AlignRight().Text("SECRETARIADO EJECUTIVO DEL SISTEMA").Bold().FontSize(11);
                    right.Item().AlignRight().Text("NACIONAL DE SEGURIDAD PÚBLICA").Bold().FontSize(11);
                    right.Item().AlignRight().Text("CENTRO NACIONAL DE INFORMACIÓN").Bold().FontSize(11);
                });
            });
        });
    }

    private void ConstruirDatosGenerales(IContainer container, CargaAcuseInfo carga)
    {
        container.Column(column =>
        {
            column.Item()
                .Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(7.5f));
                    text.Span("Código de referencia: ").Bold();
                    text.Span(carga.CodigoReferencia);
                });

            column.Item()
                .Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(7.5f));
                    text.Span("Entidad: ").Bold();
                    text.Span(carga.EntidadFederativa);
                    text.Span(" | Periodo: ").Bold();
                    text.Span($"{carga.MesCorte:00}/{carga.AnioCorte}");
                    text.Span(" | Fecha validación: ").Bold();
                    text.Span($"{carga.FechaValidacion:dd/MM/yyyy HH:mm}");
                });
        });
    }

    private void ConstruirDetalleRegistros(IContainer container, CargaAcuseInfo carga)
    {
        container.Column(column =>
        {
            column.Item()
                .Background("#7A1735")
                .Padding(4)
                .AlignCenter()
                .Text("Detalle de registros enviados:")
                .FontColor(Colors.White)
                .Bold()
                .FontSize(11);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Element(CeldaEncabezado).Text("Descripcion");
                    header.Cell().Element(CeldaEncabezado).Text("Total Registros");
                });

                table.Cell().Element(CeldaNormal).Text("Expedientes:");
                table.Cell().Element(CeldaNormal).AlignCenter().Text(carga.TotalCarpetasInvestigacion.ToString());

                table.Cell().Element(CeldaNormal).Text("Delitos:");
                table.Cell().Element(CeldaNormal).AlignCenter().Text(carga.TotalDelitos.ToString());

                table.Cell().Element(CeldaNormal).Text("Victimas:");
                table.Cell().Element(CeldaNormal).AlignCenter().Text(carga.TotalVictimas.ToString());
            });
        });
    }

    private void ConstruirTablaResumen(IContainer container, List<CargaAcuseResumenItem> resumen)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(45);
                columns.RelativeColumn(2.2f);
                columns.ConstantColumn(55);
                columns.RelativeColumn(2.5f);
                columns.ConstantColumn(45);
                columns.ConstantColumn(45);
            });

            table.Header(header =>
            {
                header.Cell().Element(CeldaEncabezado).Text("Clave\nDelito");
                header.Cell().Element(CeldaEncabezado).Text("Tipo de delito");
                header.Cell().Element(CeldaEncabezado).Text("Clave\nSubtipo");
                header.Cell().Element(CeldaEncabezado).Text("Subtipo de delito");
                header.Cell().Element(CeldaEncabezado).Text("Total\nDelitos");
                header.Cell().Element(CeldaEncabezado).Text("Total\nVictimas");
            });

            foreach (var item in resumen)
            {
                table.Cell().Element(CeldaNormal).Text(item.ClaveDelito);
                table.Cell().Element(CeldaNormal).Text(item.TipoDelito);
                table.Cell().Element(CeldaNormal).Text(item.ClaveSubtipo);
                table.Cell().Element(CeldaNormal).Text(item.SubtipoDelito);
                table.Cell().Element(CeldaNormal).AlignCenter().Text(item.TotalDelitos.ToString());
                table.Cell().Element(CeldaNormal).AlignCenter().Text(item.TotalVictimas.ToString());
            }
        });
    }

    private void ConstruirPiePagina(IContainer container)
    {
        var rutaPiePagina = ObtenerRutaArchivo(RutaPiePaginaAcuse);

        container
            .AlignCenter()
            .Height(70)
            .Width(571)
            .Image(rutaPiePagina)
            .FitArea();
    }

    private string ObtenerRutaArchivo(string rutaRelativa)
    {
        // Convierte wwwroot/images/archivo.png a ruta física.
        // Esto evita errores cuando la API se ejecuta desde otra carpeta.
        var rutaLimpia = rutaRelativa
            .Replace("wwwroot/", string.Empty)
            .Replace("/", Path.DirectorySeparatorChar.ToString());

        var rutaFisica = Path.Combine(_webHostEnvironment.WebRootPath, rutaLimpia);

        if (!File.Exists(rutaFisica))
        {
            throw new FileNotFoundException($"No se encontró el archivo requerido para el acuse: {rutaFisica}");
        }

        return rutaFisica;
    }

    private static IContainer CeldaEncabezado(IContainer container)
    {
        return container
            .Border(0.5f)
            .BorderColor(Colors.Black)
            .Background("#B7B7B7")
            .Padding(2)
            .AlignCenter()
            .AlignMiddle()
            .DefaultTextStyle(x => x.FontSize(8).Bold());
    }

    private static IContainer CeldaNormal(IContainer container)
    {
        return container
            .Border(0.5f)
            .BorderColor(Colors.Grey.Darken1)
            .Padding(2)
            .AlignMiddle()
            .DefaultTextStyle(x => x.FontSize(6.2f));
    }
}