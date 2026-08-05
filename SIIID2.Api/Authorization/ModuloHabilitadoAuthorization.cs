using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using SIIID2.Api.Data;

namespace SIIID2.Api.Authorization;

public sealed class ModuloHabilitadoRequirement : IAuthorizationRequirement
{
    public string ClaveModulo { get; }

    public ModuloHabilitadoRequirement(string claveModulo) => ClaveModulo = claveModulo;
}

public sealed class ModuloHabilitadoHandler : AuthorizationHandler<ModuloHabilitadoRequirement>
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public ModuloHabilitadoHandler(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ModuloHabilitadoRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario))
        {
            return;
        }

        const string sql = @"
            SELECT CAST(CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.usuario u
                INNER JOIN dbo.roles r
                    ON r.id_rol = u.id_rol
                   AND r.activo = 1
                INNER JOIN dbo.usuario_modulo um
                    ON um.id_usuario = u.id_usuario
                   AND um.habilitado = 1
                   AND um.activo = 1
                INNER JOIN dbo.catalogo_modulo m
                    ON m.id_modulo = um.id_modulo
                   AND m.clave = @ClaveModulo
                   AND m.activo = 1
                WHERE u.id_usuario = @IdUsuario
                  AND u.activo = 1
            ) THEN 1 ELSE 0 END AS bit);";

        using var connection = _dbConnectionFactory.CrearConexion();

        var habilitado = await connection.ExecuteScalarAsync<bool>(sql, new
        {
            IdUsuario = idUsuario,
            ClaveModulo = requirement.ClaveModulo
        });

        if (habilitado) context.Succeed(requirement);
    }
}