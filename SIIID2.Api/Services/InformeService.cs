using ClosedXML.Excel;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Extensions.Caching.Memory;

namespace SIIID2.Api.Services;

public class InformeService : IInformeService
{
    private readonly IInformeRepository _informeRepository;
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly ILogger<InformeService> _logger;
    private readonly IMemoryCache _cache;

    public InformeService(IInformeRepository informeRepository, IUsuarioRepository usuarioRepository, ILogger<InformeService> logger, IMemoryCache cache)
    {
        _informeRepository = informeRepository;
        _usuarioRepository = usuarioRepository;
        _logger = logger;
        _cache = cache;
    }

    public async Task<List<InformeEnvioItem>> ObtenerEnviosAsync(int idUsuarioConsulta, int? idEntidadFederativa, int? mesCorte, int? anioCorte)
    {
        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            return new List<InformeEnvioItem>();
        }

        if (!usuarioConsulta.EsSuperUsuario && !usuarioConsulta.IdEntidadFederativa.HasValue)
        {
            return new List<InformeEnvioItem>();
        }

        return await _informeRepository.ObtenerEnviosAsync(
            usuarioConsulta.EsSuperUsuario,
            usuarioConsulta.IdEntidadFederativa,
            idEntidadFederativa,
            mesCorte,
            anioCorte);
    }

    public async Task<InformeArchivoZipResponse> GenerarZipArchivosEnvioAsync(string codigoReferencia, int idUsuarioConsulta)
    {
        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            throw new UnauthorizedAccessException("El usuario autenticado no existe o no está activo.");
        }

        var carga = await _informeRepository.ObtenerCargaConfirmadaParaArchivosAsync(codigoReferencia);

        if (carga == null)
        {
            throw new InvalidOperationException("No se encontró una carga o actualización confirmada para el código de referencia indicado.");
        }

        if (!usuarioConsulta.EsSuperUsuario)
        {
            if (!usuarioConsulta.IdEntidadFederativa.HasValue || usuarioConsulta.IdEntidadFederativa.Value != carga.IdEntidadFederativa)
            {
                throw new UnauthorizedAccessException("No tiene permiso para descargar archivos de otra entidad federativa.");
            }
        }

        var carpetas = await _informeRepository.ObtenerCarpetasConfirmadasPeriodoAsync(carga);
        var delitos = await _informeRepository.ObtenerDelitosConfirmadosPeriodoAsync(carga);
        var victimas = await _informeRepository.ObtenerVictimasConfirmadasPeriodoAsync(carga);

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AgregarExcelAlZip(
                archive,
                "carpetas.xlsx",
                "carpetas",
                carpetas);

            AgregarExcelAlZip(
                archive,
                "delitos.xlsx",
                "delitos",
                delitos);

            AgregarExcelAlZip(
                archive,
                "victimas.xlsx",
                "victimas",
                victimas);
        }

        return new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = GenerarNombreArchivoZip(carga)
        };
    }

    public async Task<InformeArchivoZipResponse> GenerarZipSabanasAsync(int idUsuarioConsulta, int anioCorte, string? tipoSabana)
    {
        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            throw new UnauthorizedAccessException("El usuario autenticado no existe o no está activo.");
        }

        if (!usuarioConsulta.EsSuperUsuario)
        {
            throw new UnauthorizedAccessException("Solo un SUPER_USUARIO puede descargar las sábanas estadísticas.");
        }

        if (anioCorte < 2000 || anioCorte > 2100)
        {
            throw new InvalidOperationException("El año de corte no es válido.");
        }

        var tipo = NormalizarTipoSabana(tipoSabana);
        var firma = await _informeRepository.ObtenerFirmaSabanaAsync(anioCorte);
        var cacheKey = $"SABANAS:{tipo}:{anioCorte}:{firma.UltimoIdCarga}:{firma.TotalCargasConfirmadas}:{firma.UltimaFechaMovimiento:O}";

        if (_cache.TryGetValue<InformeArchivoZipResponse>(cacheKey, out var sabanasCacheadas))
        {
            _logger.LogInformation("PERFORMANCE_SABANAS_CACHE_HIT tipo={TipoSabana} anio={AnioCorte} key={CacheKey}", tipo, anioCorte, cacheKey);
            return sabanasCacheadas!;
        }

        var swTotal = Stopwatch.StartNew();
        var swConsultas = Stopwatch.StartNew();
        var tareas = new List<(string Archivo, string Hoja, Task<List<IDictionary<string, object?>>> Consulta)>();

        if (tipo is "COMPLETA" or "ESTATALES")
        {
            tareas.Add(("estatal-delitos.xlsx", "estatal-delitos", _informeRepository.ObtenerSabanaEstatalDelitosAsync(anioCorte)));
            tareas.Add(("estatal-victimas.xlsx", "estatal-victimas", _informeRepository.ObtenerSabanaEstatalVictimasAsync(anioCorte)));
        }

        if (tipo is "COMPLETA" or "MUNICIPALES")
        {
            tareas.Add(("municipal-delitos.xlsx", "municipal-delitos", _informeRepository.ObtenerSabanaMunicipalDelitosAsync(anioCorte)));
            tareas.Add(("municipal-victimas.xlsx", "municipal-victimas", _informeRepository.ObtenerSabanaMunicipalVictimasAsync(anioCorte)));
        }

        await Task.WhenAll(tareas.Select(x => x.Consulta));

        var resultados = tareas.Select(x => (x.Archivo, x.Hoja, Filas: x.Consulta.Result)).ToList();

        swConsultas.Stop();

        _logger.LogInformation("PERFORMANCE_SABANAS_CONSULTAS tipo={TipoSabana} anio={AnioCorte} tiempoMs={TiempoMs} archivos={Archivos}",
            tipo, anioCorte, swConsultas.ElapsedMilliseconds, string.Join(", ", resultados.Select(x => $"{x.Archivo}:{x.Filas.Count}")));

        var swZip = Stopwatch.StartNew();

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var resultado in resultados)
            {
                AgregarExcelAlZip(archive, resultado.Archivo, resultado.Hoja, resultado.Filas);
            }
        }

        swZip.Stop();
        swTotal.Stop();

        _logger.LogInformation("PERFORMANCE_SABANAS_ZIP tipo={TipoSabana} anio={AnioCorte} tiempoMs={TiempoMs}", tipo, anioCorte, swZip.ElapsedMilliseconds);
        _logger.LogInformation("PERFORMANCE_SABANAS_TOTAL tipo={TipoSabana} anio={AnioCorte} tiempoMs={TiempoMs}", tipo, anioCorte, swTotal.ElapsedMilliseconds);

        var response = new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = ObtenerNombreZipSabanas(tipo, anioCorte)
        };

        _cache.Set(
            cacheKey,
            response,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(30),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4)
            });

        return response;
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

        var columnas = filas
            .First()
            .Keys
            .ToList();

        for (var columna = 0; columna < columnas.Count; columna++)
        {
            worksheet.Cell(1, columna + 1).Value = columnas[columna];
            worksheet.Cell(1, columna + 1).Style.Font.Bold = true;

            if (columnas[columna] == "Clave_Ent")
            {
                worksheet.Column(columna + 1).Style.NumberFormat.Format = "@";
            }
        }

        for (var fila = 0; fila < filas.Count; fila++)
        {
            for (var columna = 0; columna < columnas.Count; columna++)
            {
                var nombreColumna = columnas[columna];
                var valor = filas[fila].TryGetValue(nombreColumna, out var dato)
                    ? dato
                    : null;

                var celda = worksheet.Cell(fila + 2, columna + 1);

                if (nombreColumna == "Clave_Ent")
                {
                    celda.Style.NumberFormat.Format = "@";
                    celda.Value = valor?.ToString() ?? string.Empty;
                }
                else
                {
                    celda.Value = ConvertirValorExcel(valor);
                }
            }
        }

        AplicarAnchosBasicos(worksheet, columnas);

        workbook.SaveAs(entryStream);
    }

    private static void AplicarAnchosBasicos(IXLWorksheet worksheet, IReadOnlyList<string> columnas)
    {
        for (var i = 0; i < columnas.Count; i++)
        {
            var nombreColumna = columnas[i];
            var columna = worksheet.Column(i + 1);

            if (nombreColumna.Contains("Entidad", StringComparison.OrdinalIgnoreCase) ||
                nombreColumna.Contains("Municipio", StringComparison.OrdinalIgnoreCase))
            {
                columna.Width = 28;
                continue;
            }

            if (nombreColumna.Contains("Bien jurídico", StringComparison.OrdinalIgnoreCase) ||
                nombreColumna.Contains("Tipo de delito", StringComparison.OrdinalIgnoreCase) ||
                nombreColumna.Contains("Subtipo", StringComparison.OrdinalIgnoreCase) ||
                nombreColumna.Contains("Modalidad", StringComparison.OrdinalIgnoreCase))
            {
                columna.Width = 32;
                continue;
            }

            if (nombreColumna.Contains("Rango", StringComparison.OrdinalIgnoreCase))
            {
                columna.Width = 18;
                continue;
            }

            if (nombreColumna is "Enero" or "Febrero" or "Marzo" or "Abril" or "Mayo" or "Junio"
                or "Julio" or "Agosto" or "Septiembre" or "Octubre" or "Noviembre" or "Diciembre")
            {
                columna.Width = 12;
                continue;
            }

            columna.Width = 14;
        }

        worksheet.SheetView.FreezeRows(1);
    }

    private static XLCellValue ConvertirValorExcel(object? valor)
    {
        if (valor == null || valor == DBNull.Value)
        {
            return string.Empty;
        }

        if (valor is DateTime fecha)
        {
            return fecha;
        }

        if (valor is int entero)
        {
            return entero;
        }

        if (valor is long enteroLargo)
        {
            return enteroLargo;
        }

        if (valor is decimal numeroDecimal)
        {
            return numeroDecimal;
        }

        if (valor is double numeroDouble)
        {
            return numeroDouble;
        }

        if (valor is float numeroFloat)
        {
            return numeroFloat;
        }

        if (valor is bool booleano)
        {
            return booleano;
        }

        return valor.ToString() ?? string.Empty;
    }

    private static string GenerarNombreArchivoZip(InformeArchivoCargaInfo carga)
    {
        var entidad = NormalizarNombreArchivo(carga.EntidadFederativa);
        var mes = NormalizarNombreArchivo(ObtenerNombreMes(carga.MesCorte));

        return $"ARCHIVOS_ENVIO_{entidad}_{mes}_{carga.AnioCorte}.zip";
    }

    private static string NormalizarNombreArchivo(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return string.Empty;
        }

        valor = valor.Trim().ToUpperInvariant();

        var reemplazos = new Dictionary<char, char>
    {
        { 'Á', 'A' },
        { 'É', 'E' },
        { 'Í', 'I' },
        { 'Ó', 'O' },
        { 'Ú', 'U' },
        { 'Ü', 'U' },
        { 'Ñ', 'N' }
    };

        foreach (var reemplazo in reemplazos)
        {
            valor = valor.Replace(reemplazo.Key, reemplazo.Value);
        }

        var caracteres = valor
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();

        valor = new string(caracteres);

        while (valor.Contains("__"))
        {
            valor = valor.Replace("__", "_");
        }

        return valor.Trim('_');
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
            _ => string.Empty
        };
    }

    public async Task<List<InformeReporteCargaItem>> ObtenerReporteCargasAsync(int idUsuarioConsulta, int? idEntidadFederativa, int? mesCorte, int? anioCorte)
    {
        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            throw new UnauthorizedAccessException("El usuario autenticado no existe o no está activo.");
        }

        if (!usuarioConsulta.EsSuperUsuario)
        {
            throw new UnauthorizedAccessException("Solo un SUPER_USUARIO puede consultar el reporte de cargas.");
        }

        if (mesCorte.HasValue && (mesCorte.Value < 1 || mesCorte.Value > 12))
        {
            return new List<InformeReporteCargaItem>();
        }

        if (anioCorte.HasValue && (anioCorte.Value < 2000 || anioCorte.Value > 2100))
        {
            return new List<InformeReporteCargaItem>();
        }

        return await _informeRepository.ObtenerReporteCargasAsync(
            idEntidadFederativa,
            mesCorte,
            anioCorte);
    }

    private static string NormalizarTipoSabana(string? tipoSabana)
    {
        var tipo = (tipoSabana ?? "COMPLETA").Trim().ToUpperInvariant();

        return tipo switch
        {
            "COMPLETA" => "COMPLETA",
            "ESTATALES" => "ESTATALES",
            "MUNICIPALES" => "MUNICIPALES",
            _ => throw new InvalidOperationException("El tipo de sábana no es válido.")
        };
    }

    private static string ObtenerNombreZipSabanas(string tipoSabana, int anioCorte)
    {
        return tipoSabana switch
        {
            "ESTATALES" => $"SABANAS_ESTATALES_{anioCorte}.zip",
            "MUNICIPALES" => $"SABANAS_MUNICIPALES_{anioCorte}.zip",
            _ => $"SABANAS_COMPLETAS_{anioCorte}.zip"
        };
    }
}