using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class UsuarioRepository : IUsuarioRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public UsuarioRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<UsuarioCargaInfo?> ObtenerUsuarioCargaAsync(int idUsuario)
    {
        // Se obtiene el usuario activo, su rol y sus permisos de carga.
        // Si no existe registro en habilita_carga_modificacion, se toma como false.
        var sql = @"
            SELECT
                u.id_usuario AS IdUsuario,
                u.id_entidad_federativa AS IdEntidadFederativa,
                r.rol AS Rol,
                COALESCE(h.habilita_carga, 0) AS HabilitaCarga,
                COALESCE(h.habilita_modificacion, 0) AS HabilitaModificacion
            FROM usuario u
            INNER JOIN roles r
                ON r.id_rol = u.id_rol
            LEFT JOIN habilita_carga_modificacion h
                ON h.id_usuario = u.id_usuario
               AND h.activo = 1
            WHERE u.id_usuario = @IdUsuario
              AND u.activo = 1
              AND r.activo = 1;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<UsuarioCargaInfo>(sql, new
        {
            IdUsuario = idUsuario
        });
    }

    public async Task<UsuarioAuthInfo?> ObtenerUsuarioAuthAsync(string usuario)
    {
        // Obtiene los datos necesarios para login.
        // La contraseña en base debe estar hasheada con BCrypt.
        var sql = @"
            SELECT
                u.id_usuario AS IdUsuario,
                u.usuario AS Usuario,
                u.password AS PasswordHash,
                u.nombre AS Nombre,
                u.primer_apellido AS PrimerApellido,
                u.segundo_apellido AS SegundoApellido,
                r.rol AS Rol,
                u.id_entidad_federativa AS IdEntidadFederativa,
                ef.nombre AS EntidadFederativa,
                COALESCE(h.habilita_carga, 0) AS HabilitaCarga,
                COALESCE(h.habilita_modificacion, 0) AS HabilitaModificacion
            FROM usuario u
            INNER JOIN roles r
                ON r.id_rol = u.id_rol
               AND r.activo = 1
            LEFT JOIN catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = u.id_entidad_federativa
               AND ef.activo = 1
            LEFT JOIN habilita_carga_modificacion h
                ON h.id_usuario = u.id_usuario
               AND h.activo = 1
            WHERE u.usuario = @Usuario
              AND u.activo = 1;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<UsuarioAuthInfo>(sql, new
        {
            Usuario = usuario.Trim()
        });
    }

    public async Task<string?> ObtenerDuplicadoUsuarioAsync(string usuario, string correoElectronico, string rfc, string curp)
    {
        // Regresa el primer campo duplicado encontrado.
        var sql = @"
            IF EXISTS (SELECT 1 FROM usuario WHERE usuario = @Usuario)
                SELECT 'USUARIO' AS Duplicado;
            ELSE IF EXISTS (SELECT 1 FROM usuario WHERE correo_electronico = @CorreoElectronico)
                SELECT 'CORREO' AS Duplicado;
            ELSE IF EXISTS (SELECT 1 FROM usuario WHERE rfc = @Rfc)
                SELECT 'RFC' AS Duplicado;
            ELSE IF EXISTS (SELECT 1 FROM usuario WHERE curp = @Curp)
                SELECT 'CURP' AS Duplicado;
            ELSE
                SELECT NULL AS Duplicado;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<string?>(sql, new
        {
            Usuario = usuario.Trim(),
            CorreoElectronico = correoElectronico.Trim(),
            Rfc = rfc.Trim().ToUpperInvariant(),
            Curp = curp.Trim().ToUpperInvariant()
        });
    }

    public async Task<int?> ObtenerIdRolActivoAsync(string rol)
    {
        var sql = @"
            SELECT id_rol
            FROM roles
            WHERE rol = @Rol
              AND activo = 1;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<int?>(sql, new
        {
            Rol = rol.Trim().ToUpperInvariant()
        });
    }

    public async Task<bool> ExisteEntidadActivaAsync(int idEntidadFederativa)
    {
        var sql = @"
            SELECT COUNT(1)
            FROM catalogo_entidad_federativa
            WHERE id_entidad_federativa = @IdEntidadFederativa
              AND activo = 1;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var total = await connection.ExecuteScalarAsync<int>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa
        });

        return total > 0;
    }

    public async Task<int> CrearUsuarioAsync(CrearUsuarioRequest request, int idRol, string passwordHash, int idUsuarioAlta)
    {
        // Inserta usuario y permisos en una sola transacción.
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var sqlUsuario = @"
                INSERT INTO usuario (
                    usuario,
                    password,
                    nombre,
                    primer_apellido,
                    segundo_apellido,
                    correo_electronico,
                    rfc,
                    curp,
                    telefono_contacto,
                    id_entidad_federativa,
                    fecha_alta,
                    fecha_modificacion,
                    id_usuario_alta,
                    id_usuario_modificacion,
                    id_rol,
                    activo
                )
                OUTPUT INSERTED.id_usuario
                VALUES (
                    @Usuario,
                    @Password,
                    @Nombre,
                    @PrimerApellido,
                    @SegundoApellido,
                    @CorreoElectronico,
                    @Rfc,
                    @Curp,
                    @TelefonoContacto,
                    @IdEntidadFederativa,
                    SYSDATETIME(),
                    SYSDATETIME(),
                    @IdUsuarioAlta,
                    @IdUsuarioAlta,
                    @IdRol,
                    1
                );
            ";

            var idUsuario = await connection.ExecuteScalarAsync<int>(
                sqlUsuario,
                new
                {
                    Usuario = request.Usuario.Trim(),
                    Password = passwordHash,
                    Nombre = request.Nombre.Trim(),
                    PrimerApellido = request.PrimerApellido.Trim(),
                    SegundoApellido = string.IsNullOrWhiteSpace(request.SegundoApellido) ? null : request.SegundoApellido.Trim(),
                    CorreoElectronico = request.CorreoElectronico.Trim(),
                    Rfc = request.Rfc.Trim().ToUpperInvariant(),
                    Curp = request.Curp.Trim().ToUpperInvariant(),
                    TelefonoContacto = string.IsNullOrWhiteSpace(request.TelefonoContacto) ? null : request.TelefonoContacto.Trim(),
                    IdEntidadFederativa = request.IdEntidadFederativa,
                    IdUsuarioAlta = idUsuarioAlta,
                    IdRol = idRol
                },
                transaction);

            var sqlPermisos = @"
                INSERT INTO habilita_carga_modificacion (
                    habilita_carga,
                    habilita_modificacion,
                    id_usuario,
                    activo
                )
                VALUES (
                    @HabilitaCarga,
                    @HabilitaModificacion,
                    @IdUsuario,
                    1
                );
            ";

            await connection.ExecuteAsync(
                sqlPermisos,
                new
                {
                    request.HabilitaCarga,
                    request.HabilitaModificacion,
                    IdUsuario = idUsuario
                },
                transaction);

            await transaction.CommitAsync();

            return idUsuario;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}