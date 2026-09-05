using System.IO.Compression;
using ClosedXML.Excel;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class FederalEnviosService : IFederalEnviosService
{
    private readonly IFederalEnviosRepository _federalEnviosRepository;
    private readonly IFederalCargaRepository _federalCargaRepository;
    private readonly IFederalAcusePdfService _acusePdfService;

    public FederalEnviosService(IFederalEnviosRepository federalEnviosRepository, IFederalCargaRepository federalCargaRepository, IFederalAcusePdfService acusePdfService)
    {
        _federalEnviosRepository = federalEnviosRepository;
        _federalCargaRepository = federalCargaRepository;
        _acusePdfService = acusePdfService;
    }

    public async Task<List<InformePeriodoItem>> ObtenerPeriodosAsync(int idUsuarioConsulta)
    {
        var usuario = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);
        if (usuario == null) return [];

        return await _federalEnviosRepository.ObtenerPeriodosAsync();
    }

    public async Task<List<InformeEnvioItem>> ObtenerEnviosAsync(int idUsuarioConsulta, int? mesCorte, int? anioCorte)
    {
        var usuario = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);
        if (usuario == null) return [];

        return await _federalEnviosRepository.ObtenerEnviosAsync(mesCorte, anioCorte);
    }

    public async Task<InformeArchivoZipResponse> GenerarZipArchivosAsync(int idUsuarioConsulta, string codigoReferencia)
    {
        var usuario = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);
        if (usuario == null) throw new UnauthorizedAccessException("El usuario no tiene acceso activo al módulo Federal.");

        var codigo = (codigoReferencia ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(codigo)) throw new InvalidOperationException("Debe indicar el código de referencia.");

        var carga = await _federalEnviosRepository.ObtenerCargaParaArchivosAsync(codigo);
        if (carga == null) throw new KeyNotFoundException("No se encontró el envío Federal solicitado.");

        var esConfirmada =
            string.Equals(carga.Estado, "CONFIRMADO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(carga.Estado, "CONFIRMADO_ACTUALIZACION", StringComparison.OrdinalIgnoreCase);

        List<IDictionary<string, object?>> carpetas;
        List<IDictionary<string, object?>> delitos;
        List<IDictionary<string, object?>> victimas;

        if (esConfirmada)
        {
            carpetas = await _federalEnviosRepository.ObtenerCarpetasConfirmadasPeriodoAsync(carga.MesCorte, carga.AnioCorte);
            delitos = await _federalEnviosRepository.ObtenerDelitosConfirmadosPeriodoAsync(carga.MesCorte, carga.AnioCorte);
            victimas = await _federalEnviosRepository.ObtenerVictimasConfirmadasPeriodoAsync(carga.MesCorte, carga.AnioCorte);
        }
        else
        {
            if (!carga.TieneStagingDisponible) throw new InvalidOperationException("El envío Federal ya no conserva archivos temporales disponibles.");

            carpetas = await _federalEnviosRepository.ObtenerCarpetasStagingAsync(carga.IdCarga);
            delitos = await _federalEnviosRepository.ObtenerDelitosStagingAsync(carga.IdCarga);
            victimas = await _federalEnviosRepository.ObtenerVictimasStagingAsync(carga.IdCarga);
        }

        if (carpetas.Count == 0 && delitos.Count == 0 && victimas.Count == 0)
            throw new InvalidOperationException("No existen registros disponibles para reconstruir los archivos del envío Federal.");

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AgregarExcelAlZip(archive, "carpetas.xlsx", "carpetas", carpetas);
            AgregarExcelAlZip(archive, "delitos.xlsx", "delitos", delitos);
            AgregarExcelAlZip(archive, "victimas.xlsx", "victimas", victimas);
        }

        return new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = $"ARCHIVOS_FEDERAL_{carga.MesCorte:00}_{carga.AnioCorte}_{codigo}.zip"
        };
    }

    public async Task<InformeArchivoZipResponse> GenerarZipAcusesAsync(int idUsuarioConsulta, int mesCorte, int anioCorte)
    {
        if (mesCorte < 1 || mesCorte > 12) throw new InvalidOperationException("El mes de corte no es válido.");
        if (anioCorte < 2000 || anioCorte > 2100) throw new InvalidOperationException("El año de corte no es válido.");

        var usuario = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);
        if (usuario == null) throw new UnauthorizedAccessException("El usuario no tiene acceso activo al módulo Federal.");

        var envios = await _federalEnviosRepository.ObtenerEnviosAsync(mesCorte, anioCorte);

        var enviosConAcuse = envios
            .Where(x =>
                string.Equals(x.TipoCarga, "CARGA_INICIAL", StringComparison.OrdinalIgnoreCase) &&
                x.Estado is "VALIDADO_PENDIENTE" or "PENDIENTE_APROBACION" or "CONFIRMADO")
            .ToList();

        if (enviosConAcuse.Count == 0)
            throw new InvalidOperationException($"No existen acuses federales disponibles para {ObtenerNombreMes(mesCorte)} de {anioCorte}.");

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var envio in enviosConAcuse)
            {
                var esConfirmado = string.Equals(envio.Estado, "CONFIRMADO", StringComparison.OrdinalIgnoreCase);

                var pdf = esConfirmado
                    ? await _acusePdfService.GenerarAcuseConfirmadoAsync(envio.CodigoReferencia, idUsuarioConsulta)
                    : await _acusePdfService.GenerarAcusePrevioAsync(envio.CodigoReferencia, idUsuarioConsulta);

                var tipoDocumento = esConfirmado ? "ACUSE" : "INFORME_PREVIO";
                var entry = archive.CreateEntry($"FGR_{tipoDocumento}_{envio.CodigoReferencia}.pdf", CompressionLevel.Fastest);

                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(pdf);
            }
        }

        return new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = $"ACUSES_FEDERAL_{NormalizarNombreArchivo(ObtenerNombreMes(mesCorte))}_{anioCorte}.zip"
        };
    }

    private static void AgregarExcelAlZip(ZipArchive archive, string nombreArchivo, string nombreHoja, List<IDictionary<string, object?>> filas)
    {
        var entry = archive.CreateEntry(nombreArchivo, CompressionLevel.Fastest);

        using var entryStream = entry.Open();
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(nombreHoja);

        if (filas.Count == 0)
        {
            worksheet.Cell(1, 1).Value = "Sin registros";
            workbook.SaveAs(entryStream);
            return;
        }

        var columnas = filas.First().Keys.ToList();

        for (var columna = 0; columna < columnas.Count; columna++)
        {
            worksheet.Cell(1, columna + 1).Value = columnas[columna];
            worksheet.Cell(1, columna + 1).Style.Font.Bold = true;
            worksheet.Column(columna + 1).Width = columnas[columna] is "rmen_de_hchos" or "dom_hchos" ? 40 : 18;
        }

        for (var fila = 0; fila < filas.Count; fila++)
        {
            for (var columna = 0; columna < columnas.Count; columna++)
            {
                var nombreColumna = columnas[columna];
                filas[fila].TryGetValue(nombreColumna, out var valor);

                worksheet.Cell(fila + 2, columna + 1).Style.NumberFormat.Format = "@";
                worksheet.Cell(fila + 2, columna + 1).Value = valor?.ToString() ?? string.Empty;
            }
        }

        worksheet.SheetView.FreezeRows(1);
        workbook.SaveAs(entryStream);
    }

    private static string ObtenerNombreMes(int mes)
    {
        return mes switch
        {
            1 => "Enero",
            2 => "Febrero",
            3 => "Marzo",
            4 => "Abril",
            5 => "Mayo",
            6 => "Junio",
            7 => "Julio",
            8 => "Agosto",
            9 => "Septiembre",
            10 => "Octubre",
            11 => "Noviembre",
            12 => "Diciembre",
            _ => mes.ToString("00")
        };
    }

    private static string NormalizarNombreArchivo(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return string.Empty;

        valor = valor.Trim().ToUpperInvariant();

        foreach (var reemplazo in new Dictionary<char, char>
        {
            { 'Á', 'A' },
            { 'É', 'E' },
            { 'Í', 'I' },
            { 'Ó', 'O' },
            { 'Ú', 'U' },
            { 'Ü', 'U' },
            { 'Ñ', 'N' }
        })
        {
            valor = valor.Replace(reemplazo.Key, reemplazo.Value);
        }

        valor = new string(valor.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        while (valor.Contains("__")) valor = valor.Replace("__", "_");

        return valor.Trim('_');
    }
}