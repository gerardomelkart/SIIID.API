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
    private readonly ISemanalAcusePdfService _acusePdfService;

    public SemanalEnviosService(ISemanalEnviosRepository semanalEnviosRepository, ISemanalCargaRepository semanalCargaRepository, ISemanalAdministracionCargasRepository administracionRepository, ISemanalAcusePdfService acusePdfService)
    {
        _semanalEnviosRepository = semanalEnviosRepository;
        _semanalCargaRepository = semanalCargaRepository;
        _administracionRepository = administracionRepository;
        _acusePdfService = acusePdfService;
    }

    public async Task<List<SemanalEnvioItem>> ObtenerEnviosAsync(int idUsuarioConsulta, int? idEntidadFederativa, int? idUsuarioCarga, int? anioCorte, int? mesCorte, string? tipoCarga, string? estado)
    {
        var usuario = await ObtenerUsuarioAutorizadoAsync(idUsuarioConsulta);
        var tipoCargaNormalizado = NormalizarFiltro(tipoCarga);
        var estadoNormalizado = NormalizarFiltro(estado);

        var registros = await _semanalEnviosRepository.ObtenerEnviosAsync(
            usuario.EsSuperUsuario,
            idUsuarioConsulta,
            idEntidadFederativa,
            idUsuarioCarga,
            anioCorte,
            mesCorte,
            tipoCargaNormalizado,
            estadoNormalizado);

        foreach (var registro in registros)
        {
            var estadoRegistro = registro.Estado.Trim().ToUpperInvariant();
            var esActualizacion = string.Equals(registro.TipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase);

            registro.EsConfirmado = estadoRegistro is "CONFIRMADO" or "CONFIRMADO_ACTUALIZACION";
            registro.EsPendiente = estadoRegistro is "VALIDADO_PENDIENTE" or "VALIDADO_PENDIENTE_ACTUALIZACION" or "PENDIENTE_APROBACION";
            registro.EsRechazadoAdministrador = estadoRegistro == "RECHAZADO_ADMIN";
            registro.EstadoTexto = ObtenerEstadoTexto(estadoRegistro, esActualizacion);
            registro.FechaEnvioTexto = registro.FechaMovimiento.ToString("dd-MM-yyyy");
            var periodos = registro.Periodos
                .Select(x => new { x.AnioCorte, x.MesCorte })
                .Distinct()
                .OrderBy(x => x.AnioCorte)
                .ThenBy(x => x.MesCorte)
                .Select(x => $"{ObtenerNombreMes(x.MesCorte)} {x.AnioCorte}")
                .ToList();

            registro.Periodo = periodos.Count switch
            {
                0 => $"{ObtenerNombreMes(registro.MesCorte)} {registro.AnioCorte}",
                1 => periodos[0],
                _ => string.Join(", ", periodos)
            };

            registro.Semana = registro.Periodo;
            registro.FechaRechazoTexto = registro.EsRechazadoAdministrador
                ? registro.FechaConfirmacion?.ToString("dd-MM-yyyy HH:mm") ?? string.Empty
                : string.Empty;

            if (registro.EsConfirmado)
            {
                registro.EndpointAcuse = $"/api/semanal/cargas/{registro.CodigoReferencia}/acuse-confirmado";
                registro.EndpointArchivos = $"/api/semanal/envios/{registro.CodigoReferencia}/archivos";
            }
            else if (registro.EsPendiente || registro.EsRechazadoAdministrador && registro.TieneStagingDisponible)
            {
                registro.EndpointAcuse = $"/api/semanal/cargas/{registro.CodigoReferencia}/acuse";
                registro.EndpointArchivos = registro.TieneStagingDisponible
                    ? $"/api/semanal/envios/{registro.CodigoReferencia}/archivos"
                    : string.Empty;
            }
            else
            {
                registro.EndpointAcuse = string.Empty;
                registro.EndpointArchivos = string.Empty;
            }

            var pendienteResoluble =
                !esActualizacion && estadoRegistro == "VALIDADO_PENDIENTE" ||
                esActualizacion && estadoRegistro == "VALIDADO_PENDIENTE_ACTUALIZACION";

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

    public async Task<List<SemanalReporteCargaItem>> ObtenerReporteCargasAsync(int idUsuarioConsulta, int? idEntidadFederativa, int? idUsuarioCarga, int? anioCorte, int? mesCorte)
    {
        var usuario = await ObtenerUsuarioAutorizadoAsync(idUsuarioConsulta);

        if (!usuario.EsSuperUsuario)
        {
            throw new UnauthorizedAccessException("Únicamente el superusuario puede consultar el reporte de cargas preliminares.");
        }

        return await _semanalEnviosRepository.ObtenerReporteCargasAsync(
            idEntidadFederativa,
            idUsuarioCarga,
            anioCorte,
            mesCorte);
    }

    public async Task<InformeArchivoZipResponse> GenerarZipArchivosAsync(int idUsuarioConsulta, string codigoReferencia)
    {
        var usuario = await ObtenerUsuarioAutorizadoAsync(idUsuarioConsulta);
        var codigoLimpio = codigoReferencia?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(codigoLimpio)) throw new InvalidOperationException("Debe indicar el código de referencia.");

        var referencia = await _semanalEnviosRepository.ObtenerReferenciaAsync(codigoLimpio);

        if (referencia == null) throw new KeyNotFoundException("No se encontró la operación preliminar solicitada.");

        if (!usuario.EsSuperUsuario && referencia.IdUsuarioCarga != idUsuarioConsulta)
        {
            throw new UnauthorizedAccessException("No tiene permiso para descargar archivos de una operación registrada por otro usuario.");
        }

        var esConfirmada = string.Equals(referencia.Estado, "CONFIRMADO", StringComparison.OrdinalIgnoreCase) 
                           || string.Equals(referencia.Estado, "CONFIRMADO_ACTUALIZACION", StringComparison.OrdinalIgnoreCase);

        List<IDictionary<string, object?>> carpetas;
        List<IDictionary<string, object?>> delitos;
        List<IDictionary<string, object?>> victimas;

        if (esConfirmada)
        {
            carpetas = await _semanalEnviosRepository.ObtenerCarpetasConfirmadasSemanaAsync(referencia);
            delitos = await _semanalEnviosRepository.ObtenerDelitosConfirmadosSemanaAsync(referencia);
            victimas = await _semanalEnviosRepository.ObtenerVictimasConfirmadasSemanaAsync(referencia);
        }
        else
        {
            carpetas = await _administracionRepository.ObtenerCarpetasPendientesAsync(referencia.IdSemanalCarga);
            delitos = await _administracionRepository.ObtenerDelitosPendientesAsync(referencia.IdSemanalCarga);
            victimas = await _administracionRepository.ObtenerVictimasPendientesAsync(referencia.IdSemanalCarga);
        }

        if (carpetas.Count == 0 && delitos.Count == 0 && victimas.Count == 0)
        {
            var mensaje = esConfirmada
                ? "La operación confirmada no contiene registros finales disponibles para descarga."
                : "La operación ya no conserva archivos temporales disponibles para descarga.";

            throw new InvalidOperationException(mensaje);
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
            NombreArchivo = $"ARCHIVOS_PRELIMINARES_{NormalizarNombreArchivo(referencia.EntidadFederativa)}_{codigoLimpio}.zip"
        };
    }

    public async Task<InformeArchivoZipResponse> GenerarZipAcusesAsync(int idUsuarioConsulta, int anioCorte, int mesCorte, int? idEntidadFederativa, int? idUsuarioCarga)
    {
        if (anioCorte < 2000 || anioCorte > 2100) throw new InvalidOperationException("El año de corte no es válido.");
        if (mesCorte < 1 || mesCorte > 12) throw new InvalidOperationException("El mes de corte no es válido.");

        var usuarioConsulta = await ObtenerUsuarioAutorizadoAsync(idUsuarioConsulta);

        var envios = await ObtenerEnviosAsync(
            idUsuarioConsulta,
            usuarioConsulta.EsSuperUsuario ? idEntidadFederativa : null,
            usuarioConsulta.EsSuperUsuario ? idUsuarioCarga : null,
            anioCorte,
            mesCorte,
            null,
            null);

        var enviosConAcuse = envios
            .Where(envio => envio.Estado is
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
                var esConfirmado =
                    string.Equals(envio.Estado, "CONFIRMADO", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(envio.Estado, "CONFIRMADO_ACTUALIZACION", StringComparison.OrdinalIgnoreCase);

                var pdf = esConfirmado
                    ? await _acusePdfService.GenerarAcuseConfirmadoAsync(envio.CodigoReferencia, idUsuarioConsulta, anioCorte, mesCorte)
                    : await _acusePdfService.GenerarAcusePrevioAsync(envio.CodigoReferencia, idUsuarioConsulta, anioCorte, mesCorte);

                var tipoDocumento = esConfirmado ? "ACUSE" : "INFORME_PREVIO";
                var entidad = NormalizarNombreArchivo(envio.EntidadFederativa);
                var usuario = NormalizarNombreArchivo(envio.UsuarioCarga);
                var nombreArchivo = $"{envio.ClaveEntidad}_{entidad}_{usuario}_{tipoDocumento}_{envio.CodigoReferencia}.pdf";
                var entry = archive.CreateEntry(nombreArchivo, CompressionLevel.Fastest);

                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(pdf);
            }
        }

        var usuarios = enviosConAcuse
            .Select(x => x.IdUsuarioCarga)
            .Distinct()
            .ToList();

        var alcanceUsuario = usuarios.Count == 1
            ? $"USUARIO_{NormalizarNombreArchivo(enviosConAcuse[0].UsuarioCarga)}"
            : "TODOS_LOS_USUARIOS";

        return new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = $"ACUSES_PRELIMINARES_{NormalizarNombreArchivo(ObtenerNombreMes(mesCorte))}_{anioCorte}_{alcanceUsuario}.zip"
        };
    }

    public async Task<InformeArchivoZipResponse> GenerarZipPlanosAsync(int idUsuarioConsulta, int anioCorte, int mesCorte, string? tipoPlano, string? modoPlano)
    {
        var usuario = await ObtenerUsuarioAutorizadoAsync(idUsuarioConsulta);

        if (anioCorte < 2000 || anioCorte > 2100) throw new InvalidOperationException("El año de corte no es válido.");
        if (mesCorte < 1 || mesCorte > 12) throw new InvalidOperationException("El mes de corte no es válido.");

        var tipo = (tipoPlano ?? "COMPLETA").Trim().ToUpperInvariant();
        var modo = NormalizarModoPlano(modoPlano);

        if (tipo is not "COMPLETA" and not "ESTATALES" and not "MUNICIPALES") throw new InvalidOperationException("El tipo de plano debe ser COMPLETA, ESTATALES o MUNICIPALES.");
        if (!usuario.EsSuperUsuario && modo != "CONFIRMADO") throw new UnauthorizedAccessException("Únicamente el superusuario puede generar planos previos o mixtos.");

        var idEntidadFederativa = usuario.EsSuperUsuario ? null : usuario.IdEntidadFederativa;

        if (!await _semanalEnviosRepository.ExisteInformacionPlanoAsync(anioCorte, mesCorte, idEntidadFederativa, modo))
        {
            throw new InvalidOperationException($"No existe información semanal disponible para {ObtenerNombreMes(mesCorte)} de {anioCorte} en modo {modo.ToLowerInvariant()}.");
        }

        var tareas = new List<(string Archivo, string Hoja, Task<List<IDictionary<string, object?>>> Consulta)>();

        if (tipo is "COMPLETA" or "ESTATALES")
        {
            tareas.Add(("estatal-delitos.xlsx", "estatal-delitos", _semanalEnviosRepository.ObtenerPlanoEstatalDelitosAsync(anioCorte, mesCorte, idEntidadFederativa, modo)));
            tareas.Add(("estatal-victimas.xlsx", "estatal-victimas", _semanalEnviosRepository.ObtenerPlanoEstatalVictimasAsync(anioCorte, mesCorte, idEntidadFederativa, modo)));
        }

        if (tipo is "COMPLETA" or "MUNICIPALES")
        {
            tareas.Add(("municipal-delitos.xlsx", "municipal-delitos", _semanalEnviosRepository.ObtenerPlanoMunicipalDelitosAsync(anioCorte, mesCorte, idEntidadFederativa, modo)));
            tareas.Add(("municipal-victimas.xlsx", "municipal-victimas", _semanalEnviosRepository.ObtenerPlanoMunicipalVictimasAsync(anioCorte, mesCorte, idEntidadFederativa, modo)));
        }

        await Task.WhenAll(tareas.Select(tarea => tarea.Consulta));

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var tarea in tareas) AgregarExcelAlZip(archive, tarea.Archivo, tarea.Hoja, tarea.Consulta.Result);
        }

        return new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = $"PLANOS_SEMANALES_{modo}_{NormalizarNombreArchivo(ObtenerNombreMes(mesCorte))}_{anioCorte}.zip"
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

    private static string NormalizarModoPlano(string? modoPlano)
    {
        var modo = (modoPlano ?? "CONFIRMADO").Trim().ToUpperInvariant();

        return modo is "CONFIRMADO" or "PREVIO" or "MIXTO"
            ? modo
            : throw new InvalidOperationException("El modo del plano debe ser CONFIRMADO, PREVIO o MIXTO.");
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