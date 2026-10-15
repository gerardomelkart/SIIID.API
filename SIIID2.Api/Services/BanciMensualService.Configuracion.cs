using System.Data;
using Dapper;
using SIIID2.Api.Models;

namespace SIIID2.Api.Services;
public sealed partial class BanciMensualService
{
    public async Task<object> CambiarConfiguracionAsync(int usuario, BanciConfiguracionCambio cambio)
    {
        await AccesoAsync(usuario, null, "ADMIN");
        if(cambio.Clave is not ("PERMITIR_CORTES_ANTERIORES" or "VALIDACIONES_CAMPOS"))
            throw new InvalidOperationException("La opción no se puede modificar desde esta vista.");
        if(string.IsNullOrWhiteSpace(cambio.Motivo) || cambio.Motivo.Length > 500)
            throw new InvalidOperationException("Indique un motivo de hasta 500 caracteres.");
        using var db = factory.CrearConexion();
        db.Open();
        using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var bloqueo = await db.ExecuteScalarAsync<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'SIIID2:CONFIGURACION',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @r;", transaction: tx);
            if(bloqueo < 0)
                throw new InvalidOperationException("La configuración está siendo modificada.");
            var version = await db.ExecuteScalarAsync<long>("SELECT version FROM dbo.sistema_configuracion_version WITH(UPDLOCK,HOLDLOCK) WHERE id=1;", transaction: tx);
            if(version != cambio.Version)
                throw new InvalidOperationException("La configuración cambió. Actualice la vista.");
            var anterior = await db.QuerySingleAsync<bool>("SELECT c.habilitado FROM dbo.sistema_configuracion c JOIN dbo.catalogo_modulo m ON m.id_modulo=c.id_modulo WHERE m.clave='BANCI' AND c.clave=@Clave AND c.disponible=1;", cambio, tx);
            version++;
            await db.ExecuteAsync("""
                UPDATE c SET habilitado=@Habilitado FROM dbo.sistema_configuracion c JOIN dbo.catalogo_modulo m ON m.id_modulo=c.id_modulo WHERE m.clave='BANCI' AND c.clave=@Clave;
                UPDATE dbo.sistema_configuracion_version SET version=@version,fecha_modificacion=SYSUTCDATETIME() WHERE id=1;
                INSERT dbo.sistema_configuracion_bitacora(id_usuario,modulo,clave,valor_anterior,valor_nuevo,motivo,version)
                VALUES(@usuario,'BANCI',@Clave,@anterior,@Habilitado,@Motivo,@version);
                """, new { usuario, cambio.Clave, cambio.Habilitado, cambio.Motivo, anterior, version }, tx);
            tx.Commit();
            return new
            {
                esValido = true,
                version
            };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
