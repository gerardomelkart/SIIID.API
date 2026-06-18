using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using ClosedXML.Excel;
using System.IO.Compression;

namespace SIIID2.Api.Services;

public class AdministracionCargasService : IAdministracionCargasService
{
    private readonly IAdministracionCargasRepository _administracionRepository;

    private readonly IUsuarioRepository _usuarioRepository;

    private readonly ICargaRepository _cargaRepository;

    private readonly IActualizacionRepository _actualizacionRepository;

    public AdministracionCargasService(IAdministracionCargasRepository administracionRepository, IUsuarioRepository usuarioRepository, ICargaRepository cargaRepository, IActualizacionRepository actualizacionRepository)
    {
        _administracionRepository = administracionRepository;

        _usuarioRepository = usuarioRepository;

        _cargaRepository = cargaRepository;

        _actualizacionRepository =  actualizacionRepository;
    }

    public async Task<List<CargaPendienteAdministracionItem>> ObtenerPendientesAsync(int idUsuario)
    {
        await ValidarSuperUsuarioAsync(idUsuario);

        return await _administracionRepository.ObtenerPendientesAsync();
    }

    public async Task<CargaPendienteAdministracionDetalle?> ObtenerDetalleAsync(int idUsuario, string codigoReferencia)
    {
        await ValidarSuperUsuarioAsync(idUsuario);

        return await _administracionRepository.ObtenerDetalleAsync(codigoReferencia);
    }

    public async Task<ConfirmarCargaResponse> AprobarAsync(int idUsuario, string codigoReferencia)
    {
        await ValidarSuperUsuarioAsync(idUsuario);

        var carga = await _administracionRepository.ObtenerReferenciaAsync(codigoReferencia);

        if (carga == null)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                CodigoReferencia = codigoReferencia,
                Estado = "NO_ENCONTRADA",
                Mensaje =
                    "No se encontro una carga con ese codigo de referencia."
            };
        }

        if (!string.Equals(
                carga.Estado,
                "PENDIENTE_APROBACION",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                CodigoReferencia = codigoReferencia,
                Estado = carga.Estado,
                Mensaje =
                    $"La carga ya fue resuelta o no esta pendiente de aprobacion. Estado actual: {carga.Estado}."
            };
        }

        if (string.Equals(
                carga.TipoCarga,
                "CARGA_INICIAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return await _cargaRepository
                .AprobarCargaPendienteAsync(
                    codigoReferencia,
                    idUsuario);
        }

        if (string.Equals(
                carga.TipoCarga,
                "ACTUALIZACION",
                StringComparison.OrdinalIgnoreCase))
        {
            return await _actualizacionRepository
                .AprobarActualizacionPendienteAsync(
                    codigoReferencia,
                    idUsuario);
        }

        return new ConfirmarCargaResponse
        {
            EsValido = false,
            CodigoReferencia = codigoReferencia,
            Estado = "TIPO_CARGA_INVALIDO",
            Mensaje = "El tipo de carga no es valido."
        };
    }

    public async Task<ConfirmarCargaResponse> RechazarAsync(int idUsuario, string codigoReferencia, string motivo)
    {
        await ValidarSuperUsuarioAsync(idUsuario);

        var motivoLimpio = motivo?.Trim() ?? string.Empty;

        if (motivoLimpio.Length < 5)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                CodigoReferencia = codigoReferencia,
                Estado = "MOTIVO_INVALIDO",
                Mensaje =
                    "Debe capturar un motivo de rechazo valido."
            };
        }

        var carga = await _administracionRepository
            .ObtenerReferenciaAsync(codigoReferencia);

        if (carga == null)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                CodigoReferencia = codigoReferencia,
                Estado = "NO_ENCONTRADA",
                Mensaje =
                    "No se encontro una carga con ese codigo de referencia."
            };
        }

        if (!string.Equals(
                carga.Estado,
                "PENDIENTE_APROBACION",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                CodigoReferencia = codigoReferencia,
                Estado = carga.Estado,
                Mensaje =
                    $"La carga ya fue resuelta o no esta pendiente de aprobacion. Estado actual: {carga.Estado}."
            };
        }

        if (string.Equals(
                carga.TipoCarga,
                "CARGA_INICIAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return await _cargaRepository
                .RechazarCargaPendienteAsync(
                    codigoReferencia,
                    idUsuario,
                    motivoLimpio);
        }

        if (string.Equals(
                carga.TipoCarga,
                "ACTUALIZACION",
                StringComparison.OrdinalIgnoreCase))
        {
            return await _actualizacionRepository
                .RechazarActualizacionPendienteAsync(
                    codigoReferencia,
                    idUsuario,
                    motivoLimpio);
        }

        return new ConfirmarCargaResponse
        {
            EsValido = false,
            CodigoReferencia = codigoReferencia,
            Estado = "TIPO_CARGA_INVALIDO",
            Mensaje = "El tipo de carga no es valido."
        };
    }

    private async Task ValidarSuperUsuarioAsync(int idUsuario)
    {
        var usuario = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuario);

        if (usuario == null || !usuario.EsSuperUsuario)
        {
            throw new UnauthorizedAccessException("Solo un superusuario puede revisar y resolver cargas pendientes.");
        }
    }

    public async Task<CargaReferenciaAdministracionInfo?>ObtenerReferenciaAsync(int idUsuario, string codigoReferencia)
    {
        await ValidarSuperUsuarioAsync(idUsuario);
        return await _administracionRepository.ObtenerReferenciaAsync(codigoReferencia);
    }

    public async Task<InformeArchivoZipResponse> GenerarZipArchivosPendientesAsync(int idUsuario, string codigoReferencia)
    {
        await ValidarSuperUsuarioAsync(idUsuario);

        var referencia = await _administracionRepository.ObtenerReferenciaAsync(codigoReferencia);

        if (referencia == null)
        {
            throw new KeyNotFoundException("No se encontro una carga con ese codigo de referencia.");
        }

        if (!string.Equals(referencia.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"La carga ya no se encuentra pendiente de aprobacion. Estado actual: {referencia.Estado}.");
        }

        var detalle = await _administracionRepository.ObtenerDetalleAsync(codigoReferencia);

        if (detalle == null)
        {
            throw new InvalidOperationException("No fue posible obtener el detalle de la carga pendiente.");
        }

        var carpetas = await _administracionRepository.ObtenerCarpetasPendientesAsync(detalle.IdCarga);
        var delitos = await _administracionRepository.ObtenerDelitosPendientesAsync(detalle.IdCarga);
        var victimas = await _administracionRepository.ObtenerVictimasPendientesAsync(detalle.IdCarga);

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AgregarExcelAlZip(archive, "carpetas.xlsx", "carpetas", carpetas);
            AgregarExcelAlZip(archive, "delitos.xlsx", "delitos", delitos);
            AgregarExcelAlZip(archive, "victimas.xlsx", "victimas", victimas);
        }

        var entidad = NormalizarNombreArchivo(detalle.EntidadFederativa);
        var mes = NormalizarNombreArchivo(ObtenerNombreMes(detalle.MesCorte));

        return new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = $"ARCHIVOS_REVISION_{entidad}_{mes}_{detalle.AnioCorte}.zip"
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

    private static string ObtenerNombreMes(int mes)
    {
        return mes switch
        {
            1 => "ENERO",
            2 => "FEBRERO",
            3 => "MARZO",
            4 => "ABRIL",
            5 => "MAYO",
            6 => "JUNIO",
            7 => "JULIO",
            8 => "AGOSTO",
            9 => "SEPTIEMBRE",
            10 => "OCTUBRE",
            11 => "NOVIEMBRE",
            12 => "DICIEMBRE",
            _ => $"MES_{mes}"
        };
    }

    private static string NormalizarNombreArchivo(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return "SIN_DATO";
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

        var caracteres = valor.Select(caracter => char.IsLetterOrDigit(caracter) ? caracter : '_').ToArray();
        valor = new string(caracteres);

        while (valor.Contains("__"))
        {
            valor = valor.Replace("__", "_");
        }

        return valor.Trim('_');
    }
}