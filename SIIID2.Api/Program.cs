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

// Punto de arranque de la API.
// Aquí se registran servicios, controladores, Swagger y configuración general.
var builder = WebApplication.CreateBuilder(args);

// Habilita controladores MVC/API.
builder.Services.AddControllers();

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

// Registro de validadores específicos por archivo y ya como tal la validacion cruzada.
builder.Services.AddScoped<CarpetasValidator>();
builder.Services.AddScoped<DelitosValidator>();
builder.Services.AddScoped<VictimasValidator>();
builder.Services.AddScoped<CargaIntegridadValidator>();
builder.Services.AddScoped<CatalogosValidator>();
builder.Services.AddScoped<ICargaRepository, CargaRepository>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();

// Registro del servicio principal de carga.
// Cuando el controller pida ICargaArchivosService, se usará CargaArchivosService.
builder.Services.AddScoped<ICargaArchivosService, CargaArchivosService>();
//para el login
builder.Services.AddScoped<IAuthService, AuthService>();
//para que jale el pdf
builder.Services.AddScoped<IAcusePdfService, AcusePdfService>();

//para que jale el token
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException("No se encontró Jwt:SecretKey.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("No se encontró Jwt:Issuer.");

var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("No se encontró Jwt:Audience.");

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
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecretKey))
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
            }
        };
    });

builder.Services.AddAuthorization();

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

var app = builder.Build();



// Manejo global de errores no controlados.
app.UseMiddleware<ExceptionHandlingMiddleware>();

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
app.UseAuthorization();

// Mapea los controllers, por ejemplo: /api/cargas/validar.
app.MapControllers();

// Ruta raíz temporal para abrir Swagger al entrar a https://localhost:puerto/.
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();
