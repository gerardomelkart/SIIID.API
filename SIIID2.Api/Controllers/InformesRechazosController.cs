using ClosedXML.Excel;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using System.IO.Compression;
using System.Security.Claims;

namespace SIIID2.Api.Controllers;

[ApiController]
[Route("api/informes/rechazos")]
public class InformesRechazosController : ControllerBase
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IInformeRepository _informeRepository;
    private readonly IUsuarioRepository _usuarioRepository;

    public InformesRechazosController(IDbConnectionFactory dbConnectionFactory, IInformeRepository informeRepository, IUsuarioRepository usuarioRepository)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _informeRepository = informeRepository;
        _usuarioRepository = usuarioRepository;
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> ObtenerRechazos([FromQuery] int? idEntidadFederativa = null, [FromQuery] int? mesCorte = null, [FromQuery] int? anioCorte = null)
    {
        var idUsuarioConsulta = ObtenerIdUsuarioAutenticado();

        if (!idUsuarioConsulta.HasValue)
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta.Value);

        if (usuarioConsulta == null)
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_USUARIO_NO_DISPONIBLE",
                mensaje = "El usuario autenticado no existe o no está activo."
            });
        }

        if (!usuarioConsulta.EsSuperUsuario && !usuarioConsulta.IdEntidadFederativa.HasValue)
        {
            return Ok(Array.Empty<InformeEnvioItem>());
        }

        const string sql = @"
            SELECT
                c.id_carga AS IdCarga,
                c.codigo_referencia AS CodigoReferencia,
                c.tipo_carga AS TipoCarga,
                c.estado AS Estado,
                c.id_entidad_federativa AS IdEntidadFederativa,
                ef.nombre AS EntidadFederativa,
                ef.clave AS ClaveEntidad,
                c.fecha_validacion AS FechaEnvio,
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte,
                ucarga.usuario AS UsuarioEnvio,
                c.mensaje_error AS MotivoRechazo,
                c.fecha_confirmacion AS FechaRechazo,
                urechazo.usuario AS UsuarioRechazo,
                CASE
                    WHEN (
                        EXISTS (SELECT 1 FROM dbo.carga_tmp_carpeta tc WHERE tc.id_carga = c.id_carga AND tc.activo = 1)
                        OR EXISTS (SELECT 1 FROM dbo.carga_tmp_delito td WHERE td.id_carga = c.id_carga AND td.activo = 1)
                        OR EXISTS (SELECT 1 FROM dbo.carga_tmp_victima tv WHERE tv.id_carga = c.id_carga AND tv.activo = 1)
                    )
                    AND NOT EXISTS (
                        SELECT 1
                        FROM dbo.carga c2
                        WHERE ISNULL(c2.id_entidad_federativa, 0) = ISNULL(c.id_entidad_federativa, 0)
                          AND c2.mes_corte = c.mes_corte
                          AND c2.anio_corte = c.anio_corte
                          AND ISNULL(c2.tipo_carga, N'') = ISNULL(c.tipo_carga, N'')
                          AND c2.activo = 1
                          AND c2.id_carga > c.id_carga
                          AND c2.estado IN (
                              N'VALIDADO_PENDIENTE',
                              N'VALIDADO_PENDIENTE_ACTUALIZACION',
                              N'PENDIENTE_APROBACION',
                              N'RECHAZADO_ADMIN',
                              N'CONFIRMADO',
                              N'CONFIRMADO_ACTUALIZACION'
                          )
                    )
                    THEN CAST(1 AS bit)
                    ELSE CAST(0 AS bit)
                END AS TieneStagingDisponible
            FROM dbo.carga c
            INNER JOIN dbo.usuario ucarga
                ON ucarga.id_usuario = c.id_usuario_carga
            INNER JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = c.id_entidad_federativa
            LEFT JOIN dbo.usuario urechazo
                ON urechazo.id_usuario = c.id_usuario_confirmacion
            WHERE c.activo = 1
              AND c.estado = N'RECHAZADO_ADMIN'
              AND (@EsSuperUsuario = 1 OR c.id_entidad_federativa = @IdEntidadFederativaUsuario)
              AND (@IdEntidadFederativa IS NULL OR c.id_entidad_federativa = @IdEntidadFederativa)
              AND (@MesCorte IS NULL OR c.mes_corte = @MesCorte)
              AND (@AnioCorte IS NULL OR c.anio_corte = @AnioCorte)
            ORDER BY
                ef.nombre,
                c.anio_corte DESC,
                c.mes_corte DESC,
                c.fecha_confirmacion DESC,
                c.id_carga DESC;";

        using var connection = _dbConnectionFactory.CrearConexion();

        var rechazados = (await connection.QueryAsync<InformeEnvioItem>(sql, new
        {
            EsSuperUsuario = usuarioConsulta.EsSuperUsuario,
            IdEntidadFederativaUsuario = usuarioConsulta.IdEntidadFederativa,
            IdEntidadFederativa = usuarioConsulta.EsSuperUsuario ? idEntidadFederativa : null,
            MesCorte = mesCorte,
            AnioCorte = anioCorte
        })).ToList();

        foreach (var envio in rechazados)
        {
            envio.FechaEnvioTexto = envio.FechaEnvio.ToString("dd-MM-yyyy");
            envio.Corte = $"{ObtenerNombreMes(envio.MesCorte)} {envio.AnioCorte}";
            envio.EstadoTexto = envio.TieneStagingDisponible ? "Rechazado por administrador" : "Rechazado por administrador (histórico)";
            envio.EsConfirmado = false;
            envio.EsRechazadoAdministrador = true;
            envio.FechaRechazoTexto = envio.FechaRechazo?.ToString("dd-MM-yyyy HH:mm") ?? string.Empty;
            envio.EndpointAcuse = envio.TieneStagingDisponible ? ObtenerEndpointAcusePrevio(envio.TipoCarga, envio.CodigoReferencia) : string.Empty;
            envio.EndpointExcel = envio.TieneStagingDisponible ? $"/api/informes/rechazos/{envio.CodigoReferencia}/archivos" : string.Empty;
        }

        return Ok(rechazados);
    }

    [Authorize]
    [HttpGet("{codigoReferencia}/archivos")]
    public async Task<IActionResult> DescargarArchivosRechazados(string codigoReferencia)
    {
        var idUsuarioConsulta = ObtenerIdUsuarioAutenticado();

        if (!idUsuarioConsulta.HasValue)
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido.",
                traceId = HttpContext.TraceIdentifier
            });
        }

        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta.Value);

        if (usuarioConsulta == null)
        {
            return Unauthorized(new
            {
                esValido = false,
                codigo = "GENERAL_USUARIO_NO_DISPONIBLE",
                mensaje = "El usuario autenticado no existe o no está activo."
            });
        }

        const string sql = @"
            SELECT
                c.id_carga AS IdCarga,
                c.codigo_referencia AS CodigoReferencia,
                c.tipo_carga AS TipoCarga,
                c.estado AS Estado,
                c.id_entidad_federativa AS IdEntidadFederativa,
                ef.nombre AS EntidadFederativa,
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte,
                CASE
                    WHEN (
                        EXISTS (SELECT 1 FROM dbo.carga_tmp_carpeta tc WHERE tc.id_carga = c.id_carga AND tc.activo = 1)
                        OR EXISTS (SELECT 1 FROM dbo.carga_tmp_delito td WHERE td.id_carga = c.id_carga AND td.activo = 1)
                        OR EXISTS (SELECT 1 FROM dbo.carga_tmp_victima tv WHERE tv.id_carga = c.id_carga AND tv.activo = 1)
                    )
                    AND NOT EXISTS (
                        SELECT 1
                        FROM dbo.carga c2
                        WHERE ISNULL(c2.id_entidad_federativa, 0) = ISNULL(c.id_entidad_federativa, 0)
                          AND c2.mes_corte = c.mes_corte
                          AND c2.anio_corte = c.anio_corte
                          AND ISNULL(c2.tipo_carga, N'') = ISNULL(c.tipo_carga, N'')
                          AND c2.activo = 1
                          AND c2.id_carga > c.id_carga
                          AND c2.estado IN (
                              N'VALIDADO_PENDIENTE',
                              N'VALIDADO_PENDIENTE_ACTUALIZACION',
                              N'PENDIENTE_APROBACION',
                              N'RECHAZADO_ADMIN',
                              N'CONFIRMADO',
                              N'CONFIRMADO_ACTUALIZACION'
                          )
                    )
                    THEN CAST(1 AS bit)
                    ELSE CAST(0 AS bit)
                END AS TieneStagingDisponible
            FROM dbo.carga c
            INNER JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = c.id_entidad_federativa
            WHERE c.codigo_referencia = @CodigoReferencia
              AND c.activo = 1
              AND c.estado = N'RECHAZADO_ADMIN';";

        using var connection = _dbConnectionFactory.CrearConexion();
        var carga = await connection.QueryFirstOrDefaultAsync<InformeArchivoCargaInfo>(sql, new { CodigoReferencia = codigoReferencia });

        if (carga == null)
        {
            return NotFound(new
            {
                esValido = false,
                codigo = "INFORMES_RECHAZO_NO_ENCONTRADO",
                mensaje = "No se encontró una carga rechazada por el administrador para el código de referencia indicado."
            });
        }

        if (!usuarioConsulta.EsSuperUsuario && (!usuarioConsulta.IdEntidadFederativa.HasValue || usuarioConsulta.IdEntidadFederativa.Value != carga.IdEntidadFederativa))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                esValido = false,
                codigo = "INFORMES_SIN_PERMISO",
                mensaje = "No tiene permiso para descargar archivos de otra entidad federativa."
            });
        }

        if (!carga.TieneStagingDisponible)
        {
            return Conflict(new
            {
                esValido = false,
                codigo = "INFORMES_RECHAZO_STAGING_NO_DISPONIBLE",
                mensaje = "Los archivos reconstruidos ya no están disponibles porque este rechazo ya es histórico o su staging fue depurado por mantenimiento."
            });
        }

        var carpetas = await _informeRepository.ObtenerCarpetasPendientesAsync(carga.IdCarga);
        var delitos = await _informeRepository.ObtenerDelitosPendientesAsync(carga.IdCarga);
        var victimas = await _informeRepository.ObtenerVictimasPendientesAsync(carga.IdCarga);

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AgregarExcelAlZip(archive, "carpetas.xlsx", "carpetas", carpetas);
            AgregarExcelAlZip(archive, "delitos.xlsx", "delitos", delitos);
            AgregarExcelAlZip(archive, "victimas.xlsx", "victimas", victimas);
        }

        var nombreArchivo = $"ARCHIVOS_RECHAZADOS_{NormalizarNombreArchivo(carga.EntidadFederativa)}_{NormalizarNombreArchivo(ObtenerNombreMes(carga.MesCorte))}_{carga.AnioCorte}.zip";

        return File(zipStream.ToArray(), "application/zip", nombreArchivo);
    }

    private int? ObtenerIdUsuarioAutenticado()
    {
        var idUsuarioClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idUsuarioClaim, out var idUsuarioConsulta) ? idUsuarioConsulta : null;
    }

    private static string ObtenerEndpointAcusePrevio(string tipoCarga, string codigoReferencia)
    {
        return string.Equals(tipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase)
            ? $"/api/actualizaciones/{codigoReferencia}/acuse"
            : $"/api/cargas/{codigoReferencia}/acuse";
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
                var valor = filas[fila].TryGetValue(nombreColumna, out var dato) ? dato : null;
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

        for (var i = 0; i < columnas.Count; i++)
        {
            worksheet.Column(i + 1).Width = 14;
        }

        worksheet.SheetView.FreezeRows(1);
        workbook.SaveAs(entryStream);
    }

    private static XLCellValue ConvertirValorExcel(object? valor)
    {
        if (valor == null || valor == DBNull.Value)
        {
            return string.Empty;
        }

        return valor switch
        {
            DateTime fecha => fecha,
            int entero => entero,
            long enteroLargo => enteroLargo,
            decimal numeroDecimal => numeroDecimal,
            double numeroDouble => numeroDouble,
            float numeroFloat => numeroFloat,
            bool booleano => booleano,
            _ => valor.ToString() ?? string.Empty
        };
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

        var caracteres = valor.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
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
            _ => mes.ToString("00")
        };
    }
}
