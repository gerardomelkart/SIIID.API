using System.Data;
using System.Globalization;
using Dapper;
using SIIID2.Api.Models;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;

public sealed partial class BanciMensualService
{
    public async Task<NotificacionRechazoResponse> ConsumirRechazosAsync(int usuario)
    {
        var acceso = await AccesoAsync(usuario);
        if(acceso.Rol != "ENLACE_ESTATAL") return new();
        using var db = factory.CrearConexion();
        db.Open();
        using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        var filas = await db.QueryAsync<long>("""
            INSERT dbo.banci_carga_bitacora_estado(id_banci_carga,estado_anterior,estado_nuevo,id_usuario,comentario)
            OUTPUT INSERTED.id_banci_carga
            SELECT c.id_banci_carga,c.estado,c.estado,@usuario,N'Notificación de rechazo consultada.'
            FROM dbo.banci_carga c WITH(UPDLOCK,HOLDLOCK)
            WHERE c.id_usuario_carga=@usuario AND c.id_entidad_federativa=@entidad AND c.estado='RECHAZADO_ADMIN'
              AND NOT EXISTS(SELECT 1 FROM dbo.banci_carga_bitacora_estado b
                  WHERE b.id_banci_carga=c.id_banci_carga AND b.id_usuario=@usuario
                    AND b.comentario=N'Notificación de rechazo consultada.');
            """, new { usuario, entidad = acceso.IdEntidadFederativa }, tx);
        var cantidad = filas.Count();
        tx.Commit();
        return new() { Cantidad = cantidad, HayNotificacion = cantidad > 0 };
    }

    public async Task<BanciRecordatorio> RecordatorioAsync(int usuario)
    {
        var acceso = await AccesoAsync(usuario);
        if(acceso.Rol != "ENLACE_ESTATAL" || !acceso.HabilitaCarga || !acceso.IdEntidadFederativa.HasValue) return new();
        var corte = FechaRangoValidator.Hoy().AddMonths(-1);
        using var db = factory.CrearConexion();
        var pendientes = await OrigenAsync(db, acceso.IdEntidadFederativa.Value, corte.Month, corte.Year, false);
        await ExcluirCarpetasRegistradasAsync(db, pendientes, incluirEnRevision: true);
        return PrepararRecordatorio(pendientes.Carpetas.Count, corte.Year, corte.Month);
    }

    public static BanciRecordatorio PrepararRecordatorio(int carpetasPendientes, int anio, int mes)
    {
        if(carpetasPendientes <= 0) return new();
        var periodo = new DateTime(anio, mes, 1).ToString("MMMM 'de' yyyy", CultureInfo.GetCultureInfo("es-MX"));
        return new() { HayPendiente = true, Titulo = "Carga BANCI pendiente", Periodo = periodo,
            Mensaje = $"Falta completar la carga BANCI correspondiente al corte de {periodo}. Hay {carpetasPendientes} carpeta(s) pendientes de enviar. Favor de realizar la carga correspondiente." };
    }
}
