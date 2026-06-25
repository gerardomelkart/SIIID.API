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
            "INFORME PREVIO",
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
            "",
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
            "INFORME PREVIO DE ACTUALIZACIÓN",
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
            "",
            mostrarMarcaPrevio: false);
    }

    private byte[] GenerarPdf(CargaAcuseInfo carga, List<CargaAcuseResumenItem> resumen, string titulo, bool mostrarMarcaPrevio)
    {
        var totalDelitos = resumen.Sum(x => x.TotalDelitos);
        var totalVictimas = resumen.Sum(x => x.TotalVictimas);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);

                page.MarginTop(20);
                page.MarginLeft(20);
                page.MarginRight(20);
                page.MarginBottom(15);

                page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                if (mostrarMarcaPrevio)
                {
                    page.Background().Element(contenedor => ConstruirMarcaAgua(contenedor));
                }

                page.Header().Element(header => ConstruirEncabezado(header));

                page.Content().Column(column =>
                {
                    column.Spacing(6);

                    if (!string.IsNullOrWhiteSpace(titulo))
                    {
                        column.Item()
                            .PaddingTop(4)
                            .AlignCenter()
                            .Text(titulo)
                            .FontSize(17)
                            .Bold();
                    }

                    if (mostrarMarcaPrevio)
                    {
                        column.Item().Element(contenedor => ConstruirDatosGeneralesPrevio(contenedor, carga));
                    }
                    else
                    {
                        column.Item().Element(contenedor => ConstruirLeyendaAcuseConfirmado(contenedor, carga));
                    }

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
                row.RelativeItem(3)
                    .Height(55)
                    .AlignLeft()
                    .Image(rutaLogo)
                    .FitArea();

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

    private void ConstruirDatosGeneralesPrevio(IContainer container, CargaAcuseInfo carga)
    {
        container.Column(column =>
        {
            column.Item()
                .Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(7.5f));
                    text.Span("\n");
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

    private void ConstruirLeyendaAcuseConfirmado(IContainer container, CargaAcuseInfo carga)
    {
        var fechaAcuse = carga.FechaConfirmacion ?? carga.FechaValidacion;

        container.Column(column =>
        {
            column.Spacing(6);

            column.Item()
                .PaddingTop(4)
                .AlignCenter()
                .Text("ACUSE DE ENTREGA DE INFORMACIÓN")
                .FontSize(13)
                .Bold();

            column.Item()
                .Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8.5f));

                    text.Span("Mediante este documento, se confirma que la información proporcionada ha sido enviada de manera satisfactoria a través de nuestra plataforma web. ");
                    text.Span("Queda así registrada su recepción formal, garantizando que los datos ingresados han sido recibidos y procesados conforme a los protocolos establecidos. ");
                    text.Span("Para cualquier consulta o verificación posterior, este acuse servirá como constancia válida del envío realizado por parte de la entidad de ");
                    text.Span(carga.EntidadFederativa).Bold();
                    text.Span(", el día ");
                    text.Span(ObtenerFechaLarga(fechaAcuse)).Bold();
                    text.Span(", a las ");
                    text.Span(fechaAcuse.ToString("HH:mm:ss", new CultureInfo("es-MX"))).Bold();
                    text.Span(" horas.");
                });

            column.Item()
                .PaddingTop(4)
                .Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8.5f));

                    text.Span("Mes de Corte: ").Bold();
                    text.Span(ObtenerNombreMes(carga.MesCorte));

                    text.Span("    |    ");

                    text.Span("Entidad: ").Bold();
                    text.Span(carga.EntidadFederativa);
                });

            column.Item()
                .Text(text =>
                {
                    text.DefaultTextStyle(x => x.FontSize(8.5f));

                    text.Span("Periodo: ").Bold();
                    text.Span($"{carga.MesCorte:00}/{carga.AnioCorte}");

                    text.Span("    |    ");

                    text.Span("Fecha validación: ").Bold();
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

    private static string ObtenerFechaLarga(DateTime fecha)
    {
        return $"{fecha:dd} de {ObtenerNombreMes(fecha.Month)} de {fecha:yyyy}";
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