using System.Text.Json;
using SIIID2.Api.Models;

namespace SIIID2.Api.Middleware;

public class ApiErrorResponseMiddleware
{
    private readonly RequestDelegate _next;

    public ApiErrorResponseMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Solo normalizamos respuestas de la API.
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;

        await using var memoryBody = new MemoryStream();
        context.Response.Body = memoryBody;

        await _next(context);

        var statusCodeOriginal = context.Response.StatusCode;

        if (DebeNormalizarRespuesta(statusCodeOriginal))
        {
            context.Response.Body = originalBody;
            context.Response.Clear();

            // IMPORTANTE:
            // Después de Clear(), hay que volver a poner el status original.
            context.Response.StatusCode = statusCodeOriginal;
            context.Response.ContentType = "application/json";

            var error = CrearError(context, statusCodeOriginal);

            var json = JsonSerializer.Serialize(error, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
            return;
        }

        memoryBody.Seek(0, SeekOrigin.Begin);
        context.Response.Body = originalBody;
        await memoryBody.CopyToAsync(originalBody);
    }

    private static bool DebeNormalizarRespuesta(int statusCode)
    {
        return statusCode == StatusCodes.Status404NotFound
            || statusCode == StatusCodes.Status405MethodNotAllowed
            || statusCode == StatusCodes.Status415UnsupportedMediaType
            || statusCode == StatusCodes.Status413PayloadTooLarge;
    }

    private static ErrorResponse CrearError(HttpContext context, int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status404NotFound => new ErrorResponse
            {
                Codigo = "GENERAL_RUTA_NO_ENCONTRADA",
                Mensaje = "La ruta solicitada no existe.",
                TraceId = context.TraceIdentifier
            },

            StatusCodes.Status405MethodNotAllowed => new ErrorResponse
            {
                Codigo = "GENERAL_METODO_NO_PERMITIDO",
                Mensaje = "El método HTTP utilizado no está permitido para esta ruta.",
                TraceId = context.TraceIdentifier
            },

            StatusCodes.Status415UnsupportedMediaType => new ErrorResponse
            {
                Codigo = "GENERAL_TIPO_CONTENIDO_NO_SOPORTADO",
                Mensaje = "El tipo de contenido enviado no es soportado por este endpoint. Verifique el método HTTP y el Content-Type.",
                TraceId = context.TraceIdentifier
            },

            StatusCodes.Status413PayloadTooLarge => new ErrorResponse
            {
                Codigo = "GENERAL_ARCHIVO_DEMASIADO_GRANDE",
                Mensaje = "El tamaño de la petición excede el límite permitido.",
                TraceId = context.TraceIdentifier
            },

            _ => new ErrorResponse
            {
                Codigo = $"GENERAL_HTTP_{statusCode}",
                Mensaje = "La solicitud no pudo ser procesada.",
                TraceId = context.TraceIdentifier
            }
        };
    }
}