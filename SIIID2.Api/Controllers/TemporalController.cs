using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace SIIID2.Api.Controllers;

[ApiController]
[Route("api/temporal")]
public class TemporalController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public TemporalController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("hash-password")]
    public IActionResult HashPassword([FromBody] HashPasswordRequest request)
    {
        // Valida que venga la contraseña.
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new
            {
                mensaje = "Debe enviar la contraseña."
            });
        }

        // Genera hash BCrypt para guardar en base.
        var hash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        return Ok(new
        {
            passwordOriginal = request.Password,
            hash
        });
    }

    [HttpPost("token-prueba")]
    public IActionResult TokenPrueba([FromBody] TokenPruebaRequest request)
    {
        // Leemos configuración JWT.
        var jwtSecretKey = _configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("No se encontró Jwt:SecretKey.");

        var jwtIssuer = _configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("No se encontró Jwt:Issuer.");

        var jwtAudience = _configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("No se encontró Jwt:Audience.");

        var minutesToExpire = int.TryParse(_configuration["Jwt:MinutesToExpire"], out var minutos)
            ? minutos
            : 480;

        // El id_usuario se agrega al token.
        // Este valor será leído después por CargasController.
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, request.IdUsuario.ToString()),
            new Claim(ClaimTypes.Name, request.Usuario)
        };

        // Llave usada para firmar el token.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        // Token temporal para pruebas.
        var token = new JwtSecurityToken(
            issuer: jwtIssuer,
            audience: jwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(minutesToExpire),
            signingCredentials: credentials);

        var tokenTexto = new JwtSecurityTokenHandler().WriteToken(token);

        return Ok(new
        {
            token = tokenTexto,
            expiraEnMinutos = minutesToExpire
        });
    }
}

public class HashPasswordRequest
{
    public string Password { get; set; } = string.Empty;
}

public class TokenPruebaRequest
{
    public int IdUsuario { get; set; }
    public string Usuario { get; set; } = string.Empty;
}