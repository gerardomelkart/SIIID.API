using Dapper;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public sealed partial class BanciMensualService
{
    public async Task<IEnumerable<BanciOperacion>> PendientesAdministracionAsync(int usuario)
    {
        await AccesoAsync(usuario, null, "ADMIN");
        using var db = factory.CrearConexion();
        var filas = (await db.QueryAsync<BanciOperacion>("""
            SELECT b.Id,b.Referencia,b.Entidad,b.EntidadNombre,b.Mes,b.Anio,b.Tipo,b.Estado,b.Usuario,
                b.NombreUsuario,COALESCE(b.FechaEnvio,b.Fecha) AS Fecha,b.Motivo,b.Manual,b.TieneOriginal,
                a.nombre_original AS NombreOriginal,
                (SELECT COUNT(*) FROM dbo.banci_carga_tmp_carpeta t WHERE t.id_banci_carga=b.Id) AS TotalCarpetas,
                (SELECT COUNT(*) FROM dbo.banci_carga_tmp_delito t WHERE t.id_banci_carga=b.Id) AS TotalDelitos,
                (SELECT COUNT(*) FROM dbo.banci_carga_tmp_victima t WHERE t.id_banci_carga=b.Id) AS TotalVictimas
            FROM (
            """ + BanciMensualSql.Operaciones + """
            ) b LEFT JOIN dbo.banci_carga_archivo a ON a.id_banci_carga=b.Id
            WHERE b.Estado='PENDIENTE_APROBACION' AND NOT EXISTS(SELECT 1 FROM dbo.banci_operacion_vinculada ov WHERE ov.id_banci_carga=b.Id) ORDER BY COALESCE(b.FechaEnvio,b.Fecha),b.Id;
            """)).ToList();
        foreach(var fila in filas)
            fila.Fecha = DateTime.SpecifyKind(fila.Fecha, DateTimeKind.Utc);
        return filas;
    }

    public async Task<BanciRevisionAdministracion> RevisionAdministracionAsync(int usuario, Guid referencia)
    {
        await AccesoAsync(usuario, null, "ADMIN");
        return PrepararRevisionAdministracion(await DetalleAsync(usuario, referencia));
    }

    public static BanciRevisionAdministracion PrepararRevisionAdministracion(BanciLibro libro)
    {
        const int limite = 100;
        return new BanciRevisionAdministracion
        {
            TotalCarpetas = libro.Carpetas.Count,
            TotalDelitos = libro.Delitos.Count,
            TotalVictimas = libro.Victimas.Count,
            TotalCambios = libro.Cambios.Count,
            VictimasConCambios = libro.Cambios.Select(c => (c.Carpeta, c.Delito, c.Victima)).Distinct().Count(),
            LimiteMuestra = limite,
            Libro = new BanciLibro
            {
                Entidad = libro.Entidad,
                Anio = libro.Anio,
                Mes = libro.Mes,
                Manual = libro.Manual,
                Actualizacion = libro.Actualizacion,
                Carpetas = libro.Carpetas.Take(limite).ToList(),
                Delitos = libro.Delitos.Take(limite).ToList(),
                Victimas = libro.Victimas.Take(limite).ToList(),
                Cambios = libro.Cambios.Take(limite).ToList()
            }
        };
    }
}
