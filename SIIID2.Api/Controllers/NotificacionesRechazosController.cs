using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Security.Claims;

namespace SIIID2.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notificaciones/rechazos")]
public class NotificacionesRechazosController : ControllerBase
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public NotificacionesRechazosController(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    [Authorize(Policy = "MODULO_MENSUAL")]
    [HttpPost("mensual/consumir")]
    public async Task<IActionResult> ConsumirMensual()
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        const string sql = @"
        DECLARE @Rechazos TABLE (id_carga BIGINT NOT NULL);

        UPDATE dbo.carga
        SET rechazo_visto = 1,
            fecha_rechazo_visto = SYSDATETIME()
        OUTPUT INSERTED.id_carga INTO @Rechazos(id_carga)
        WHERE id_usuario_carga = @IdUsuario
          AND estado = N'RECHAZADO_ADMIN'
          AND rechazo_visto = 0;

        SELECT id_carga FROM @Rechazos;";

        return Ok(await ConsumirAsync(sql, idUsuario));
    }

    [Authorize(Policy = "MODULO_SEMANAL")]
    [HttpPost("semanal/consumir")]
    public async Task<IActionResult> ConsumirSemanal()
    {
        if (!ObtenerIdUsuario(out var idUsuario)) return TokenSinUsuario();

        const string sql = @"
        DECLARE @Rechazos TABLE (id_semanal_carga BIGINT NOT NULL);

        UPDATE dbo.semanal_carga
        SET rechazo_visto = 1,
            fecha_rechazo_visto = SYSDATETIME()
        OUTPUT INSERTED.id_semanal_carga INTO @Rechazos(id_semanal_carga)
        WHERE id_usuario_carga = @IdUsuario
          AND estado = N'RECHAZADO_ADMIN'
          AND rechazo_visto = 0;

        SELECT id_semanal_carga FROM @Rechazos;";

        return Ok(await ConsumirAsync(sql, idUsuario));
    }

    private async Task<NotificacionRechazoResponse> ConsumirAsync(string sql, int idUsuario)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();

        var rechazos = await connection.QueryAsync<long>(sql, new { IdUsuario = idUsuario });
        var cantidad = rechazos.Count();

        return new NotificacionRechazoResponse
        {
            HayNotificacion = cantidad > 0,
            Cantidad = cantidad
        };
    }

    private bool ObtenerIdUsuario(out int idUsuario) => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idUsuario);

    private IActionResult TokenSinUsuario()
    {
        return Unauthorized(new
        {
            esValido = false,
            codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
            mensaje = "El token no contiene un id de usuario válido.",
            traceId = HttpContext.TraceIdentifier
        });
    }
}