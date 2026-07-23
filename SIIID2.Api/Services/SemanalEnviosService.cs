using System.IO.Compression;
using ClosedXML.Excel;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class SemanalEnviosService : ISemanalEnviosService
{
    private readonly ISemanalEnviosRepository _semanalEnviosRepository;
    private readonly ISemanalCargaRepository _semanalCargaRepository;
    private readonly ISemanalAdministracionCargasRepository _administracionRepository;

    public SemanalEnviosService(ISemanalEnviosRepository semanalEnviosRepository, ISemanalCargaRepository semanalCargaRepository, ISemanalAdministracionCargasRepository administracionRepository)
    {
        _semanalEnviosRepository = semanalEnviosRepository;
        _semanalCargaRepository = semanalCargaRepository;
        _administracionRepository = administracionRepository;
    }

    public async Task<List<SemanalEnvioItem>> ObtenerEnviosAsync(int idUsuarioConsulta, int? idEntidadFederativa, int? anioSemana, int? numeroSemana, string? tipoCarga, string? estado)
    {
        var usuario = await ObtenerUsuarioAutorizadoAsync(idUsuarioConsulta);
        var tipoCargaNormalizado = NormalizarFiltro(tipoCarga);
        var estadoNormalizado = NormalizarFiltro(estado);

        var registros = await _semanalEnviosRepository.ObtenerEnviosAsync(
            usuario.EsSuperUsuario,
            usuario.IdEntidadFederativa,
            idEntidadFederativa,
            anioSemana,
            numeroSemana,
            tipoCargaNormalizado,
            estadoNormalizado);

        foreach (var registro in registros)
        {
            var estadoRegistro = registro.Estado.Trim().ToUpperInvariant();
            var esActualizacion = string.Equals(registro.TipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase);

            registro.EsConfirmado = estadoRegistro is "CONFIRMADO" or "CONFIRMADO_ACTUALIZACION";
            registro.EsPendiente = estadoRegistro is "VALIDADO_PENDIENTE" or "VALIDADO_PENDIENTE_ACTUALIZACION" or "PENDIENTE_APROBACION";
            registro.EstadoTexto = ObtenerEstadoTexto(estadoRegistro, esActualizacion);
            registro.EndpointAcuse = registro.EsConfirmado
                ? $"/api/semanal/cargas/{registro.CodigoReferencia}/acuse-confirmado"
                : $"/api/semanal/cargas/{registro.CodigoReferencia}/acuse";
            registro.EndpointArchivos = $"/api/semanal/envios/{registro.CodigoReferencia}/archivos";

            var pendienteResoluble =
                (!esActualizacion && estadoRegistro == "VALIDADO_PENDIENTE") ||
                (esActualizacion && estadoRegistro == "VALIDADO_PENDIENTE_ACTUALIZACION");

            var permisoOperacion = esActualizacion
                ? usuario.HabilitaModificacion
                : usuario.HabilitaCarga;

            registro.PuedeResolverPendiente =
                pendienteResoluble &&
                permisoOperacion &&
                (usuario.EsSuperUsuario || registro.IdUsuarioCarga == idUsuarioConsulta);
        }

        return registros;
    }

    public async Task<InformeArchivoZipResponse> GenerarZipArchivosAsync(int idUsuarioConsulta, string codigoReferencia)
    {
        var usuario = await ObtenerUsuarioAutorizadoAsync(idUsuarioConsulta);
        var codigoLimpio = codigoReferencia?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(codigoLimpio)) throw new InvalidOperationException("Debe indicar el código de referencia.");

        var referencia = await _semanalEnviosRepository.ObtenerReferenciaAsync(codigoLimpio);

        if (referencia == null) throw new KeyNotFoundException("No se encontró la operación semanal solicitada.");

        if (!usuario.EsSuperUsuario &&
            (!usuario.IdEntidadFederativa.HasValue ||
             usuario.IdEntidadFederativa.Value != referencia.IdEntidadFederativa))
        {
            throw new UnauthorizedAccessException("No tiene permiso para descargar archivos de otra entidad federativa.");
        }

        var carpetas = await _administracionRepository.ObtenerCarpetasPendientesAsync(referencia.IdSemanalCarga);
        var delitos = await _administracionRepository.ObtenerDelitosPendientesAsync(referencia.IdSemanalCarga);
        var victimas = await _administracionRepository.ObtenerVictimasPendientesAsync(referencia.IdSemanalCarga);

        if (carpetas.Count == 0 && delitos.Count == 0 && victimas.Count == 0)
        {
            throw new InvalidOperationException("La operación ya no conserva archivos temporales disponibles para descarga.");
        }

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
            NombreArchivo = $"ARCHIVOS_SEMANA_{referencia.NumeroSemana}_{referencia.AnioSemana}_{NormalizarNombreArchivo(referencia.EntidadFederativa)}_{codigoLimpio}.zip"
        };
    }

    private async Task<UsuarioCargaInfo> ObtenerUsuarioAutorizadoAsync(int idUsuario)
    {
        var usuario = await _semanalCargaRepository.ObtenerUsuarioCargaAsync(idUsuario);

        if (usuario == null) throw new UnauthorizedAccessException("El usuario no tiene acceso activo al módulo semanal.");

        var rol = (usuario.Rol ?? string.Empty).Trim().ToUpperInvariant();

        if (!usuario.EsSuperUsuario && rol != "ENLACE_ESTATAL" && rol != "CONSULTA")
        {
            throw new UnauthorizedAccessException("El usuario no tiene permiso para consultar envíos semanales.");
        }

        if (!usuario.EsSuperUsuario && !usuario.IdEntidadFederativa.HasValue)
        {
            throw new UnauthorizedAccessException("El usuario no tiene una entidad federativa asignada.");
        }

        return usuario;
    }

    private static string? NormalizarFiltro(string? valor)
    {
        var resultado = valor?.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(resultado) ? null : resultado;
    }

    private static string ObtenerEstadoTexto(string estado, bool esActualizacion)
    {
        var operacion = esActualizacion ? "actualización" : "carga";

        return estado switch
        {
            "VALIDADO_PENDIENTE" => $"Pendiente de confirmar {operacion}",
            "VALIDADO_PENDIENTE_ACTUALIZACION" => "Actualización pendiente de confirmar",
            "PENDIENTE_APROBACION" => "Pendiente de aprobación administrativa",
            "CONFIRMADO" => "Carga confirmada",
            "CONFIRMADO_ACTUALIZACION" => "Actualización confirmada",
            "RECHAZADO" => $"{char.ToUpper(operacion[0])}{operacion[1..]} rechazada por el usuario",
            "RECHAZADO_ADMIN" => $"{char.ToUpper(operacion[0])}{operacion[1..]} rechazada administrativamente",
            "EXPIRADO" => $"{char.ToUpper(operacion[0])}{operacion[1..]} expirada",
            _ => estado.Replace("_", " ")
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
}