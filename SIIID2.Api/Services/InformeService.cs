using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using System.IO.Compression;
using ClosedXML.Excel;

namespace SIIID2.Api.Services;

public class InformeService : IInformeService
{
    private readonly IInformeRepository _informeRepository;
    private readonly IUsuarioRepository _usuarioRepository;

    public InformeService(IInformeRepository informeRepository, IUsuarioRepository usuarioRepository)
    {
        _informeRepository = informeRepository;
        _usuarioRepository = usuarioRepository;
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

    public async Task<InformeArchivoZipResponse> GenerarZipSabanasAsync(int idUsuarioConsulta, int anioCorte)
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

        var estatalDelitos = await _informeRepository.ObtenerSabanaEstatalDelitosAsync(anioCorte);
        var municipalDelitos = await _informeRepository.ObtenerSabanaMunicipalDelitosAsync(anioCorte);
        var estatalVictimas = await _informeRepository.ObtenerSabanaEstatalVictimasAsync(anioCorte);
        var municipalVictimas = await _informeRepository.ObtenerSabanaMunicipalVictimasAsync(anioCorte);

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AgregarExcelAlZip(
                archive,
                "estatal-delitos.xlsx",
                "estatal-delitos",
                estatalDelitos);

            AgregarExcelAlZip(
                archive,
                "municipal-delitos.xlsx",
                "municipal-delitos",
                municipalDelitos);

            AgregarExcelAlZip(
                archive,
                "estatal-victimas.xlsx",
                "estatal-victimas",
                estatalVictimas);

            AgregarExcelAlZip(
                archive,
                "municipal-victimas.xlsx",
                "municipal-victimas",
                municipalVictimas);
        }

        return new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = $"SABANAS_{anioCorte}.zip"
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

        var columnas = filas
            .First()
            .Keys
            .ToList();

        for (var columna = 0; columna < columnas.Count; columna++)
        {
            worksheet.Cell(1, columna + 1).Value = columnas[columna];
            worksheet.Cell(1, columna + 1).Style.Font.Bold = true;

            if (columnas[columna] == "Clave_Ent" || columnas[columna] == "Cve. Municipio")
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

                if (nombreColumna == "Clave_Ent" || nombreColumna == "Cve. Municipio")
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

        worksheet.Columns().AdjustToContents();

        workbook.SaveAs(entryStream);
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
}