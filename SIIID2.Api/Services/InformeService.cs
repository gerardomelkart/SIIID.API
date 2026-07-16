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
    private readonly IAcusePdfService _acusePdfService;

    public InformeService(IInformeRepository informeRepository, IUsuarioRepository usuarioRepository, IAcusePdfService acusePdfService, ILogger<InformeService> logger, IMemoryCache cache)
    {
        _informeRepository = informeRepository;
        _usuarioRepository = usuarioRepository;
        _acusePdfService = acusePdfService;
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
            throw new InvalidOperationException("No se encontró una carga o actualización activa para el código de referencia indicado.");
        }

        if (!usuarioConsulta.EsSuperUsuario)
        {
            if (!usuarioConsulta.IdEntidadFederativa.HasValue || usuarioConsulta.IdEntidadFederativa.Value != carga.IdEntidadFederativa)
            {
                throw new UnauthorizedAccessException("No tiene permiso para descargar archivos de otra entidad federativa.");
            }
        }

        var esConfirmada =
            string.Equals(carga.Estado, "CONFIRMADO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(carga.Estado, "CONFIRMADO_ACTUALIZACION", StringComparison.OrdinalIgnoreCase);

        List<IDictionary<string, object?>> carpetas;
        List<IDictionary<string, object?>> delitos;
        List<IDictionary<string, object?>> victimas;

        if (esConfirmada)
        {
            carpetas = await _informeRepository.ObtenerCarpetasConfirmadasPeriodoAsync(carga);
            delitos = await _informeRepository.ObtenerDelitosConfirmadosPeriodoAsync(carga);
            victimas = await _informeRepository.ObtenerVictimasConfirmadasPeriodoAsync(carga);
        }
        else
        {
            carpetas = await _informeRepository.ObtenerCarpetasPendientesAsync(carga.IdCarga);
            delitos = await _informeRepository.ObtenerDelitosPendientesAsync(carga.IdCarga);
            victimas = await _informeRepository.ObtenerVictimasPendientesAsync(carga.IdCarga);
        }

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
            NombreArchivo = $"ARCHIVOS_{NormalizarNombreArchivo(carga.EntidadFederativa)}_{carga.MesCorte:00}_{carga.AnioCorte}.zip"
        };
    }

    public async Task<InformeArchivoZipResponse> GenerarZipAcusesEnviosAsync(int idUsuarioConsulta, int mesCorte, int anioCorte)
    {
        if (mesCorte < 1 || mesCorte > 12)
        {
            throw new InvalidOperationException("El mes de corte no es válido.");
        }

        if (anioCorte < 2000 || anioCorte > 2100)
        {
            throw new InvalidOperationException("El año de corte no es válido.");
        }

        var envios = await ObtenerEnviosAsync(idUsuarioConsulta, null, mesCorte, anioCorte);

        var enviosConAcuse = envios
            .Where(x => x.Estado is
                "VALIDADO_PENDIENTE" or
                "VALIDADO_PENDIENTE_ACTUALIZACION" or
                "PENDIENTE_APROBACION" or
                "CONFIRMADO" or
                "CONFIRMADO_ACTUALIZACION")
            .ToList();

        if (enviosConAcuse.Count == 0)
        {
            throw new InvalidOperationException($"No existen acuses disponibles para {ObtenerNombreMes(mesCorte)} de {anioCorte}.");
        }

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var envio in enviosConAcuse)
            {
                var esActualizacion = string.Equals(envio.TipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase);
                var esConfirmado = string.Equals(envio.Estado, "CONFIRMADO", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(envio.Estado, "CONFIRMADO_ACTUALIZACION", StringComparison.OrdinalIgnoreCase);

                byte[] pdf;

                if (esConfirmado)
                {
                    pdf = esActualizacion
                        ? await _acusePdfService.GenerarAcuseConfirmadoActualizacionAsync(envio.CodigoReferencia, idUsuarioConsulta)
                        : await _acusePdfService.GenerarAcuseConfirmadoAsync(envio.CodigoReferencia, idUsuarioConsulta);
                }
                else
                {
                    pdf = esActualizacion
                        ? await _acusePdfService.GenerarAcusePrevioActualizacionAsync(envio.CodigoReferencia, idUsuarioConsulta)
                        : await _acusePdfService.GenerarAcusePrevioAsync(envio.CodigoReferencia, idUsuarioConsulta);
                }

                var tipoDocumento = esConfirmado ? "ACUSE" : "INFORME_PREVIO";
                var entidad = NormalizarNombreArchivo(envio.EntidadFederativa);
                var nombreArchivo = $"{envio.ClaveEntidad}_{entidad}_{tipoDocumento}_{envio.CodigoReferencia}.pdf";
                var entry = archive.CreateEntry(nombreArchivo, CompressionLevel.Fastest);

                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(pdf);
            }
        }

        var mes = NormalizarNombreArchivo(ObtenerNombreMes(mesCorte));

        return new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = $"ACUSES_{mes}_{anioCorte}.zip"
        };
    }

    public async Task<InformeArchivoZipResponse> GenerarZipSabanasAsync(int idUsuarioConsulta, int anioCorte, string? tipoSabana, string? modoPlano)
    {
        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            throw new UnauthorizedAccessException("El usuario autenticado no existe o no está activo.");
        }

        var rol = (usuarioConsulta.Rol ?? string.Empty).Trim().ToUpperInvariant();

        var puedeDescargarSabana =
            usuarioConsulta.EsSuperUsuario ||
            rol == "ENLACE_ESTATAL" ||
            rol == "CONSULTA";

        if (!puedeDescargarSabana)
        {
            throw new UnauthorizedAccessException("No tiene permiso para descargar los planos estadísticos.");
        }

        if (!usuarioConsulta.EsSuperUsuario && !usuarioConsulta.IdEntidadFederativa.HasValue)
        {
            throw new UnauthorizedAccessException("El usuario no tiene una entidad federativa asignada.");
        }

        var idEntidadFederativaFiltro = usuarioConsulta.EsSuperUsuario
            ? null
            : usuarioConsulta.IdEntidadFederativa;

        if (anioCorte < 2000 || anioCorte > 2100)
        {
            throw new InvalidOperationException("El año de corte no es válido.");
        }

        var tipo = NormalizarTipoSabana(tipoSabana);

        var modo = NormalizarModoPlano(modoPlano);

        if (!usuarioConsulta.EsSuperUsuario && modo != "CONFIRMADO")
        {
            throw new UnauthorizedAccessException("Sólo el SUPER_USUARIO puede generar planos previos o mixtos.");
        }
        var firma = await _informeRepository.ObtenerFirmaSabanaAsync(anioCorte, idEntidadFederativaFiltro);

        if (!firma.MesUltimoCorte.HasValue)
        {
            throw new InvalidOperationException($"No existe información disponible para el año {anioCorte}.");
        }

        if (modo == "CONFIRMADO" && firma.TotalCargasConfirmadas == 0)
        {
            throw new InvalidOperationException($"No existe información confirmada para el año {anioCorte}.");
        }

        if (modo == "PREVIO" && firma.TotalCargasPendientes == 0)
        {
            throw new InvalidOperationException($"No existen cargas pendientes de aprobación para el año {anioCorte}.");
        }

        var mesUltimoCorte = firma.MesUltimoCorte.Value;


        var cacheScope = idEntidadFederativaFiltro.HasValue
            ? $"ENTIDAD:{idEntidadFederativaFiltro.Value}"
            : "NACIONAL";

        var cacheKey = $"SABANAS:{cacheScope}:{tipo}:{modo}:{anioCorte}:{mesUltimoCorte}:{firma.UltimoIdCarga}:{firma.TotalCargasConfirmadas}:{firma.TotalCargasPendientes}:{firma.UltimaFechaMovimiento:O}";

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
            tareas.Add(("estatal-delitos.xlsx", "estatal-delitos", _informeRepository.ObtenerSabanaEstatalDelitosAsync(anioCorte, idEntidadFederativaFiltro, modo, mesUltimoCorte)));
            tareas.Add(("estatal-victimas.xlsx", "estatal-victimas", _informeRepository.ObtenerSabanaEstatalVictimasAsync(anioCorte, idEntidadFederativaFiltro, modo, mesUltimoCorte)));
        }

        if (tipo is "COMPLETA" or "MUNICIPALES")
        {
            tareas.Add(("municipal-delitos.xlsx", "municipal-delitos", _informeRepository.ObtenerSabanaMunicipalDelitosAsync(anioCorte, idEntidadFederativaFiltro, modo, mesUltimoCorte)));
            tareas.Add(("municipal-victimas.xlsx", "municipal-victimas", _informeRepository.ObtenerSabanaMunicipalVictimasAsync(anioCorte, idEntidadFederativaFiltro, modo, mesUltimoCorte)));
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
            NombreArchivo = ObtenerNombreZipSabanas(tipo, anioCorte, idEntidadFederativaFiltro, modo)
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

    private static string NormalizarModoPlano(string? modoPlano)
    {
        var modo = (modoPlano ?? "CONFIRMADO").Trim().ToUpperInvariant();

        return modo switch
        {
            "PREVIO" => "PREVIO",
            "MIXTO" => "MIXTO",
            _ => "CONFIRMADO"
        };
    }

    private static string NormalizarTipoSabana(string? tipoSabana)
    {
        var tipo = (tipoSabana ?? "COMPLETA").Trim().ToUpperInvariant();

        return tipo switch
        {
            "COMPLETA" => "COMPLETA",
            "ESTATALES" => "ESTATALES",
            "MUNICIPALES" => "MUNICIPALES",
            _ => throw new InvalidOperationException("El tipo de plano no es válido.")
        };
    }

    private static string ObtenerNombreZipSabanas(string tipo, int anioCorte, int? idEntidadFederativa, string modo)
    {
        var sufijoEntidad = idEntidadFederativa.HasValue ? $"_ENTIDAD_{idEntidadFederativa.Value:00}" : string.Empty;

        return tipo switch
        {
            "ESTATALES" => $"PLANO_ESTATAL_{modo}{sufijoEntidad}_{anioCorte}.zip",
            "MUNICIPALES" => $"PLANO_MUNICIPAL_{modo}{sufijoEntidad}_{anioCorte}.zip",
            _ => $"PLANO_ESTADISTICO_{modo}{sufijoEntidad}_{anioCorte}.zip"
        };
    }
}