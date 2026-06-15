using System.Security.Claims;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Middleware;

public class CambioPasswordObligatorioMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CambioPasswordObligatorioMiddleware> _logger;

    public CambioPasswordObligatorioMiddleware(RequestDelegate next, ILogger<CambioPasswordObligatorioMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUsuarioRepository usuarioRepository)
    {
        // Permite las solicitudes preflight de CORS.
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }

        // Solo se revisan usuarios que ya fueron autenticados.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var idUsuarioTexto = context.User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!int.TryParse(idUsuarioTexto, out var idUsuario))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            await context.Response.WriteAsJsonAsync(new
            {
                esValido = false,
                codigo = "GENERAL_TOKEN_SIN_ID_USUARIO",
                mensaje = "El token no contiene un id de usuario válido."
            });

            return;
        }

        var requiereCambio =
            await usuarioRepository.ObtenerRequiereCambioPasswordAsync(idUsuario);

        if (!requiereCambio.HasValue)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            await context.Response.WriteAsJsonAsync(new
            {
                esValido = false,
                codigo = "USUARIO_NO_ACTIVO",
                mensaje = "El usuario autenticado no existe o no está activo."
            });

            return;
        }

        if (!requiereCambio.Value)
        {
            await _next(context);
            return;
        }

        // Mientras la bandera sea 1, esta es la única operación permitida.
        if (context.Request.Path.StartsWithSegments(
                "/api/auth/cambiar-password"))
        {
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "Solicitud bloqueada por cambio obligatorio de contraseña. IdUsuario: {IdUsuario}, Ruta: {Ruta}",
            idUsuario,
            context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        await context.Response.WriteAsJsonAsync(new
        {
            esValido = false,
            codigo = "CAMBIO_PASSWORD_REQUERIDO",
            mensaje = "Debe cambiar su contraseña antes de continuar."
        });
    }
}