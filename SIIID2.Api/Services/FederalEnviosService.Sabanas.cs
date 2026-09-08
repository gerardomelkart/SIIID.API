using System.Diagnostics;
using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.Extensions.Caching.Memory;
using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public partial class FederalEnviosService
{
    public async Task<InformeArchivoZipResponse> GenerarZipSabanasAsync(
        int idUsuarioConsulta,
        int anioCorte,
        string? tipoSabana,
        string? modoPlano)
    {
        var usuario = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuario == null)
            throw new UnauthorizedAccessException("El usuario no tiene acceso activo al módulo Federal.");

        if (anioCorte < 2000 || anioCorte > 2100)
            throw new InvalidOperationException("El año de corte no es válido.");

        var tipo = NormalizarTipoSabana(tipoSabana);
        var modo = NormalizarModoPlano(modoPlano);

        if (!usuario.EsSuperUsuario && modo != "CONFIRMADO")
            throw new UnauthorizedAccessException("Sólo el SUPER_USUARIO puede generar planos previos o mixtos.");

        var firma = await _federalEnviosRepository.ObtenerFirmaSabanaAsync(anioCorte);

        if (!firma.MesUltimoCorte.HasValue)
            throw new InvalidOperationException($"No existe información Federal disponible para el año {anioCorte}.");

        if (modo == "CONFIRMADO" && firma.TotalCargasConfirmadas == 0)
            throw new InvalidOperationException($"No existe información Federal confirmada para el año {anioCorte}.");

        if (modo == "PREVIO" && firma.TotalCargasPendientes == 0)
            throw new InvalidOperationException($"No existen cargas federales pendientes de aprobación para el año {anioCorte}.");

        var mesUltimoCorte = firma.MesUltimoCorte.Value;

        var cacheKey =
            $"FEDERAL_SABANAS:{tipo}:{modo}:{anioCorte}:{mesUltimoCorte}:{firma.UltimoIdCarga}:{firma.TotalCargasConfirmadas}:{firma.TotalCargasPendientes}:{firma.UltimaFechaMovimiento:O}";

        if (_cache.TryGetValue<InformeArchivoZipResponse>(cacheKey, out var cacheado) && cacheado != null)
        {
            _logger.LogInformation(
                "PERFORMANCE_FEDERAL_SABANAS_CACHE_HIT tipo={Tipo} modo={Modo} anio={Anio}",
                tipo,
                modo,
                anioCorte);

            return cacheado;
        }

        var swTotal = Stopwatch.StartNew();
        var swConsultas = Stopwatch.StartNew();

        var tareas =
            new List<(string Archivo, string Hoja, Task<List<IDictionary<string, object?>>> Consulta)>();

        if (tipo is "COMPLETA" or "ESTATALES")
        {
            tareas.Add((
                "estatal-delitos.xlsx",
                "estatal-delitos",
                _federalEnviosRepository.ObtenerSabanaEstatalDelitosAsync(anioCorte, modo, mesUltimoCorte)));

            tareas.Add((
                "estatal-victimas.xlsx",
                "estatal-victimas",
                _federalEnviosRepository.ObtenerSabanaEstatalVictimasAsync(anioCorte, modo, mesUltimoCorte)));
        }

        if (tipo is "COMPLETA" or "MUNICIPALES")
        {
            tareas.Add((
                "municipal-delitos.xlsx",
                "municipal-delitos",
                _federalEnviosRepository.ObtenerSabanaMunicipalDelitosAsync(anioCorte, modo, mesUltimoCorte)));

            tareas.Add((
                "municipal-victimas.xlsx",
                "municipal-victimas",
                _federalEnviosRepository.ObtenerSabanaMunicipalVictimasAsync(anioCorte, modo, mesUltimoCorte)));
        }

        await Task.WhenAll(tareas.Select(x => x.Consulta));

        var resultados = tareas
            .Select(x => (x.Archivo, x.Hoja, Filas: x.Consulta.Result))
            .ToList();

        swConsultas.Stop();

        _logger.LogInformation(
            "PERFORMANCE_FEDERAL_SABANAS_CONSULTAS tipo={Tipo} modo={Modo} anio={Anio} tiempoMs={TiempoMs} archivos={Archivos}",
            tipo,
            modo,
            anioCorte,
            swConsultas.ElapsedMilliseconds,
            string.Join(", ", resultados.Select(x => $"{x.Archivo}:{x.Filas.Count}")));

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var resultado in resultados)
            {
                AgregarExcelPlanoAlZip(
                    archive,
                    resultado.Archivo,
                    resultado.Hoja,
                    resultado.Filas);
            }
        }

        swTotal.Stop();

        _logger.LogInformation(
            "PERFORMANCE_FEDERAL_SABANAS_TOTAL tipo={Tipo} modo={Modo} anio={Anio} tiempoMs={TiempoMs}",
            tipo,
            modo,
            anioCorte,
            swTotal.ElapsedMilliseconds);

        var response = new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = ObtenerNombreZipSabanas(tipo, modo, anioCorte)
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

    private static void AgregarExcelPlanoAlZip(
        ZipArchive archive,
        string nombreArchivo,
        string nombreHoja,
        List<IDictionary<string, object?>> filas)
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
            var nombre = columnas[columna];

            worksheet.Cell(1, columna + 1).Value = nombre;
            worksheet.Cell(1, columna + 1).Style.Font.Bold = true;

            if (nombre == "Clave_Ent")
                worksheet.Column(columna + 1).Style.NumberFormat.Format = "@";
        }

        for (var fila = 0; fila < filas.Count; fila++)
        {
            for (var columna = 0; columna < columnas.Count; columna++)
            {
                var nombre = columnas[columna];

                filas[fila].TryGetValue(nombre, out var valor);

                var celda = worksheet.Cell(fila + 2, columna + 1);

                if (nombre == "Clave_Ent")
                {
                    celda.Style.NumberFormat.Format = "@";
                    celda.Value = valor?.ToString() ?? string.Empty;
                    continue;
                }

                celda.Value = ConvertirValorExcelPlano(valor);
            }
        }

        AplicarAnchosPlano(worksheet, columnas);

        workbook.SaveAs(entryStream);
    }

    private static void AplicarAnchosPlano(
        IXLWorksheet worksheet,
        IReadOnlyList<string> columnas)
    {
        for (var i = 0; i < columnas.Count; i++)
        {
            var nombre = columnas[i];
            var columna = worksheet.Column(i + 1);

            if (nombre.Contains("Entidad", StringComparison.OrdinalIgnoreCase) ||
                nombre.Contains("Municipio", StringComparison.OrdinalIgnoreCase))
            {
                columna.Width = 28;
                continue;
            }

            if (nombre.Contains("Bien jurídico", StringComparison.OrdinalIgnoreCase) ||
                nombre.Contains("Tipo de delito", StringComparison.OrdinalIgnoreCase) ||
                nombre.Contains("Subtipo", StringComparison.OrdinalIgnoreCase) ||
                nombre.Contains("Modalidad", StringComparison.OrdinalIgnoreCase))
            {
                columna.Width = 32;
                continue;
            }

            if (nombre.Contains("Rango", StringComparison.OrdinalIgnoreCase))
            {
                columna.Width = 18;
                continue;
            }

            if (nombre is
                "Enero" or
                "Febrero" or
                "Marzo" or
                "Abril" or
                "Mayo" or
                "Junio" or
                "Julio" or
                "Agosto" or
                "Septiembre" or
                "Octubre" or
                "Noviembre" or
                "Diciembre")
            {
                columna.Width = 12;
                continue;
            }

            columna.Width = 14;
        }

        worksheet.SheetView.FreezeRows(1);
    }

    private static XLCellValue ConvertirValorExcelPlano(object? valor)
    {
        if (valor == null || valor == DBNull.Value)
            return string.Empty;

        if (valor is DateTime fecha)
            return fecha;

        if (valor is int entero)
            return entero;

        if (valor is long enteroLargo)
            return enteroLargo;

        if (valor is decimal numeroDecimal)
            return numeroDecimal;

        if (valor is double numeroDouble)
            return numeroDouble;

        if (valor is float numeroFloat)
            return numeroFloat;

        if (valor is bool booleano)
            return booleano;

        return valor.ToString() ?? string.Empty;
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

    private static string ObtenerNombreZipSabanas(
        string tipo,
        string modo,
        int anioCorte)
    {
        return tipo switch
        {
            "ESTATALES" => $"PLANO_ESTATAL_FEDERAL_{modo}_{anioCorte}.zip",
            "MUNICIPALES" => $"PLANO_MUNICIPAL_FEDERAL_{modo}_{anioCorte}.zip",
            _ => $"PLANO_ESTADISTICO_FEDERAL_{modo}_{anioCorte}.zip"
        };
    }
}