using System.IO.Compression;
using Dapper;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public sealed partial class BanciMensualService
{
    public async Task<IEnumerable<BanciPeriodo>> PeriodosCargasAsync(int usuario)
    {
        await AccesoAsync(usuario, null, "ADMIN");
        using var db = factory.CrearConexion();
        return await db.QueryAsync<BanciPeriodo>("SELECT DISTINCT anio_corte AS Anio,mes_corte AS Mes FROM dbo.banci_carga ORDER BY Anio DESC,Mes DESC;");
    }

    public async Task<IEnumerable<BanciReporteCarga>> ReporteCargasAsync(int usuario, int anio, int mes)
    {
        await AccesoAsync(usuario, null, "ADMIN");
        if(mes is < 1 or > 12 || anio < 1900 || anio > DateTime.UtcNow.Year) throw new ArgumentException("Corte no válido.");
        using var db = factory.CrearConexion();
        var filas = (await db.QueryAsync<BanciReporteCarga>("""
            SELECT ef.id_entidad_federativa AS Entidad,ef.nombre AS EntidadNombre,
                COALESCE(intentos.Total,0) AS Intentos,COALESCE(ultimo.estado,'SIN_CARGA') AS Estado,
                ultimo.fecha_validacion AS FechaCarga,CASE WHEN ultimo.estado IN('CONFIRMADO','CONFIRMADO_ACTUALIZACION') THEN ultimo.fecha_aprobacion END AS FechaAprobacion,
                exitosa.FechaCargaExitosa
            FROM dbo.catalogo_entidad_federativa ef
            OUTER APPLY(SELECT COUNT(*) AS Total FROM dbo.banci_carga c
                WHERE c.id_entidad_federativa=ef.id_entidad_federativa AND c.anio_corte=@anio AND c.mes_corte=@mes) intentos
            OUTER APPLY(SELECT TOP(1) c.estado,c.fecha_validacion,c.fecha_aprobacion FROM dbo.banci_carga c
                WHERE c.id_entidad_federativa=ef.id_entidad_federativa AND c.anio_corte=@anio AND c.mes_corte=@mes
                ORDER BY c.fecha_validacion DESC,c.id_banci_carga DESC) ultimo
            OUTER APPLY(SELECT MIN(c.fecha_validacion) AS FechaCargaExitosa FROM dbo.banci_carga c
                WHERE c.id_entidad_federativa=ef.id_entidad_federativa AND c.anio_corte=@anio AND c.mes_corte=@mes
                  AND c.estado IN('PENDIENTE_APROBACION','CONFIRMADO','CONFIRMADO_ACTUALIZACION')) exitosa
            WHERE ef.activo=1 AND TRY_CONVERT(int,ef.clave) BETWEEN 1 AND 32;
            """, new { anio, mes })).ToList();
        return PrepararReporteCargas(filas);
    }

    public static IEnumerable<BanciReporteCarga> PrepararReporteCargas(IEnumerable<BanciReporteCarga> datos)
    {
        var filas = datos.ToList();
        var ranking = 0;
        foreach(var fila in filas.OrderBy(f => f.FechaCargaExitosa).ThenBy(f => f.Entidad))
        {
            fila.Ranking = fila.FechaCargaExitosa.HasValue ? ++ranking : null;
            fila.FechaCarga = UtcReporte(fila.FechaCarga);
            fila.FechaAprobacion = UtcReporte(fila.FechaAprobacion);
        }
        return filas.OrderBy(f => f.Ranking ?? int.MaxValue).ThenBy(f => f.Entidad);
    }

    public async Task<IEnumerable<BanciArchivoOriginal>> UltimosOriginalesAsync(int usuario)
    {
        await AccesoAsync(usuario, null, "ADMIN");
        using var db = factory.CrearConexion();
        var filas = (await db.QueryAsync<BanciArchivoOriginal>("""
            SELECT Referencia,Entidad,EntidadNombre,Mes,Anio,Tipo,Fecha,NombreOriginal,TamanioBytes,Sha256 FROM (
                SELECT b.Referencia,b.Entidad,b.EntidadNombre,b.Mes,b.Anio,b.Tipo,b.Fecha,
                    a.nombre_original AS NombreOriginal,a.longitud_bytes AS TamanioBytes,a.huella_sha256 AS Sha256,
                    ROW_NUMBER() OVER(PARTITION BY b.Entidad ORDER BY b.Fecha DESC,b.Id DESC) AS rn
                FROM (
            """ + BanciMensualSql.Operaciones + """
                ) b JOIN dbo.banci_carga_archivo a ON a.id_banci_carga=b.Id
                WHERE b.Manual=0 AND b.TieneOriginal=1
            ) x WHERE rn=1 ORDER BY EntidadNombre;
            """)).ToList();
        foreach(var fila in filas) fila.Fecha = DateTime.SpecifyKind(fila.Fecha, DateTimeKind.Utc);
        return filas;
    }

    public async Task<byte[]> OriginalZipAsync(int usuario, Guid referencia)
    {
        await AccesoAsync(usuario, null, "ADMIN");
        var original = await OriginalAsync(usuario, referencia);
        using var salida = new MemoryStream();
        using(var zip = new ZipArchive(salida, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entrada = zip.CreateEntry(Path.GetFileName(original.Nombre));
            await using var archivo = entrada.Open();
            await archivo.WriteAsync(original.Bytes);
        }
        return salida.ToArray();
    }

    private static DateTime? UtcReporte(DateTime? fecha) => fecha.HasValue ? DateTime.SpecifyKind(fecha.Value, DateTimeKind.Utc) : null;
}
