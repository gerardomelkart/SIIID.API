using System.Globalization;
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

    private readonly IAcuseRepository _acuseRepository;
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IWebHostEnvironment _webHostEnvironment;

    public AcusePdfService(IAcuseRepository acuseRepository, IUsuarioRepository usuarioRepository, IWebHostEnvironment webHostEnvironment)
    {
        _acuseRepository = acuseRepository;
        _usuarioRepository = usuarioRepository;
        _webHostEnvironment = webHostEnvironment;
    }

    public async Task<byte[]> GenerarAcusePrevioAsync(string codigoReferencia, int idUsuarioConsulta)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            throw new UnauthorizedAccessException("El usuario autenticado no existe o no está activo.");
        }

        var carga = await _acuseRepository.ObtenerCargaParaAcuseAsync(codigoReferencia);

        if (carga == null)
        {
            throw new InvalidOperationException("No se encontró la carga solicitada.");
        }

        var estadoAcusePrevioPermitido =  string.Equals(carga.Estado, "VALIDADO_PENDIENTE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(carga.Estado,"PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase);

        if (!estadoAcusePrevioPermitido)
        {
            throw new InvalidOperationException("El informe previo solo puede generarse para cargas validadas o pendientes de aprobación.");
        }

        if (!usuarioConsulta.EsSuperUsuario &&
            usuarioConsulta.IdEntidadFederativa.HasValue &&
            carga.IdEntidadFederativa.HasValue &&
            usuarioConsulta.IdEntidadFederativa.Value != carga.IdEntidadFederativa.Value)
        {
            throw new UnauthorizedAccessException("El usuario no tiene permiso para consultar el acuse de esta entidad.");
        }

        var resumen = await _acuseRepository.ObtenerResumenAcuseAsync(carga.IdCarga);

        return GenerarPdf(
            carga,
            resumen,
            mostrarMarcaPrevio: true);
    }

    public async Task<byte[]> GenerarAcuseConfirmadoAsync(string codigoReferencia, int idUsuarioConsulta)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            throw new UnauthorizedAccessException("El usuario autenticado no existe o no está activo.");
        }

        var carga = await _acuseRepository.ObtenerCargaParaAcuseAsync(codigoReferencia);

        if (carga == null)
        {
            throw new InvalidOperationException("No se encontró la carga solicitada.");
        }

        if (!string.Equals(carga.Estado, "CONFIRMADO", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El acuse confirmado solo puede generarse para cargas en estado CONFIRMADO.");
        }

        if (!usuarioConsulta.EsSuperUsuario &&
            usuarioConsulta.IdEntidadFederativa.HasValue &&
            carga.IdEntidadFederativa.HasValue &&
            usuarioConsulta.IdEntidadFederativa.Value != carga.IdEntidadFederativa.Value)
        {
            throw new UnauthorizedAccessException("El usuario no tiene permiso para consultar el acuse de esta entidad.");
        }

        var resumen = await _acuseRepository.ObtenerResumenAcuseConfirmadoAsync(carga.IdCarga);

        return GenerarPdf(
            carga,
            resumen,
            mostrarMarcaPrevio: false);
    }

    public async Task<byte[]> GenerarAcusePrevioActualizacionAsync(string codigoReferencia, int idUsuarioConsulta)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            throw new UnauthorizedAccessException("El usuario autenticado no existe o no está activo.");
        }

        var carga = await _acuseRepository.ObtenerCargaParaAcuseAsync(codigoReferencia);

        if (carga == null)
        {
            throw new InvalidOperationException("No se encontró la actualización solicitada.");
        }

        var estadoAcusePrevioActualizacionPermitido = string.Equals(carga.Estado, "VALIDADO_PENDIENTE_ACTUALIZACION", StringComparison.OrdinalIgnoreCase)||
            string.Equals(carga.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase);

        if (!estadoAcusePrevioActualizacionPermitido)
        {
            throw new InvalidOperationException("El informe previo de actualización solo puede generarse para actualizaciones validadas o pendientes de aprobación.");
        }

        if (!usuarioConsulta.EsSuperUsuario &&
            usuarioConsulta.IdEntidadFederativa.HasValue &&
            carga.IdEntidadFederativa.HasValue &&
            usuarioConsulta.IdEntidadFederativa.Value != carga.IdEntidadFederativa.Value)
        {
            throw new UnauthorizedAccessException("El usuario no tiene permiso para consultar el acuse de esta entidad.");
        }

        var resumen = await _acuseRepository.ObtenerResumenAcuseAsync(carga.IdCarga);

        return GenerarPdf(
            carga,
            resumen,
            mostrarMarcaPrevio: true);
    }

    public async Task<byte[]> GenerarAcuseConfirmadoActualizacionAsync(string codigoReferencia, int idUsuarioConsulta)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            throw new UnauthorizedAccessException("El usuario autenticado no existe o no está activo.");
        }

        var carga = await  _acuseRepository.ObtenerCargaParaAcuseAsync(codigoReferencia);

        if (carga == null)
        {
            throw new InvalidOperationException("No se encontró la actualización solicitada.");
        }

        if (!string.Equals(carga.Estado, "CONFIRMADO_ACTUALIZACION", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("El acuse confirmado de actualización solo puede generarse para actualizaciones en estado CONFIRMADO_ACTUALIZACION.");
        }

        if (!usuarioConsulta.EsSuperUsuario &&
            usuarioConsulta.IdEntidadFederativa.HasValue &&
            carga.IdEntidadFederativa.HasValue &&
            usuarioConsulta.IdEntidadFederativa.Value != carga.IdEntidadFederativa.Value)
        {
            throw new UnauthorizedAccessException("El usuario no tiene permiso para consultar el acuse de esta entidad.");
        }

        var resumen = await _acuseRepository.ObtenerResumenAcuseConfirmadoActualizacionAsync(carga.IdCarga);

        return GenerarPdf(
            carga,
            resumen,
            mostrarMarcaPrevio: false);
    }

    private byte[] GenerarPdf(CargaAcuseInfo carga, List<CargaAcuseResumenItem> resumen, bool mostrarMarcaPrevio)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginTop(12);
                page.MarginLeft(20);
                page.MarginRight(20);
                page.MarginBottom(8);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Noto Sans"));

                if (mostrarMarcaPrevio)
                {
                    page.Background().Layers(layers =>
                    {
                        layers.PrimaryLayer().ShowOnce().Element(contenedor => ConstruirMarcaAgua(contenedor, 80));
                        layers.Layer().SkipOnce().Element(contenedor => ConstruirMarcaAgua(contenedor, 55));
                    });
                }

                page.Header().Element(header => ConstruirEncabezado(header, carga));

                page.Content().Column(column =>
                {
                    column.Spacing(4);
                    column.Item().Element(contenedor => ConstruirLeyendaAcuse(contenedor, carga));
                    column.Item().Element(contenedor => ConstruirDetalleRegistros(contenedor, carga));
                    column.Item().Element(contenedor => ConstruirTablaResumen(contenedor, resumen));
                });

                page.Footer().Element(footer => ConstruirPiePagina(footer));
            });
        }).GeneratePdf();
    }

    private void ConstruirMarcaAgua(IContainer container, float offsetY)
    {
        container.AlignCenter().AlignMiddle().OffsetY(offsetY).Rotate(-35).Text("PREVIO").FontSize(72).Bold().FontColor(Colors.Red.Lighten4);
    }

    private void ConstruirEncabezado(IContainer container, CargaAcuseInfo carga)
    {
        var rutaLogo = ObtenerRutaArchivo(RutaLogoAcuse);
        var rutaMujer = ObtenerRutaArchivo(RutaMujerAcuse);
        var fechaAcuse = carga.FechaConfirmacion ?? carga.FechaValidacion;

        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem(3).Height(38).AlignLeft().Image(rutaLogo).FitArea();
                row.RelativeItem().Height(42).AlignRight().Image(rutaMujer).FitArea();
            });

            column.Item().PaddingTop(1).Row(row =>
            {
                row.RelativeItem();

                row.RelativeItem(2.3f).Column(right =>
                {
                    right.Item().AlignRight().Text("SECRETARIADO EJECUTIVO DEL SISTEMA").Bold().FontSize(7.3f);
                    right.Item().AlignRight().Text("NACIONAL DE SEGURIDAD PÚBLICA").Bold().FontSize(7.3f);
                    right.Item().AlignRight().Text("CENTRO NACIONAL DE INFORMACIÓN").Bold().FontSize(7.3f);
                    right.Item().PaddingTop(2).AlignRight().Text($"Ciudad de México a {ObtenerFechaLargaEncabezado(fechaAcuse)}").FontSize(6.8f);
                });
            });
        });
    }


    private void ConstruirDetalleRegistros(IContainer container, CargaAcuseInfo carga)
    {
        var cultura = new CultureInfo("es-MX");

        container.Column(column =>
        {
            column.Item()
                .PaddingTop(8)
                .Table(table =>
                {
                    DefinirColumnasAcuse(table);

                    table.Cell().ColumnSpan(6).Element(CeldaTituloSeccion)
                        .Text("Detalle de registros suministrados");

                    table.Cell().ColumnSpan(4).Element(CeldaEncabezado)
                        .Text("Descripción");

                    table.Cell().ColumnSpan(2).Element(CeldaEncabezado)
                        .Text("Total");

                    table.Cell().ColumnSpan(4).Element(CeldaDescripcionDetalle)
                        .Text("Carpetas de Investigación:");

                    table.Cell().ColumnSpan(2).Element(CeldaNumeroDetalle)
                        .Text(carga.TotalCarpetasInvestigacion.ToString("N0", cultura));

                    table.Cell().ColumnSpan(4).Element(CeldaDescripcionDetalle)
                        .Text("Delitos:");

                    table.Cell().ColumnSpan(2).Element(CeldaNumeroDetalle)
                        .Text(carga.TotalDelitos.ToString("N0", cultura));

                    table.Cell().ColumnSpan(4).Element(CeldaDescripcionDetalle)
                        .Text("Víctimas:");

                    table.Cell().ColumnSpan(2).Element(CeldaNumeroDetalle)
                        .Text(carga.TotalVictimas.ToString("N0", cultura));
                });
        });
    }

    private void ConstruirTablaResumen(IContainer container, List<CargaAcuseResumenItem> resumen)
    {
        var cultura = new CultureInfo("es-MX");

        container.Table(table =>
        {
            DefinirColumnasAcuse(table);

            table.Header(header =>
            {
                header.Cell().Element(CeldaEncabezado).Text("Clave\nDelito");
                header.Cell().Element(CeldaEncabezado).Text("Tipo de delito");
                header.Cell().Element(CeldaEncabezado).Text("Clave\nSubtipo");
                header.Cell().Element(CeldaEncabezado).Text("Subtipo de delito");
                header.Cell().Element(CeldaEncabezado).Text("Total\nDelitos");
                header.Cell().Element(CeldaEncabezado).Text("Total\nVíctimas");
            });

            foreach (var item in resumen)
            {
                table.Cell().Element(CeldaNormalSinCorte).Text(item.ClaveDelito);
                table.Cell().Element(CeldaNormalSinCorte).Text(item.TipoDelito);
                table.Cell().Element(CeldaNormalSinCorte).Text(item.ClaveSubtipo);
                table.Cell().Element(CeldaNormalSinCorte).Text(item.SubtipoDelito);
                table.Cell().Element(CeldaNumeroSinCorte).Text(item.TotalDelitos.ToString("N0", cultura));
                table.Cell().Element(CeldaNumeroSinCorte).Text(item.TotalVictimas.ToString("N0", cultura));
            }
        });
    }


    private static IContainer CeldaNormalSinCorte(IContainer container)
    {
        return CeldaNormal(container)
            .ShowEntire();
    }

    private static IContainer CeldaNumeroSinCorte(IContainer container)
    {
        return CeldaNumero(container)
            .ShowEntire();
    }

    private void ConstruirPiePagina(IContainer container)
    {
        var rutaPiePagina = ObtenerRutaArchivo(RutaPiePaginaAcuse);

        container.AlignLeft().Height(54).Width(572).Image(rutaPiePagina).FitArea();
    }

    private string ObtenerRutaArchivo(string rutaRelativa)
    {
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

    private static string ObtenerNombreMes(int mes)
    {
        if (mes < 1 || mes > 12)
        {
            return mes.ToString("00");
        }

        var cultura = new CultureInfo("es-MX");
        var nombreMes = cultura.DateTimeFormat.GetMonthName(mes);

        return char.ToUpper(nombreMes[0], cultura) + nombreMes[1..];
    }
    private static void DefinirColumnasAcuse(TableDescriptor table)
    {
        table.ColumnsDefinition(columns =>
        {
            columns.ConstantColumn(45);
            columns.RelativeColumn(2.4f);
            columns.ConstantColumn(55);
            columns.RelativeColumn(2.6f);
            columns.ConstantColumn(50);
            columns.ConstantColumn(50);
        });
    }

    private static IContainer CeldaTituloSeccion(IContainer container)
    {
        return container
            .Border(0.5f)
            .BorderColor(Colors.Black)
            .Background("#7A1735")
            .Padding(3)
            .AlignCenter()
            .AlignMiddle()
            .DefaultTextStyle(x => x.FontSize(8).Bold().FontColor(Colors.White));
    }

    private static IContainer CeldaEncabezado(IContainer container)
    {
        return container
            .Border(0.5f)
            .BorderColor(Colors.Black)
            .Background("#B7B7B7")
            .Padding(3)
            .AlignCenter()
            .AlignMiddle()
            .DefaultTextStyle(x => x.FontSize(7.5f).Bold());
    }

    private static IContainer CeldaNormal(IContainer container)
    {
        return container
            .ShowEntire()
            .Border(0.5f)
            .BorderColor(Colors.Black)
            .PaddingHorizontal(3)
            .PaddingVertical(1)
            .AlignMiddle()
            .DefaultTextStyle(x => x.FontSize(7.2f));
    }

    private static IContainer CeldaNumero(IContainer container)
    {
        return container
            .ShowEntire()
            .Border(0.5f)
            .BorderColor(Colors.Black)
            .PaddingHorizontal(3)
            .PaddingVertical(1)
            .AlignCenter()
            .AlignMiddle()
            .DefaultTextStyle(x => x.FontSize(7.0f));
    }

    private static IContainer CeldaDescripcionDetalle(IContainer container)
    {
        return CeldaNormal(container)
            .AlignRight()
            .DefaultTextStyle(x => x.FontSize(7.5f).Bold());
    }

    private static IContainer CeldaNumeroDetalle(IContainer container)
    {
        return CeldaNormal(container)
            .AlignCenter()
            .DefaultTextStyle(x => x.FontSize(7.5f).Bold());
    }



    private void ConstruirLeyendaAcuse(IContainer container, CargaAcuseInfo carga)
    {
        var cultura = new CultureInfo("es-MX");
        var fechaAcuse = carga.FechaConfirmacion ?? carga.FechaValidacion;
        var entidad = carga.EntidadFederativa?.Trim() ?? string.Empty;
        var entidadMayusculas = entidad.ToUpper(cultura);

        container.Column(column =>
        {
            column.Spacing(8);

            column.Item()
                .PaddingTop(4)
                .AlignCenter()
                .Text($"ACUSE DE ENTREGA DE INFORMACIÓN DEL ESTADO DE {entidadMayusculas}")
                .FontSize(12)
                .Bold();

            column.Item()
                .Text(text =>
                {
                    text.Justify();
                    text.DefaultTextStyle(x => x.FontSize(8.5f));

                    text.Span("El presente acuse de recepción hace constar que la estadística delictiva reportada por la ");
                    text.Span($"Fiscalía General del Estado de {entidad}").Bold();
                    text.Span(" al ");
                    text.Span("Registro Nacional de Incidencia Delictiva (RNID)").Bold();
                    text.Span(" del Sistema Nacional de Seguridad Pública, ha sido enviada de manera satisfactoria a través de la plataforma web, mediante este acuse se confirma la entrega formal de la estadística delictiva del ");
                    text.Span($"Estado de {entidad}").Bold();
                    text.Span(" el día ");
                    text.Span(ObtenerFechaLargaTexto(fechaAcuse)).Bold();
                    text.Span(" a las ");
                    text.Span(fechaAcuse.ToString("HH:mm:ss", cultura)).Bold();
                    text.Span(" horas (Tiempo del centro de México).");
                });

            column.Item()
                .PaddingTop(2)
                .Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8.5f));

                    text.Span("Mes de corte estadístico: ");
                    text.Span($"{ObtenerNombreMes(carga.MesCorte)} de {carga.AnioCorte}").Bold();
                });
        });
    }

    private static string ObtenerFechaLargaEncabezado(DateTime fecha)
    {
        return $"{fecha:dd} de {ObtenerNombreMes(fecha.Month)} de {fecha:yyyy}";
    }

    private static string ObtenerFechaLargaTexto(DateTime fecha)
    {
        var cultura = new CultureInfo("es-MX");
        var nombreMes = cultura.DateTimeFormat.GetMonthName(fecha.Month);

        return $"{fecha.Day} de {nombreMes} de {fecha:yyyy}";
    }
}