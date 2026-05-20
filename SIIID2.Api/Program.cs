using Microsoft.AspNetCore.Http.Features;
using SIIID2.Api.Readers;
using SIIID2.Api.Services;
using SIIID2.Api.Validators;
using SIIID2.Api.Data;
using SIIID2.Api.Repositories;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

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
builder.Services.AddScoped<IDbConnectionFactory, MySqlConnectionFactory>();

// Registro de repositorios.
builder.Services.AddScoped<ICatalogoRepository, CatalogoRepository>();

// Registro de validadores específicos por archivo y ya como tal la validacion cruzada.
builder.Services.AddScoped<CarpetasValidator>();
builder.Services.AddScoped<DelitosValidator>();
builder.Services.AddScoped<VictimasValidator>();
builder.Services.AddScoped<CargaIntegridadValidator>();
builder.Services.AddScoped<CatalogosValidator>();
builder.Services.AddScoped<ICatalogoRepository, CatalogoRepository>();
builder.Services.AddScoped<ICargaRepository, CargaRepository>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();

// Registro del servicio principal de carga.
// Cuando el controller pida ICargaArchivosService, se usará CargaArchivosService.
builder.Services.AddScoped<ICargaArchivosService, CargaArchivosService>();

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
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

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
