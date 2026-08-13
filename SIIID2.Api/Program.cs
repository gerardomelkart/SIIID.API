using Microsoft.AspNetCore.Http.Features;
using SIIID2.Api.Readers;
using SIIID2.Api.Services;
using SIIID2.Api.Validators;
using SIIID2.Api.Data;
using SIIID2.Api.Repositories;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using SIIID2.Api.Middleware;
using Serilog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authorization;
using SIIID2.Api.Authorization;

// Punto de arranque de la API.
// Aquí se registran servicios, controladores, Swagger y configuración general.
var builder = WebApplication.CreateBuilder(args);

// Habilita controladores MVC/API.
builder.Services.AddControllers();


builder.Services.AddMemoryCache();

// Configuración de respuestas personalizadas para errores de modelo.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errores = context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .Where(x => !string.Equals(x.Key, "request", StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Value!.Errors.Select(error => new
            {
                campo = LimpiarNombreCampoModelo(x.Key),
                mensaje = string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "El valor enviado no es válido."
                    : error.ErrorMessage
            }))
            .ToList();

        var response = new
        {
            esValido = false,
            codigo = "GENERAL_MODELO_INVALIDO",
            mensaje = "La solicitud contiene campos inválidos.",
            errores,
            traceId = context.HttpContext.TraceIdentifier
        };

        return new BadRequestObjectResult(response);
    };
});

// Aumenta el límite permitido para peticiones multipart/form-data.
// Por ahora se permiten hasta 150 MB en total por petición.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 150 * 1024 * 1024;
});

// Configuración de Swagger para probar la API desde navegador.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Registro del lector de archivos.
// Cuando alguien pida IArchivoReader, ASP.NET entregará ArchivoReader.
builder.Services.AddScoped<IArchivoReader, ArchivoReader>();

// Registro de conexión a base de datos.
builder.Services.AddScoped<IDbConnectionFactory, SqlServerConnectionFactory>();

// Registro de repositorios.
builder.Services.AddScoped<ICatalogoRepository, CatalogoRepository>();

// Registro de validadores específicos por archivo, validación cruzada y catálogos.
builder.Services.AddScoped<CarpetasValidator>();
builder.Services.AddScoped<DelitosValidator>();
builder.Services.AddScoped<VictimasValidator>();
builder.Services.AddScoped<CargaIntegridadValidator>();
builder.Services.AddScoped<CatalogosValidator>();
builder.Services.AddScoped<ICargaRepository, CargaRepository>();
builder.Services.AddScoped<IActualizacionCargaRepository, ActualizacionCargaRepository>();
builder.Services.AddScoped<IAcuseRepository, AcuseRepository>();
builder.Services.AddScoped<IActualizacionDiferenciasRepository, ActualizacionDiferenciasRepository>();
builder.Services.AddScoped<IActualizacionRepository, ActualizacionRepository>();
builder.Services.AddScoped<IAdministracionCargasRepository, AdministracionCargasRepository>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<ISemanalDelitoRepository, SemanalDelitoRepository>();
builder.Services.AddScoped<ISemanalCargaRepository, SemanalCargaRepository>();
builder.Services.AddScoped<ISemanalAdministracionCargasRepository, SemanalAdministracionCargasRepository>();
builder.Services.AddScoped<ISemanalEnviosRepository, SemanalEnviosRepository>();

// Registro del servicio principal de carga.
// Cuando el controller pida ICargaArchivosService, se usará CargaArchivosService.
builder.Services.AddScoped<ICargaArchivosService, CargaArchivosService>();
// Registro del servicio de actualizaciones.
builder.Services.AddScoped<IActualizacionArchivosService, ActualizacionArchivosService>();

// Registro del servicio de autenticación.
builder.Services.AddScoped<IAuthService, AuthService>();

// Registro del servicio de generación de acuses PDF.
builder.Services.AddScoped<IAcusePdfService, AcusePdfService>();

// Registro del servicio de usuarios.
builder.Services.AddScoped<IUsuarioService, UsuarioService>();
builder.Services.AddScoped<ISemanalDelitoService, SemanalDelitoService>();
builder.Services.AddScoped<ISemanalCargaService, SemanalCargaService>();

builder.Services.AddScoped<ISemanalAdministracionCargasService, SemanalAdministracionCargasService>();
builder.Services.AddScoped<ISemanalEnviosService, SemanalEnviosService>();

builder.Services.AddScoped<ISemanalAcusePdfService, SemanalAcusePdfService>();

//para los informes
builder.Services.AddScoped<IInformeRepository, InformeRepository>();
builder.Services.AddScoped<IInformeService, InformeService>();
builder.Services.AddScoped<IUltimosArchivosEntidadService, UltimosArchivosEntidadService>();

builder.Services.AddScoped<IAdministracionCargasService, AdministracionCargasService>();



// Configuración de autenticación JWT.
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"] ?? throw new InvalidOperationException("No se encontró Jwt:SecretKey.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? throw new InvalidOperationException("No se encontró Jwt:Issuer.");
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("No se encontró Jwt:Audience.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            // Valida que el token haya sido emitido por esta API.
            ValidateIssuer = true,
            // Valida que el token sea para los clientes esperados.
            ValidateAudience = true,
            // Valida que el token no esté expirado.
            ValidateLifetime = true,
            // Valida la firma del token.
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            // Llave secreta para validar la firma.
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey))
        };

        // Personaliza la respuesta cuando el token falta, está mal formado,
        // está vencido o no pasa la validación.
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                // Evita que ASP.NET mande la respuesta 401 default vacía.
                context.HandleResponse();

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";

                var mensaje = "No autorizado. Debe enviar un Bearer Token válido.";

                // Si el token expiró, damos un mensaje más específico.
                if (context.AuthenticateFailure is SecurityTokenExpiredException)
                {
                    mensaje = "El token expiró. Debe generar uno nuevo.";
                }

                var response = new
                {
                    esValido = false,
                    codigo = "GENERAL_TOKEN_INVALIDO",
                    mensaje
                };

                await context.Response.WriteAsJsonAsync(response);
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    esValido = false,
                    codigo = "GENERAL_SIN_PERMISO",
                    mensaje = "El usuario no tiene permiso para acceder al recurso solicitado."
                });
            }
        };
    });

builder.Services.AddScoped<IAuthorizationHandler, ModuloHabilitadoHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MODULO_MENSUAL", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ModuloHabilitadoRequirement("MENSUAL"));
    });

    options.AddPolicy("MODULO_SEMANAL", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ModuloHabilitadoRequirement("SEMANAL"));
    });

    options.AddPolicy("MODULO_FEDERAL", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ModuloHabilitadoRequirement("FEDERAL"));
    });
});

//para los logs
var rutaLogs = Path.Combine(builder.Environment.ContentRootPath, "logs", "siiid-api-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        rutaLogs,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

builder.Host.UseSerilog();

static string LimpiarNombreCampoModelo(string campo)
{
    if (string.IsNullOrWhiteSpace(campo))
    {
        return string.Empty;
    }

    // ASP.NET puede mandar campos tipo "$.nuevaPassword".
    // Para el front es más claro regresar solo "nuevaPassword".
    if (campo.StartsWith("$."))
    {
        return campo[2..];
    }

    // Por si llega algo como "$".
    if (campo == "$")
    {
        return "body";
    }

    return campo;
}


builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();


// Manejo global de errores no controlados.
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<ApiErrorResponseMiddleware>();

// Swagger solo se habilita en ambiente de desarrollo.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Redirige peticiones HTTP a HTTPS.
app.UseHttpsRedirection();

//igual para los tokens
app.UseAuthentication();

app.UseMiddleware<CambioPasswordObligatorioMiddleware>();

app.UseAuthorization();

// Mapea los controllers, por ejemplo: /api/cargas/validar.
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/swagger"));
}
else
{
    app.MapGet("/", () => Results.Ok(new
    {
        sistema = "SIIID2.Api",
        estado = "OK"
    }));
}

app.Run();
