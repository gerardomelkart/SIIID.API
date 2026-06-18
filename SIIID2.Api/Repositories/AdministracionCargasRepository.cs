using Dapper;
using DocumentFormat.OpenXml.Drawing.Charts;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;
using ClosedXML.Excel;
using System.IO.Compression;

namespace SIIID2.Api.Repositories;

public class AdministracionCargasRepository : IAdministracionCargasRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public AdministracionCargasRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<List<CargaPendienteAdministracionItem>> ObtenerPendientesAsync()
    {
        const string sql = @"
            SELECT
                c.id_carga AS IdCarga,
                c.codigo_referencia AS CodigoReferencia,
                c.tipo_carga AS TipoCarga,
                c.id_entidad_federativa AS IdEntidadFederativa,
                ISNULL(ef.nombre, '') AS EntidadFederativa,
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte,
                c.fecha_validacion AS FechaValidacion,
                c.id_usuario_carga AS IdUsuarioCarga,
                u.usuario AS UsuarioCarga,
                LTRIM(RTRIM(
                    CONCAT(
                        u.nombre,
                        ' ',
                        u.primer_apellido,
                        CASE
                            WHEN NULLIF(u.segundo_apellido, '') IS NULL
                                THEN ''
                            ELSE CONCAT(' ', u.segundo_apellido)
                        END
                    )
                )) AS NombreUsuarioCarga,
                c.total_carpetas_investigacion AS TotalCarpetas,
                c.total_delitos AS TotalDelitos,
                c.total_victimas AS TotalVictimas,
                (
                    SELECT COUNT(1)
                    FROM dbo.carga_advertencia ca
                    WHERE ca.id_carga = c.id_carga
                      AND ca.activo = 1
                ) AS TotalAdvertencias
            FROM dbo.carga c
            INNER JOIN dbo.usuario u
                ON u.id_usuario = c.id_usuario_carga
            LEFT JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa =
                   c.id_entidad_federativa
            WHERE c.estado = 'PENDIENTE_APROBACION'
              AND c.activo = 1
            ORDER BY
                c.fecha_validacion ASC,
                c.id_carga ASC;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        var registros = await connection.QueryAsync<CargaPendienteAdministracionItem>(sql);
        return registros.ToList();
    }

    public async Task<CargaPendienteAdministracionDetalle?> ObtenerDetalleAsync(string codigoReferencia)
    {
        const string sqlCarga = @"
            SELECT TOP 1
                c.id_carga AS IdCarga,
                c.codigo_referencia AS CodigoReferencia,
                c.tipo_carga AS TipoCarga,
                c.id_entidad_federativa AS IdEntidadFederativa,
                ISNULL(ef.nombre, '') AS EntidadFederativa,
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte,
                c.fecha_validacion AS FechaValidacion,
                c.id_usuario_carga AS IdUsuarioCarga,
                u.usuario AS UsuarioCarga,
                LTRIM(RTRIM(
                    CONCAT(
                        u.nombre,
                        ' ',
                        u.primer_apellido,
                        CASE
                            WHEN NULLIF(u.segundo_apellido, '') IS NULL
                                THEN ''
                            ELSE CONCAT(' ', u.segundo_apellido)
                        END
                    )
                )) AS NombreUsuarioCarga,
                c.total_carpetas_investigacion AS TotalCarpetas,
                c.total_delitos AS TotalDelitos,
                c.total_victimas AS TotalVictimas,
                (
                    SELECT COUNT(1)
                    FROM dbo.carga_advertencia ca
                    WHERE ca.id_carga = c.id_carga
                      AND ca.activo = 1
                ) AS TotalAdvertencias
            FROM dbo.carga c
            INNER JOIN dbo.usuario u
                ON u.id_usuario = c.id_usuario_carga
            LEFT JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa =
                   c.id_entidad_federativa
            WHERE c.codigo_referencia = @CodigoReferencia
              AND c.estado = 'PENDIENTE_APROBACION'
              AND c.activo = 1;
        ";

        const string sqlAdvertencias = @"
            SELECT
                ca.id_carga_advertencia AS IdCargaAdvertencia,
                ca.codigo AS Codigo,
                ca.archivo AS Archivo,
                ca.numero_fila AS NumeroFila,
                ca.columna AS Columna,
                ca.campo AS Campo,
                ca.valor AS Valor,
                ca.descripcion_resumen AS DescripcionResumen,
                ca.mensaje AS Mensaje,
                ca.aceptada_usuario AS AceptadaUsuario,
                ca.fecha_aceptacion AS FechaAceptacion
            FROM dbo.carga_advertencia ca
            WHERE ca.id_carga = @IdCarga
              AND ca.activo = 1
            ORDER BY
                ca.archivo,
                ca.numero_fila,
                ca.id_carga_advertencia;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        var carga = await connection.QueryFirstOrDefaultAsync<CargaPendienteAdministracionDetalle>(sqlCarga, new{CodigoReferencia = codigoReferencia});
        if (carga == null)
        {
            return null;
        }
        var advertencias = await connection.QueryAsync<CargaAdvertenciaAdministracionItem>(sqlAdvertencias, new {carga.IdCarga});
        carga.Advertencias = advertencias.ToList();
        return carga;
    }

    public async Task<CargaReferenciaAdministracionInfo?> ObtenerReferenciaAsync(string codigoReferencia)
    {
        const string sql = @"
        SELECT TOP 1
            c.codigo_referencia AS CodigoReferencia,
            c.tipo_carga AS TipoCarga,
            c.estado AS Estado
        FROM dbo.carga c
        WHERE c.codigo_referencia = @CodigoReferencia
          AND c.activo = 1;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<CargaReferenciaAdministracionInfo>(sql, new {CodigoReferencia = codigoReferencia});
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerCarpetasPendientesAsync(long idCarga)
    {
        const string sql = @"
        SELECT
            c.id_ci,
            c.ntra_ci,
            c.fha_de_ini,
            c.hra_de_ini,
            c.rmen_de_hchos
        FROM dbo.carga_tmp_carpeta c
        WHERE c.id_carga = @IdCarga
          AND c.activo = 1
        ORDER BY c.numero_fila;
        ";

        return await QueryDictionaryAsync(sql, idCarga);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerDelitosPendientesAsync(long idCarga)
    {
        const string sql = @"
        SELECT
            d.id_ci,
            d.id_delito,
            d.dto,
            d.moda_dto,
            d.forma_acc,
            d.fha_de_hchos,
            d.hra_de_hchos,
            d.emto_com_dto,
            d.grdo_cons,
            d.clasf_de_dto,
            ISNULL(ef.nombre, '') AS nom_ent_hchos,
            d.id_ent_hchos,
            ISNULL(mun.nombre, '') AS nom_mun_hchos,
            d.id_mun_hchos,
            d.nom_loc_hchos,
            d.id_loc_hchos,
            d.nom_col_hchos,
            d.id_col_hchos,
            d.cp,
            d.coord_x,
            d.coord_y,
            d.dom_hchos
        FROM dbo.carga_tmp_delito d
        LEFT JOIN dbo.catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos)
        LEFT JOIN dbo.catalogo_municipio mun
            ON mun.id_entidad_federativa = ef.id_entidad_federativa
           AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos)
        WHERE d.id_carga = @IdCarga
          AND d.activo = 1
        ORDER BY d.numero_fila;
        ";

        return await QueryDictionaryAsync(sql, idCarga);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerVictimasPendientesAsync(long idCarga)
    {
        const string sql = @"
        SELECT
            v.id_ci,
            v.id_delito,
            v.id_vicf,
            v.id_tv,
            v.id_tpm,
            v.sexo,
            v.genero,
            v.pob,
            v.disc,
            v.fha_nac,
            v.edad,
            v.nacional
        FROM dbo.carga_tmp_victima v
        WHERE v.id_carga = @IdCarga
          AND v.activo = 1
        ORDER BY v.numero_fila;
    ";

        return await QueryDictionaryAsync(sql, idCarga);
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryAsync(string sql, long idCarga)
    {
        using var connection = _dbConnectionFactory.CrearConexion();

        var filas = await connection.QueryAsync(sql, new { IdCarga = idCarga });

        return filas
            .Select(fila => ((IDictionary<string, object?>)fila).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase))
            .Cast<IDictionary<string, object?>>()
            .ToList();
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

        return new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = $"ARCHIVOS_REVISION_{codigoReferencia}.zip"
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
}