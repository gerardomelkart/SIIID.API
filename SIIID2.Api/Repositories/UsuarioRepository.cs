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
        var sql = @"
        SELECT
            u.id_usuario AS IdUsuario,
            u.usuario AS Usuario,
            u.password AS PasswordHash,
            u.requiere_cambio_password AS RequiereCambioPassword,
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

        SELECT
            m.id_modulo AS IdModulo,
            m.clave AS Clave,
            m.nombre AS Nombre,
            COALESCE(um.habilita_carga, 0) AS HabilitaCarga,
            COALESCE(um.habilita_modificacion, 0) AS HabilitaModificacion,
            COALESCE(um.administra_delitos, 0) AS AdministraDelitos
        FROM usuario u
        INNER JOIN usuario_modulo um
            ON um.id_usuario = u.id_usuario
           AND um.habilitado = 1
           AND um.activo = 1
        INNER JOIN catalogo_modulo m
            ON m.id_modulo = um.id_modulo
           AND m.activo = 1
        WHERE u.usuario = @Usuario
          AND u.activo = 1
        ORDER BY m.id_modulo;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        using var resultado = await connection.QueryMultipleAsync(sql, new
        {
            Usuario = usuario.Trim()
        });

        var usuarioAuth = await resultado.ReadFirstOrDefaultAsync<UsuarioAuthInfo>();

        if (usuarioAuth == null)
        {
            return null;
        }

        usuarioAuth.Modulos = (await resultado.ReadAsync<ModuloUsuarioInfo>()).ToList();

        return usuarioAuth;
    }

    public async Task<List<UsuarioListadoItem>> ObtenerUsuariosAsync(bool incluirInactivos)
    {
        var sql = @"
        SELECT
            u.id_usuario AS IdUsuario,
            u.usuario AS Usuario,
            LTRIM(RTRIM(CONCAT(
                u.nombre,
                ' ',
                u.primer_apellido,
                ' ',
                ISNULL(u.segundo_apellido, '')
            ))) AS NombreCompleto,
            u.correo_electronico AS CorreoElectronico,
            r.rol AS Rol,
            u.id_entidad_federativa AS IdEntidadFederativa,
            ef.nombre AS EntidadFederativa,
            COALESCE(h.habilita_carga, 0) AS HabilitaCarga,
            COALESCE(h.habilita_modificacion, 0) AS HabilitaModificacion,
            COALESCE(ums.habilitado, 0) AS HabilitaSemanal,
            u.activo AS Activo
        FROM usuario u
        INNER JOIN roles r
            ON r.id_rol = u.id_rol
        LEFT JOIN catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = u.id_entidad_federativa
        LEFT JOIN habilita_carga_modificacion h
            ON h.id_usuario = u.id_usuario
        LEFT JOIN catalogo_modulo ms
            ON ms.clave = N'SEMANAL'
        LEFT JOIN usuario_modulo ums
            ON ums.id_usuario = u.id_usuario
           AND ums.id_modulo = ms.id_modulo
        WHERE (@IncluirInactivos = 1 OR u.activo = 1)
        ORDER BY
            u.activo DESC,
            r.rol,
            ef.nombre,
            u.usuario;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var usuarios = await connection.QueryAsync<UsuarioListadoItem>(sql, new
        {
            IncluirInactivos = incluirInactivos
        });

        return usuarios.ToList();
    }

    public async Task<UsuarioDetalle?> ObtenerUsuarioDetalleAsync(int idUsuario)
    {
        var sql = @"
        SELECT
            u.id_usuario AS IdUsuario,
            u.usuario AS Usuario,
            u.nombre AS Nombre,
            u.primer_apellido AS PrimerApellido,
            u.segundo_apellido AS SegundoApellido,
            u.correo_electronico AS CorreoElectronico,
            u.rfc AS Rfc,
            u.curp AS Curp,
            u.telefono_contacto AS TelefonoContacto,
            u.id_entidad_federativa AS IdEntidadFederativa,
            ef.nombre AS EntidadFederativa,
            u.id_rol AS IdRol,
            r.rol AS Rol,
            COALESCE(h.habilita_carga, 0) AS HabilitaCarga,
            COALESCE(h.habilita_modificacion, 0) AS HabilitaModificacion,
            COALESCE(ums.habilitado, 0) AS HabilitaSemanal,
            COALESCE(ums.habilita_carga, 0) AS HabilitaCargaSemanal,
            COALESCE(ums.habilita_modificacion, 0) AS HabilitaModificacionSemanal,
            COALESCE(ums.administra_delitos, 0) AS AdministraDelitosSemanal,
            u.fecha_alta AS FechaAlta,
            u.fecha_modificacion AS FechaModificacion,
            u.activo AS Activo
        FROM usuario u
        INNER JOIN roles r
            ON r.id_rol = u.id_rol
        LEFT JOIN catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = u.id_entidad_federativa
        LEFT JOIN habilita_carga_modificacion h
            ON h.id_usuario = u.id_usuario
        LEFT JOIN catalogo_modulo ms
            ON ms.clave = N'SEMANAL'
        LEFT JOIN usuario_modulo ums
            ON ums.id_usuario = u.id_usuario
           AND ums.id_modulo = ms.id_modulo
        WHERE u.id_usuario = @IdUsuario;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<UsuarioDetalle>(sql, new
        {
            IdUsuario = idUsuario
        });
    }

    public async Task<List<UsuarioValidacionError>> ObtenerDuplicadosUsuarioAsync(string usuario, string correoElectronico, string rfc, string curp)
    {
        // Regresa todos los campos duplicados encontrados.
        // Esto permite informar al front todos los problemas en una sola respuesta.
        var sql = @"
        SELECT
            'usuario' AS Campo,
            'USUARIO_USUARIO_DUPLICADO' AS Codigo,
            'Ya existe un usuario registrado con ese nombre de usuario.' AS Mensaje
        WHERE EXISTS (
            SELECT 1
            FROM usuario
            WHERE usuario = @Usuario
        )

        UNION ALL

        SELECT
            'correoElectronico' AS Campo,
            'USUARIO_CORREO_DUPLICADO' AS Codigo,
            'Ya existe un usuario registrado con ese correo electrónico.' AS Mensaje
        WHERE EXISTS (
            SELECT 1
            FROM usuario
            WHERE correo_electronico = @CorreoElectronico
        )

        UNION ALL

        SELECT
            'rfc' AS Campo,
            'USUARIO_RFC_DUPLICADO' AS Codigo,
            'Ya existe un usuario registrado con ese RFC.' AS Mensaje
        WHERE EXISTS (
            SELECT 1
            FROM usuario
            WHERE rfc = @Rfc
        )

        UNION ALL

        SELECT
            'curp' AS Campo,
            'USUARIO_CURP_DUPLICADO' AS Codigo,
            'Ya existe un usuario registrado con esa CURP.' AS Mensaje
        WHERE EXISTS (
            SELECT 1
            FROM usuario
            WHERE curp = @Curp
        );
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var errores = await connection.QueryAsync<UsuarioValidacionError>(sql, new
        {
            Usuario = usuario.Trim(),
            CorreoElectronico = correoElectronico.Trim(),
            Rfc = rfc.Trim().ToUpperInvariant(),
            Curp = curp.Trim().ToUpperInvariant()
        });

        return errores.ToList();
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
                                requiere_cambio_password,
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
                                1,
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

            await GuardarPermisosModularesAsync(connection, transaction, idUsuario, request.HabilitaCarga, request.HabilitaModificacion, request.HabilitaSemanal, request.HabilitaCargaSemanal, request.HabilitaModificacionSemanal, request.AdministraDelitosSemanal, idUsuarioAlta);

            await transaction.CommitAsync();

            return idUsuario;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> ExisteUsuarioActivoAsync(int idUsuario)
    {
        var sql = @"
        SELECT COUNT(1)
        FROM usuario
        WHERE id_usuario = @IdUsuario
          AND activo = 1;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var total = await connection.ExecuteScalarAsync<int>(sql, new
        {
            IdUsuario = idUsuario
        });

        return total > 0;
    }

    public async Task<List<UsuarioValidacionError>> ObtenerDuplicadosUsuarioEdicionAsync(int idUsuario, string usuario, string correoElectronico, string rfc, string curp)
    {
        // Regresa todos los duplicados encontrados,
        // excluyendo al propio usuario que se está editando.
        var sql = @"
        SELECT
            'usuario' AS Campo,
            'USUARIO_USUARIO_DUPLICADO' AS Codigo,
            'Ya existe otro usuario registrado con ese nombre de usuario.' AS Mensaje
        WHERE EXISTS (
            SELECT 1
            FROM usuario
            WHERE usuario = @Usuario
              AND id_usuario <> @IdUsuario
        )

        UNION ALL

        SELECT
            'correoElectronico' AS Campo,
            'USUARIO_CORREO_DUPLICADO' AS Codigo,
            'Ya existe otro usuario registrado con ese correo electrónico.' AS Mensaje
        WHERE EXISTS (
            SELECT 1
            FROM usuario
            WHERE correo_electronico = @CorreoElectronico
              AND id_usuario <> @IdUsuario
        )

        UNION ALL

        SELECT
            'rfc' AS Campo,
            'USUARIO_RFC_DUPLICADO' AS Codigo,
            'Ya existe otro usuario registrado con ese RFC.' AS Mensaje
        WHERE EXISTS (
            SELECT 1
            FROM usuario
            WHERE rfc = @Rfc
              AND id_usuario <> @IdUsuario
        )

        UNION ALL

        SELECT
            'curp' AS Campo,
            'USUARIO_CURP_DUPLICADO' AS Codigo,
            'Ya existe otro usuario registrado con esa CURP.' AS Mensaje
        WHERE EXISTS (
            SELECT 1
            FROM usuario
            WHERE curp = @Curp
              AND id_usuario <> @IdUsuario
        );
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var errores = await connection.QueryAsync<UsuarioValidacionError>(sql, new
        {
            IdUsuario = idUsuario,
            Usuario = usuario.Trim(),
            CorreoElectronico = correoElectronico.Trim(),
            Rfc = rfc.Trim().ToUpperInvariant(),
            Curp = curp.Trim().ToUpperInvariant()
        });

        return errores.ToList();
    }

    public async Task EditarUsuarioAsync(int idUsuario, EditarUsuarioRequest request, int idRol, string? passwordHash, int idUsuarioModificacion)
    {
        // Edita usuario y permisos en una sola transacción.
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var sqlUsuario = @"
            UPDATE usuario
            SET usuario = @Usuario,
                nombre = @Nombre,
                primer_apellido = @PrimerApellido,
                segundo_apellido = @SegundoApellido,
                correo_electronico = @CorreoElectronico,
                rfc = @Rfc,
                curp = @Curp,
                telefono_contacto = @TelefonoContacto,
                id_entidad_federativa = @IdEntidadFederativa,
                id_rol = @IdRol,
                fecha_modificacion = SYSDATETIME(),
                id_usuario_modificacion = @IdUsuarioModificacion,

                password = CASE
                    WHEN @PasswordHash IS NULL THEN password
                    ELSE @PasswordHash
                END,

                requiere_cambio_password = CASE
                    WHEN @PasswordHash IS NULL THEN requiere_cambio_password
                    ELSE 1
                END
            WHERE id_usuario = @IdUsuario
              AND activo = 1;
        ";

            await connection.ExecuteAsync(
                sqlUsuario,
                new
                {
                    IdUsuario = idUsuario,
                    Usuario = request.Usuario.Trim(),
                    Nombre = request.Nombre.Trim(),
                    PrimerApellido = request.PrimerApellido.Trim(),
                    SegundoApellido = string.IsNullOrWhiteSpace(request.SegundoApellido) ? null : request.SegundoApellido.Trim(),
                    CorreoElectronico = request.CorreoElectronico.Trim(),
                    Rfc = request.Rfc.Trim().ToUpperInvariant(),
                    Curp = request.Curp.Trim().ToUpperInvariant(),
                    TelefonoContacto = string.IsNullOrWhiteSpace(request.TelefonoContacto) ? null : request.TelefonoContacto.Trim(),
                    IdEntidadFederativa = request.IdEntidadFederativa,
                    IdRol = idRol,
                    IdUsuarioModificacion = idUsuarioModificacion,
                    PasswordHash = passwordHash
                },
                transaction);

            var sqlPermisos = @"
            IF EXISTS (
                SELECT 1
                FROM habilita_carga_modificacion
                WHERE id_usuario = @IdUsuario
            )
            BEGIN
                UPDATE habilita_carga_modificacion
                SET habilita_carga = @HabilitaCarga,
                    habilita_modificacion = @HabilitaModificacion,
                    activo = 1
                WHERE id_usuario = @IdUsuario;
            END
            ELSE
            BEGIN
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
            END
        ";

            await connection.ExecuteAsync(
                sqlPermisos,
                new
                {
                    IdUsuario = idUsuario,
                    request.HabilitaCarga,
                    request.HabilitaModificacion
                },
                transaction);

            await GuardarPermisosModularesAsync(connection, transaction, idUsuario, request.HabilitaCarga, request.HabilitaModificacion, request.HabilitaSemanal, request.HabilitaCargaSemanal, request.HabilitaModificacionSemanal, request.AdministraDelitosSemanal, idUsuarioModificacion);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task DesactivarUsuarioAsync(int idUsuario, int idUsuarioModificacion)
    {
        // Baja lógica. No se elimina físicamente para conservar auditoría.
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var sql = @"
            UPDATE usuario
            SET activo = 0,
                fecha_modificacion = SYSDATETIME(),
                id_usuario_modificacion = @IdUsuarioModificacion
            WHERE id_usuario = @IdUsuario
              AND activo = 1;

            UPDATE habilita_carga_modificacion
            SET habilita_carga = 0,
                habilita_modificacion = 0,
                activo = 0
            WHERE id_usuario = @IdUsuario;

            UPDATE usuario_modulo
            SET activo = 0,
                fecha_modificacion = SYSDATETIME(),
                id_usuario_modificacion = @IdUsuarioModificacion
            WHERE id_usuario = @IdUsuario;
        ";

            await connection.ExecuteAsync(
                sql,
                new
                {
                    IdUsuario = idUsuario,
                    IdUsuarioModificacion = idUsuarioModificacion
                },
                transaction);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<int> ActualizarPermisosGlobalesAsync(bool habilitaCarga, bool habilitaModificacion)
    {
        var sql = @"
        SET NOCOUNT ON;

        UPDATE h
        SET h.habilita_carga = @HabilitaCarga,
            h.habilita_modificacion = @HabilitaModificacion
        FROM habilita_carga_modificacion h
        INNER JOIN usuario u
            ON u.id_usuario = h.id_usuario
        INNER JOIN roles r
            ON r.id_rol = u.id_rol
        WHERE h.activo = 1
          AND u.activo = 1
          AND r.activo = 1
          AND r.rol <> 'CONSULTA';

        UPDATE h
        SET h.habilita_carga = 0,
            h.habilita_modificacion = 0
        FROM habilita_carga_modificacion h
        INNER JOIN usuario u
            ON u.id_usuario = h.id_usuario
        INNER JOIN roles r
            ON r.id_rol = u.id_rol
        WHERE h.activo = 1
          AND u.activo = 1
          AND r.activo = 1
          AND r.rol = 'CONSULTA';

        UPDATE um
        SET um.habilitado = 1,
            um.habilita_carga = @HabilitaCarga,
            um.habilita_modificacion = @HabilitaModificacion,
            um.fecha_modificacion = SYSDATETIME(),
            um.activo = 1
        FROM usuario_modulo um
        INNER JOIN catalogo_modulo m
            ON m.id_modulo = um.id_modulo
           AND m.clave = N'MENSUAL'
        INNER JOIN usuario u
            ON u.id_usuario = um.id_usuario
        INNER JOIN roles r
            ON r.id_rol = u.id_rol
        WHERE u.activo = 1
          AND r.activo = 1
          AND r.rol <> 'CONSULTA';

        UPDATE um
        SET um.habilitado = 1,
            um.habilita_carga = 0,
            um.habilita_modificacion = 0,
            um.fecha_modificacion = SYSDATETIME(),
            um.activo = 1
        FROM usuario_modulo um
        INNER JOIN catalogo_modulo m
            ON m.id_modulo = um.id_modulo
           AND m.clave = N'MENSUAL'
        INNER JOIN usuario u
            ON u.id_usuario = um.id_usuario
        INNER JOIN roles r
            ON r.id_rol = u.id_rol
        WHERE u.activo = 1
          AND r.activo = 1
          AND r.rol = 'CONSULTA';

        SELECT COUNT(*)
        FROM usuario u
        INNER JOIN roles r
            ON r.id_rol = u.id_rol
           AND r.activo = 1
        WHERE u.activo = 1;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.ExecuteScalarAsync<int>(sql, new
        {
            HabilitaCarga = habilitaCarga,
            HabilitaModificacion = habilitaModificacion
        });
    }

    public async Task<bool> ExisteUsuarioAsync(int idUsuario)
    {
        // Valida existencia del usuario sin importar si está activo o inactivo.
        var sql = @"
        SELECT COUNT(1)
        FROM usuario
        WHERE id_usuario = @IdUsuario;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var total = await connection.ExecuteScalarAsync<int>(sql, new
        {
            IdUsuario = idUsuario
        });

        return total > 0;
    }

    public async Task ReactivarUsuarioAsync(int idUsuario, ReactivarUsuarioRequest request, int idUsuarioModificacion)
    {
        // Reactiva usuario y permisos en una sola transacción.
        // No cambia contraseña, rol, entidad ni datos personales.
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var sqlUsuario = @"
            UPDATE usuario
            SET activo = 1,
                fecha_modificacion = SYSDATETIME(),
                id_usuario_modificacion = @IdUsuarioModificacion
            WHERE id_usuario = @IdUsuario;
        ";

            await connection.ExecuteAsync(
                sqlUsuario,
                new
                {
                    IdUsuario = idUsuario,
                    IdUsuarioModificacion = idUsuarioModificacion
                },
                transaction);

            var sqlPermisos = @"
            IF EXISTS (
                SELECT 1
                FROM habilita_carga_modificacion
                WHERE id_usuario = @IdUsuario
            )
            BEGIN
                UPDATE habilita_carga_modificacion
                SET habilita_carga = @HabilitaCarga,
                    habilita_modificacion = @HabilitaModificacion,
                    activo = 1
                WHERE id_usuario = @IdUsuario;
            END
            ELSE
            BEGIN
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
            END
        ";

            await connection.ExecuteAsync(
                sqlPermisos,
                new
                {
                    IdUsuario = idUsuario,
                    request.HabilitaCarga,
                    request.HabilitaModificacion
                },
                transaction);

            await GuardarPermisosModularesAsync(connection, transaction, idUsuario, request.HabilitaCarga, request.HabilitaModificacion, null, null, null, null, idUsuarioModificacion);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<UsuarioPasswordInfo?> ObtenerUsuarioPasswordAsync(int idUsuario)
    {
        var sql = @"
        SELECT
            id_usuario AS IdUsuario,
            password AS PasswordHash,
            requiere_cambio_password AS RequiereCambioPassword
        FROM usuario
        WHERE id_usuario = @IdUsuario
          AND activo = 1;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<UsuarioPasswordInfo>(
            sql,
            new
            {
                IdUsuario = idUsuario
            });
    }

    public async Task<bool?> ObtenerRequiereCambioPasswordAsync(int idUsuario)
    {
        var sql = @"
        SELECT requiere_cambio_password
        FROM usuario
        WHERE id_usuario = @IdUsuario
          AND activo = 1;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<bool?>(
            sql,
            new
            {
                IdUsuario = idUsuario
            });
    }

    public async Task<bool> ActualizarPasswordPropioAsync(int idUsuario, string passwordHash)
    {
        var sql = @"
        UPDATE usuario
        SET password = @PasswordHash,
            requiere_cambio_password = 0,
            fecha_modificacion = SYSDATETIME(),
            id_usuario_modificacion = @IdUsuario
        WHERE id_usuario = @IdUsuario
          AND activo = 1;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var registrosAfectados = await connection.ExecuteAsync(
            sql,
            new
            {
                IdUsuario = idUsuario,
                PasswordHash = passwordHash
            });

        return registrosAfectados > 0;
    }

    private static async Task GuardarPermisosModularesAsync(SqlConnection connection, SqlTransaction transaction, int idUsuario, bool habilitaCargaMensual, bool habilitaModificacionMensual,    bool? habilitaSemanal, bool? habilitaCargaSemanal, bool? habilitaModificacionSemanal, bool? administraDelitosSemanal, int idUsuarioModificacion)
    {
        var sql = @"
        DECLARE @IdModuloMensual TINYINT =
        (
            SELECT id_modulo
            FROM catalogo_modulo
            WHERE clave = N'MENSUAL'
              AND activo = 1
        );

        DECLARE @IdModuloSemanal TINYINT =
        (
            SELECT id_modulo
            FROM catalogo_modulo
            WHERE clave = N'SEMANAL'
              AND activo = 1
        );

        IF @IdModuloMensual IS NULL OR @IdModuloSemanal IS NULL
        BEGIN
            THROW 50010, 'No fue posible resolver los módulos MENSUAL y SEMANAL.', 1;
        END;

        IF EXISTS
        (
            SELECT 1
            FROM usuario_modulo
            WHERE id_usuario = @IdUsuario
              AND id_modulo = @IdModuloMensual
        )
        BEGIN
            UPDATE usuario_modulo
            SET habilitado = 1,
                habilita_carga = @HabilitaCargaMensual,
                habilita_modificacion = @HabilitaModificacionMensual,
                administra_delitos = 0,
                fecha_modificacion = SYSDATETIME(),
                id_usuario_modificacion = @IdUsuarioModificacion,
                activo = 1
            WHERE id_usuario = @IdUsuario
              AND id_modulo = @IdModuloMensual;
        END
        ELSE
        BEGIN
            INSERT INTO usuario_modulo
            (
                id_usuario,
                id_modulo,
                habilitado,
                habilita_carga,
                habilita_modificacion,
                administra_delitos,
                id_usuario_modificacion,
                activo
            )
            VALUES
            (
                @IdUsuario,
                @IdModuloMensual,
                1,
                @HabilitaCargaMensual,
                @HabilitaModificacionMensual,
                0,
                @IdUsuarioModificacion,
                1
            );
        END;

        IF EXISTS
        (
            SELECT 1
            FROM usuario_modulo
            WHERE id_usuario = @IdUsuario
              AND id_modulo = @IdModuloSemanal
        )
        BEGIN
            UPDATE usuario_modulo
            SET habilitado = COALESCE(@HabilitaSemanal, habilitado),
                habilita_carga = COALESCE(@HabilitaCargaSemanal, habilita_carga),
                habilita_modificacion = COALESCE(@HabilitaModificacionSemanal, habilita_modificacion),
                administra_delitos = COALESCE(@AdministraDelitosSemanal, administra_delitos),
                fecha_modificacion = SYSDATETIME(),
                id_usuario_modificacion = @IdUsuarioModificacion,
                activo = 1
            WHERE id_usuario = @IdUsuario
              AND id_modulo = @IdModuloSemanal;
        END
        ELSE
        BEGIN
            INSERT INTO usuario_modulo
            (
                id_usuario,
                id_modulo,
                habilitado,
                habilita_carga,
                habilita_modificacion,
                administra_delitos,
                id_usuario_modificacion,
                activo
            )
            VALUES
            (
                @IdUsuario,
                @IdModuloSemanal,
                COALESCE(@HabilitaSemanal, 0),
                COALESCE(@HabilitaCargaSemanal, 0),
                COALESCE(@HabilitaModificacionSemanal, 0),
                COALESCE(@AdministraDelitosSemanal, 0),
                @IdUsuarioModificacion,
                1
            );
        END;
    ";

        await connection.ExecuteAsync(
            sql,
            new
            {
                IdUsuario = idUsuario,
                HabilitaCargaMensual = habilitaCargaMensual,
                HabilitaModificacionMensual = habilitaModificacionMensual,
                HabilitaSemanal = habilitaSemanal,
                HabilitaCargaSemanal = habilitaCargaSemanal,
                HabilitaModificacionSemanal = habilitaModificacionSemanal,
                AdministraDelitosSemanal = administraDelitosSemanal,
                IdUsuarioModificacion = idUsuarioModificacion
            },
            transaction);
    }
}