using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class AuthService : IAuthService
{
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IUsuarioRepository usuarioRepository, IConfiguration configuration, ILogger<AuthService> logger)
    {
        _usuarioRepository = usuarioRepository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Usuario) || string.IsNullOrWhiteSpace(request.Password))
        {
            return new LoginResponse
            {
                EsValido = false,
                Mensaje = "Debe enviar usuario y contraseña."
            };
        }

        var usuario = await _usuarioRepository.ObtenerUsuarioAuthAsync(request.Usuario);

        if (usuario == null)
        {
            _logger.LogWarning("Intento de login con usuario inexistente o inactivo: {Usuario}", request.Usuario);

            return new LoginResponse
            {
                EsValido = false,
                Mensaje = "Usuario o contraseña incorrectos."
            };
        }

        bool passwordValida;

        try
        {
            passwordValida = BCrypt.Net.BCrypt.Verify(
                request.Password,
                usuario.PasswordHash?.Trim() ?? string.Empty);
        }
        catch (BCrypt.Net.SaltParseException ex)
        {
            _logger.LogError(
                ex,
                "El usuario tiene una contraseña almacenada con formato inválido. Usuario: {Usuario}, IdUsuario: {IdUsuario}",
                usuario.Usuario,
                usuario.IdUsuario);

            return new LoginResponse
            {
                EsValido = false,
                Mensaje = "Usuario o contraseña incorrectos."
            };
        }

        if (!passwordValida)
        {
            _logger.LogWarning("Intento de login con contraseña incorrecta. Usuario: {Usuario}", request.Usuario);

            return new LoginResponse
            {
                EsValido = false,
                Mensaje = "Usuario o contraseña incorrectos."
            };
        }

        var expiraEnMinutos = ObtenerMinutosExpiracion();
        var token = GenerarToken(usuario, expiraEnMinutos);

        _logger.LogInformation(
            "Login correcto. Usuario: {Usuario}, IdUsuario: {IdUsuario}, Rol: {Rol}",
            usuario.Usuario,
            usuario.IdUsuario,
            usuario.Rol);

        return new LoginResponse
        {
            EsValido = true,
            Mensaje = "Login correcto.",
            Token = token,
            ExpiraEnMinutos = expiraEnMinutos,
            Usuario = new UsuarioLoginInfo
            {
                IdUsuario = usuario.IdUsuario,
                Usuario = usuario.Usuario,
                NombreCompleto = usuario.NombreCompleto,
                Rol = usuario.Rol,
                IdEntidadFederativa = usuario.IdEntidadFederativa,
                EntidadFederativa = usuario.EntidadFederativa,
                HabilitaCarga = usuario.HabilitaCarga,
                HabilitaModificacion = usuario.HabilitaModificacion
            }
        };
    }

    private string GenerarToken(UsuarioAuthInfo usuario, int expiraEnMinutos)
    {
        var jwtSecretKey = _configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("No se encontró Jwt:SecretKey.");

        var jwtIssuer = _configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("No se encontró Jwt:Issuer.");

        var jwtAudience = _configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("No se encontró Jwt:Audience.");

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, usuario.IdUsuario.ToString()),
            new Claim(ClaimTypes.Name, usuario.Usuario),
            new Claim(ClaimTypes.Role, usuario.Rol),
            new Claim("rol", usuario.Rol)
        };

        if (usuario.IdEntidadFederativa.HasValue)
        {
            claims.Add(new Claim("id_entidad_federativa", usuario.IdEntidadFederativa.Value.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiraEnMinutos),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private int ObtenerMinutosExpiracion()
    {
        return int.TryParse(_configuration["Jwt:MinutesToExpire"], out var minutos)
            ? minutos
            : 480;
    }
}