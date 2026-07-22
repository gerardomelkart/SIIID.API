using System.IO.Compression;
using ClosedXML.Excel;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class SemanalAdministracionCargasService : ISemanalAdministracionCargasService
{
    private readonly ISemanalAdministracionCargasRepository _administracionRepository;
    private readonly ISemanalCargaRepository _semanalCargaRepository;

    public SemanalAdministracionCargasService(ISemanalAdministracionCargasRepository administracionRepository, ISemanalCargaRepository semanalCargaRepository)
    {
        _administracionRepository = administracionRepository;
        _semanalCargaRepository = semanalCargaRepository;
    }

    public async Task<List<SemanalCargaPendienteAdministracionItem>> ObtenerPendientesAsync(int idUsuario)
    {
        await ValidarSuperUsuarioSemanalAsync(idUsuario);
        return await _administracionRepository.ObtenerPendientesAsync();
    }

    public async Task<SemanalCargaPendienteAdministracionDetalle?> ObtenerDetalleAsync(int idUsuario, string codigoReferencia)
    {
        await ValidarSuperUsuarioSemanalAsync(idUsuario);
        return await _administracionRepository.ObtenerDetalleAsync(codigoReferencia.Trim());
    }

    public async Task<SemanalCargaReferenciaAdministracionInfo?> ObtenerReferenciaAsync(int idUsuario, string codigoReferencia)
    {
        await ValidarSuperUsuarioSemanalAsync(idUsuario);
        return await _administracionRepository.ObtenerReferenciaAsync(codigoReferencia.Trim());
    }

    public async Task<ConfirmarCargaResponse> AprobarAsync(int idUsuario, string codigoReferencia)
    {
        await ValidarSuperUsuarioSemanalAsync(idUsuario);

        var codigoLimpio = codigoReferencia.Trim();
        var carga = await _administracionRepository.ObtenerReferenciaAsync(codigoLimpio);

        if (carga == null) return Error(codigoLimpio, "NO_ENCONTRADA", "No se encontró una carga semanal con ese código de referencia.");

        if (!string.Equals(carga.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase))
        {
            return Error(codigoLimpio, carga.Estado, $"La carga semanal ya fue resuelta o no está pendiente de aprobación. Estado actual: {carga.Estado}.");
        }

        return await _semanalCargaRepository.AprobarCargaPendienteAsync(codigoLimpio, idUsuario);
    }

    public async Task<ConfirmarCargaResponse> RechazarAsync(int idUsuario, string codigoReferencia, string motivo)
    {
        await ValidarSuperUsuarioSemanalAsync(idUsuario);

        var codigoLimpio = codigoReferencia.Trim();
        var motivoLimpio = motivo?.Trim() ?? string.Empty;

        if (motivoLimpio.Length < 5 || motivoLimpio.Length > 2000)
        {
            return Error(codigoLimpio, "MOTIVO_INVALIDO", "El motivo del rechazo debe tener entre 5 y 2000 caracteres.");
        }

        var carga = await _administracionRepository.ObtenerReferenciaAsync(codigoLimpio);

        if (carga == null) return Error(codigoLimpio, "NO_ENCONTRADA", "No se encontró una carga semanal con ese código de referencia.");

        if (!string.Equals(carga.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase))
        {
            return Error(codigoLimpio, carga.Estado, $"La carga semanal ya fue resuelta o no está pendiente de aprobación. Estado actual: {carga.Estado}.");
        }

        return await _semanalCargaRepository.RechazarCargaPendienteAsync(codigoLimpio, idUsuario, motivoLimpio);
    }

    public async Task<InformeArchivoZipResponse> GenerarZipArchivosPendientesAsync(int idUsuario, string codigoReferencia)
    {
        await ValidarSuperUsuarioSemanalAsync(idUsuario);

        var codigoLimpio = codigoReferencia.Trim();
        var referencia = await _administracionRepository.ObtenerReferenciaAsync(codigoLimpio);

        if (referencia == null) throw new KeyNotFoundException("No se encontró una carga semanal con ese código de referencia.");

        if (!string.Equals(referencia.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"La carga semanal ya no se encuentra pendiente de aprobación. Estado actual: {referencia.Estado}.");
        }

        var detalle = await _administracionRepository.ObtenerDetalleAsync(codigoLimpio);

        if (detalle == null) throw new InvalidOperationException("No fue posible obtener el detalle de la carga semanal pendiente.");

        var carpetas = await _administracionRepository.ObtenerCarpetasPendientesAsync(detalle.IdSemanalCarga);
        var delitos = await _administracionRepository.ObtenerDelitosPendientesAsync(detalle.IdSemanalCarga);
        var victimas = await _administracionRepository.ObtenerVictimasPendientesAsync(detalle.IdSemanalCarga);

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AgregarExcelAlZip(archive, "carpetas.xlsx", "carpetas", carpetas);
            AgregarExcelAlZip(archive, "delitos.xlsx", "delitos", delitos);
            AgregarExcelAlZip(archive, "victimas.xlsx", "victimas", victimas);
        }

        var entidad = NormalizarNombreArchivo(detalle.EntidadFederativa);

        return new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = $"ARCHIVOS_REVISION_SEMANAL_{entidad}_SEMANA_{detalle.NumeroSemana}_{detalle.AnioSemana}.zip"
        };
    }

    private async Task ValidarSuperUsuarioSemanalAsync(int idUsuario)
    {
        var usuario = await _semanalCargaRepository.ObtenerUsuarioCargaAsync(idUsuario);

        if (usuario == null || !usuario.EsSuperUsuario)
        {
            throw new UnauthorizedAccessException("Solo un superusuario con acceso al módulo semanal puede revisar y resolver cargas pendientes.");
        }
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
            worksheet.Column(columna + 1).Style.NumberFormat.Format = "@";
        }

        for (var fila = 0; fila < filas.Count; fila++)
        {
            for (var columna = 0; columna < columnas.Count; columna++)
            {
                var nombreColumna = columnas[columna];
                var valor = filas[fila].TryGetValue(nombreColumna, out var dato) ? dato : null;
                var celda = worksheet.Cell(fila + 2, columna + 1);

                celda.Style.NumberFormat.Format = "@";
                celda.Value = valor?.ToString() ?? string.Empty;
            }
        }

        worksheet.SheetView.FreezeRows(1);
        worksheet.RangeUsed()?.SetAutoFilter();
        workbook.SaveAs(entryStream);
    }

    private static string NormalizarNombreArchivo(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "SIN_DATO";

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

        foreach (var reemplazo in reemplazos) valor = valor.Replace(reemplazo.Key, reemplazo.Value);

        valor = new string(valor.Select(caracter => char.IsLetterOrDigit(caracter) ? caracter : '_').ToArray());

        while (valor.Contains("__")) valor = valor.Replace("__", "_");

        return valor.Trim('_');
    }

    private static ConfirmarCargaResponse Error(string codigoReferencia, string estado, string mensaje) => new()
    {
        EsValido = false,
        CodigoReferencia = codigoReferencia,
        Estado = estado,
        Mensaje = mensaje
    };
}