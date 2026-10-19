using System.IO.Compression;
using Dapper;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public sealed partial class BanciMensualService
{
    public async Task<object> PeriodosEnviosAsync(int usuario)
    {
        var acceso = await AccesoAsync(usuario);
        using var db = factory.CrearConexion();
        return await db.QueryAsync<BanciPeriodo>("""
            SELECT DISTINCT anio_corte AS Anio,mes_corte AS Mes FROM dbo.banci_carga
            WHERE estado IN('PENDIENTE_APROBACION','CONFIRMADO','CONFIRMADO_ACTUALIZACION','RECHAZADO_ADMIN')
              AND(@nacional=1 OR id_entidad_federativa=@entidad)
            ORDER BY Anio DESC,Mes DESC;
            """, new { nacional = AlcanceNacional(acceso), entidad = acceso.IdEntidadFederativa });
    }

    public async Task<byte[]> AcusesEnviosAsync(int usuario, int anio, int mes)
    {
        var acceso = await AccesoAsync(usuario);
        if(mes is < 1 or > 12 || anio < 1900 || anio > DateTime.UtcNow.Year)
            throw new ArgumentException("Corte no válido.");
        using var db = factory.CrearConexion();
        var envios = await db.QueryAsync<BanciOperacion>("SELECT Referencia,Entidad FROM (" + BanciMensualSql.Operaciones + ") b WHERE b.Anio=@anio AND b.Mes=@mes AND(@nacional=1 OR b.Entidad=@entidad) AND b.Estado IN('PENDIENTE_APROBACION','CONFIRMADO','CONFIRMADO_ACTUALIZACION') ORDER BY b.Entidad,b.Fecha;",
            new { anio, mes, nacional = AlcanceNacional(acceso), entidad = acceso.IdEntidadFederativa });
        using var salida = new MemoryStream();
        using(var zip = new ZipArchive(salida, ZipArchiveMode.Create, leaveOpen: true))
        {
            var numero = 0;
            foreach(var envio in envios)
            {
                var pdf = await AcuseAsync(usuario, envio.Referencia);
                var entrada = zip.CreateEntry($"BANCI_{anio}{mes:D2}_{envio.Entidad:D2}_{++numero:D3}.pdf");
                await using var destino = entrada.Open();
                await destino.WriteAsync(pdf);
            }
            if(numero == 0) throw new InvalidOperationException("No hay acuses disponibles para este corte.");
        }
        return salida.ToArray();
    }
}
