using SIIID2.Api.Models;

namespace SIIID2.Api.Middleware;

public static class StatusCodeMiddlewareExtensions
{
    public static IApplicationBuilder UseCustomStatusCodeResponses(this IApplicationBuilder app)
    {
        return app.UseStatusCodePages(async context =>
        {
            var httpContext = context.HttpContext;
            var response = httpContext.Response;

            // Si ya se empezó a escribir respuesta, no intentamos modificarla.
            if (response.HasStarted)
            {
                return;
            }

            response.ContentType = "application/json";

            var error = response.StatusCode switch
            {
                StatusCodes.Status400BadRequest => new ErrorResponse
                {
                    Codigo = "GENERAL_SOLICITUD_INVALIDA",
                    Mensaje = "La solicitud enviada no es válida.",
                    TraceId = httpContext.TraceIdentifier
                },

                StatusCodes.Status404NotFound => new ErrorResponse
                {
                    Codigo = "GENERAL_RUTA_NO_ENCONTRADA",
                    Mensaje = "La ruta solicitada no existe.",
                    TraceId = httpContext.TraceIdentifier
                },

                StatusCodes.Status405MethodNotAllowed => new ErrorResponse
                {
                    Codigo = "GENERAL_METODO_NO_PERMITIDO",
                    Mensaje = "El método HTTP utilizado no está permitido para esta ruta.",
                    TraceId = httpContext.TraceIdentifier
                },

                StatusCodes.Status415UnsupportedMediaType => new ErrorResponse
                {
                    Codigo = "GENERAL_TIPO_CONTENIDO_NO_SOPORTADO",
                    Mensaje = "El tipo de contenido enviado no es soportado por este endpoint.",
                    TraceId = httpContext.TraceIdentifier
                },

                StatusCodes.Status413PayloadTooLarge => new ErrorResponse
                {
                    Codigo = "GENERAL_ARCHIVO_DEMASIADO_GRANDE",
                    Mensaje = "El tamaño de la petición excede el límite permitido.",
                    TraceId = httpContext.TraceIdentifier
                },

                _ => new ErrorResponse
                {
                    Codigo = $"GENERAL_HTTP_{response.StatusCode}",
                    Mensaje = "La solicitud no pudo ser procesada.",
                    TraceId = httpContext.TraceIdentifier
                }
            };

            await response.WriteAsJsonAsync(error);
        });
    }
}