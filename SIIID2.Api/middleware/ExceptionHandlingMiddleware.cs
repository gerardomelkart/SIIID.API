using System.Net;
using System.Text.Json;
using SIIID2.Api.Models;

namespace SIIID2.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Se registra el error completo en logs.
            _logger.LogError(ex, "Ocurrió un error no controlado. TraceId: {TraceId}", context.TraceIdentifier);

            await ManejarExcepcionAsync(context, ex);
        }
    }

    private async Task ManejarExcepcionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var response = new ErrorResponse
        {
            EsValido = false,
            Codigo = "GENERAL_ERROR_INTERNO",
            Mensaje = "Ocurrió un error interno al procesar la solicitud.",
            TraceId = context.TraceIdentifier
        };

        // En desarrollo sí mostramos detalle para depurar.
        // En producción no conviene exponer nombres de tablas, columnas, SQL o rutas internas.
        if (_environment.IsDevelopment())
        {
            response.Detalle = exception.Message;
        }

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}