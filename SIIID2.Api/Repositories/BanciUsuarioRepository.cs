using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class BanciUsuarioRepository : IBanciUsuarioRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    public BanciUsuarioRepository(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    private const string SqlUsuarios = """
        SELECT u.id_usuario AS IdUsuario, u.usuario AS Usuario, u.nombre AS Nombre,
            u.primer_apellido AS PrimerApellido, u.segundo_apellido AS SegundoApellido,
            u.correo_electronico AS CorreoElectronico, u.rfc AS Rfc, u.curp AS Curp,
            u.telefono_contacto AS TelefonoContacto, u.id_rol AS IdRol, r.rol AS Rol,
            u.id_entidad_federativa AS IdEntidadFederativa, ef.nombre AS EntidadFederativa,
            CONVERT(bit, CASE WHEN r.rol = N'SUPER_USUARIO' THEN 1 WHEN um.activo = 1 THEN um.habilitado ELSE 0 END) AS HabilitaBanci,
            CONVERT(bit, CASE WHEN r.rol = N'SUPER_USUARIO' THEN 1 WHEN um.activo = 1 AND um.habilitado = 1 AND r.rol <> N'CONSULTA' THEN um.habilita_carga ELSE 0 END) AS HabilitaCarga,
            CONVERT(bit, CASE WHEN r.rol = N'SUPER_USUARIO' THEN 1 WHEN um.activo = 1 AND um.habilitado = 1 AND r.rol <> N'CONSULTA' THEN um.habilita_modificacion ELSE 0 END) AS HabilitaModificacion,
            CONVERT(bit, CASE WHEN u.activo = 1 AND (r.rol = N'SUPER_USUARIO' OR COALESCE(um.activo, 1) = 1) THEN 1 ELSE 0 END) AS Activo,
            CONVERT(bit, CASE WHEN r.rol = N'SUPER_USUARIO' OR um.id_usuario IS NOT NULL THEN 1 ELSE 0 END) AS TieneBanci,
            u.activo AS ActivoCuenta, u.fecha_alta AS FechaAlta, u.fecha_modificacion AS FechaModificacion,
            CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM dbo.usuario_modulo otro WHERE otro.id_usuario = u.id_usuario AND otro.id_modulo <> m.id_modulo) THEN 1 ELSE 0 END) AS TieneOtrosModulos
        FROM dbo.usuario u
        INNER JOIN dbo.roles r ON r.id_rol = u.id_rol AND r.activo = 1
        LEFT JOIN dbo.catalogo_entidad_federativa ef ON ef.id_entidad_federativa = u.id_entidad_federativa
        INNER JOIN dbo.catalogo_modulo m ON m.clave = N'BANCI'
        LEFT JOIN dbo.usuario_modulo um ON um.id_usuario = u.id_usuario AND um.id_modulo = m.id_modulo
        WHERE (@IdUsuario IS NULL OR u.id_usuario = @IdUsuario)
          AND (@IncluirInactivos = 1 OR (u.activo = 1 AND (r.rol = N'SUPER_USUARIO' OR COALESCE(um.activo, 1) = 1)))
        ORDER BY u.nombre, u.primer_apellido, u.segundo_apellido, u.id_usuario;
        """;

    public async Task<List<BanciUsuarioDetalle>> ObtenerUsuariosAsync(bool incluirInactivos)
    {
        using var connection = _dbConnectionFactory.CrearConexion();
        return (await connection.QueryAsync<BanciUsuarioDetalle>(SqlUsuarios, new { IdUsuario = (int?)null, IncluirInactivos = incluirInactivos })).ToList();
    }

    public async Task<BanciUsuarioDetalle?> ObtenerDetalleAsync(int idUsuario)
    {
        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QuerySingleOrDefaultAsync<BanciUsuarioDetalle>(SqlUsuarios, new { IdUsuario = (int?)idUsuario, IncluirInactivos = true });
    }

    public async Task<int> GuardarAsync(string operacion, int idUsuario, BanciUsuarioDatos datos, int? idRol, string? passwordHash, int idAdministrador)
    {
        if (operacion is not ("CREAR" or "EDITAR" or "DESACTIVAR" or "REACTIVAR" or "PERMISOS" or "GLOBALES")) throw new InvalidOperationException("La operación de usuario no es válida.");
        const string sql = """
            DECLARE @IdModulo tinyint;
            SELECT @IdModulo = id_modulo FROM dbo.catalogo_modulo WITH (UPDLOCK, HOLDLOCK) WHERE clave = N'BANCI' AND activo = 1;
            IF @IdModulo IS NULL THROW 51000, 'El módulo Banci no está activo.', 1;

            IF NOT EXISTS (
                SELECT 1 FROM dbo.usuario u WITH (UPDLOCK, HOLDLOCK)
                INNER JOIN dbo.roles r ON r.id_rol = u.id_rol AND r.activo = 1 AND r.rol = N'SUPER_USUARIO'
                WHERE u.id_usuario = @IdAdministrador AND u.activo = 1
            ) THROW 51001, 'Se requiere un superusuario con acceso activo al módulo Banci.', 1;

            IF @Operacion = N'GLOBALES'
            BEGIN
                UPDATE um SET habilita_carga = CASE WHEN um.habilitado = 1 AND r.rol <> N'CONSULTA' THEN @HabilitaCarga ELSE 0 END,
                    habilita_modificacion = CASE WHEN um.habilitado = 1 AND r.rol <> N'CONSULTA' THEN @HabilitaModificacion ELSE 0 END,
                    fecha_modificacion = SYSDATETIME(), id_usuario_modificacion = @IdAdministrador
                FROM dbo.usuario_modulo um
                INNER JOIN dbo.usuario u ON u.id_usuario = um.id_usuario
                INNER JOIN dbo.roles r ON r.id_rol = u.id_rol
                WHERE um.id_modulo = @IdModulo AND um.activo = 1 AND u.activo = 1 AND r.activo = 1 AND r.rol <> N'SUPER_USUARIO';

                SELECT COUNT(*) FROM dbo.usuario_modulo um
                INNER JOIN dbo.usuario u ON u.id_usuario = um.id_usuario
                INNER JOIN dbo.roles r ON r.id_rol = u.id_rol
                WHERE um.id_modulo = @IdModulo AND um.activo = 1 AND um.habilitado = 1
                  AND u.activo = 1 AND r.activo = 1 AND r.rol = N'ENLACE_ESTATAL' AND u.id_entidad_federativa BETWEEN 1 AND 32;
                RETURN;
            END;

            DECLARE @RolActual nvarchar(50), @ActivoCuenta bit, @ActivoBanci bit, @AccesoBanci bit, @EntidadActual int;
            IF @Operacion <> N'CREAR'
            BEGIN
                SELECT @RolActual = r.rol, @ActivoCuenta = u.activo, @ActivoBanci = CASE WHEN r.rol = N'SUPER_USUARIO' THEN 1 ELSE um.activo END, @AccesoBanci = um.habilitado, @EntidadActual = u.id_entidad_federativa
                FROM dbo.usuario u WITH (UPDLOCK, HOLDLOCK)
                INNER JOIN dbo.roles r ON r.id_rol = u.id_rol
                LEFT JOIN dbo.usuario_modulo um WITH (UPDLOCK, HOLDLOCK) ON um.id_usuario = u.id_usuario AND um.id_modulo = @IdModulo
                WHERE u.id_usuario = @IdUsuario;
                IF @RolActual IS NULL THROW 51002, 'El usuario no existe.', 1;
                IF @RolActual = N'SUPER_USUARIO' AND (@Operacion IN (N'DESACTIVAR', N'REACTIVAR') OR @HabilitaBanci = 0 OR (@Operacion = N'EDITAR' AND @Rol <> N'SUPER_USUARIO'))
                    THROW 51003, 'Los superusuarios conservan acceso a todos los módulos activos. Su estado y rol general no se cambian desde BANCI.', 1;
                IF @Operacion IN (N'DESACTIVAR', N'REACTIVAR') AND @ActivoBanci IS NULL
                    THROW 51003, 'El usuario aún no pertenece a Banci. Habilite su acceso desde Editar usuario.', 1;
                IF @Operacion IN (N'EDITAR', N'DESACTIVAR', N'PERMISOS') AND (@ActivoCuenta = 0 OR @ActivoBanci = 0)
                    THROW 51003, 'El usuario no está activo en Banci.', 1;
                IF @Operacion = N'REACTIVAR' AND @ActivoCuenta = 1 AND @ActivoBanci = 1
                    THROW 51003, 'El usuario ya está activo en Banci; utilice la edición para cambiar sus permisos.', 1;

                IF @IdUsuario = @IdAdministrador AND (@Operacion = N'DESACTIVAR' OR @HabilitaBanci = 0 OR (@Operacion = N'EDITAR' AND @Rol <> N'SUPER_USUARIO'))
                    THROW 51003, 'No puede deshabilitar su propio acceso Banci ni quitarse el rol de superusuario.', 1;

                IF @RolActual = N'SUPER_USUARIO' AND @ActivoCuenta = 1 AND @ActivoBanci = 1 AND @AccesoBanci = 1
                   AND (@Operacion = N'DESACTIVAR' OR @HabilitaBanci = 0 OR (@Operacion = N'EDITAR' AND @Rol <> N'SUPER_USUARIO'))
                   AND NOT EXISTS (
                       SELECT 1 FROM dbo.usuario u WITH (UPDLOCK, HOLDLOCK)
                       INNER JOIN dbo.roles r ON r.id_rol = u.id_rol AND r.rol = N'SUPER_USUARIO' AND r.activo = 1
                       INNER JOIN dbo.usuario_modulo um WITH (UPDLOCK, HOLDLOCK) ON um.id_usuario = u.id_usuario AND um.id_modulo = @IdModulo AND um.activo = 1 AND um.habilitado = 1
                       WHERE u.activo = 1 AND u.id_usuario <> @IdUsuario
                   ) THROW 51003, 'Debe conservar al menos un superusuario con acceso activo a Banci.', 1;

                IF EXISTS (SELECT 1 FROM dbo.usuario_modulo WHERE id_usuario = @IdUsuario AND id_modulo <> @IdModulo)
                   AND ((@Operacion = N'EDITAR' AND (@Rol <> @RolActual OR ISNULL(@IdEntidadFederativa, 0) <> ISNULL(@EntidadActual, 0))) OR (@Operacion = N'REACTIVAR' AND @ActivoCuenta = 0))
                    THROW 51003, 'La cuenta pertenece a otros módulos. Su rol, entidad y estado general deben administrarse desde la gestión de esa cuenta.', 1;
                IF @Operacion = N'EDITAR' AND ISNULL(@IdEntidadFederativa, 0) <> ISNULL(@EntidadActual, 0)
                   AND EXISTS (SELECT 1 FROM dbo.banci_carga WHERE id_usuario_carga = @IdUsuario)
                    THROW 51003, 'No cambie la entidad de una cuenta con cargas BANCI; conserve su autoría y alcance.', 1;
            END;

            IF @Operacion = N'PERMISOS' AND NOT EXISTS (
                SELECT 1 FROM dbo.usuario u INNER JOIN dbo.roles r ON r.id_rol = u.id_rol
                WHERE u.id_usuario = @IdUsuario AND r.activo = 1
                  AND r.rol IN (N'SUPER_USUARIO', N'ENLACE_ESTATAL', N'CONSULTA')
            ) THROW 51003, 'El rol del usuario no permite configurar permisos Banci.', 1;

            IF @Operacion IN (N'CREAR', N'EDITAR')
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM dbo.roles WHERE id_rol = @IdRol AND rol = @Rol AND activo = 1)
                    THROW 51003, 'El rol no existe o no está activo.', 1;
                IF @Rol NOT IN (N'SUPER_USUARIO', N'ENLACE_ESTATAL', N'CONSULTA')
                    THROW 51003, 'El rol indicado no está permitido.', 1;
                IF @Rol = N'ENLACE_ESTATAL' AND (@IdEntidadFederativa IS NULL OR @IdEntidadFederativa NOT BETWEEN 1 AND 32)
                    THROW 51003, 'El enlace estatal requiere una entidad entre 1 y 32.', 1;
                IF @IdEntidadFederativa IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.catalogo_entidad_federativa WHERE id_entidad_federativa = @IdEntidadFederativa AND activo = 1 AND id_entidad_federativa BETWEEN 1 AND 32)
                    THROW 51003, 'La entidad no existe o no está activa.', 1;
                IF EXISTS (SELECT 1 FROM dbo.usuario WITH (UPDLOCK, HOLDLOCK) WHERE id_usuario <> @IdUsuario AND
                    (usuario = @Usuario OR correo_electronico = @CorreoElectronico OR (@Rfc IS NOT NULL AND rfc = @Rfc) OR (@Curp IS NOT NULL AND curp = @Curp)))
                    THROW 51004, 'Ya existe una cuenta con el usuario, correo, RFC o CURP indicado.', 1;
            END;

            IF @Operacion = N'CREAR'
            BEGIN
                INSERT INTO dbo.usuario (usuario, password, requiere_cambio_password, nombre, primer_apellido, segundo_apellido,
                    correo_electronico, rfc, curp, telefono_contacto, id_entidad_federativa, fecha_alta, fecha_modificacion, id_usuario_alta, id_usuario_modificacion, id_rol, activo)
                VALUES (@Usuario, @PasswordHash, 1, @Nombre, @PrimerApellido, @SegundoApellido,
                    @CorreoElectronico, @Rfc, @Curp, @TelefonoContacto, @IdEntidadFederativa, SYSDATETIME(), SYSDATETIME(), @IdAdministrador, @IdAdministrador, @IdRol, 1);
                SET @IdUsuario = CONVERT(int, SCOPE_IDENTITY());
            END;

            IF @Operacion = N'EDITAR'
                UPDATE dbo.usuario SET usuario = @Usuario, nombre = @Nombre, primer_apellido = @PrimerApellido,
                    segundo_apellido = @SegundoApellido, correo_electronico = @CorreoElectronico, rfc = @Rfc, curp = @Curp,
                    telefono_contacto = @TelefonoContacto, id_rol = @IdRol, id_entidad_federativa = @IdEntidadFederativa,
                    password = COALESCE(@PasswordHash, password),
                    requiere_cambio_password = CASE WHEN @PasswordHash IS NULL THEN requiere_cambio_password ELSE 1 END,
                    fecha_modificacion = SYSDATETIME(), id_usuario_modificacion = @IdAdministrador
                WHERE id_usuario = @IdUsuario AND activo = 1;

            IF @Operacion = N'REACTIVAR' AND @ActivoCuenta = 0
                UPDATE dbo.usuario SET activo = 1, fecha_modificacion = SYSDATETIME(), id_usuario_modificacion = @IdAdministrador WHERE id_usuario = @IdUsuario;

            DECLARE @Acceso bit = CASE WHEN @Operacion = N'DESACTIVAR' THEN 0 ELSE @HabilitaBanci END;
            DECLARE @RolPermisos nvarchar(50) = CASE WHEN @Operacion IN (N'CREAR', N'EDITAR') THEN @Rol ELSE @RolActual END;
            IF @RolPermisos = N'SUPER_USUARIO' BEGIN SET @Acceso = 1; SET @HabilitaCarga = 1; SET @HabilitaModificacion = 1; END;
            IF @Acceso = 1 AND @RolPermisos = N'ENLACE_ESTATAL' AND @Operacion NOT IN (N'CREAR', N'EDITAR') AND (@EntidadActual IS NULL OR @EntidadActual NOT BETWEEN 1 AND 32)
                THROW 51003, 'La cuenta requiere una entidad estatal para habilitar BANCI.', 1;
            DECLARE @Carga bit = CASE WHEN @Acceso = 1 AND @RolPermisos <> N'CONSULTA' THEN @HabilitaCarga ELSE 0 END;
            DECLARE @Modificacion bit = CASE WHEN @Acceso = 1 AND @RolPermisos <> N'CONSULTA' THEN @HabilitaModificacion ELSE 0 END;
            -- La edición también puede asociar una cuenta existente, sin duplicarla.
            IF NOT EXISTS (SELECT 1 FROM dbo.usuario_modulo WITH (UPDLOCK, HOLDLOCK) WHERE id_usuario = @IdUsuario AND id_modulo = @IdModulo)
                INSERT INTO dbo.usuario_modulo (id_usuario, id_modulo, habilitado, habilita_carga, habilita_modificacion, administra_delitos, id_usuario_modificacion, activo)
                VALUES (@IdUsuario, @IdModulo, @Acceso, @Carga, @Modificacion, 0, @IdAdministrador, 1);
            ELSE
                UPDATE dbo.usuario_modulo SET habilitado = @Acceso, habilita_carga = @Carga, habilita_modificacion = @Modificacion,
                    administra_delitos = 0, activo = CASE WHEN @Operacion = N'DESACTIVAR' THEN 0 ELSE 1 END,
                    fecha_modificacion = SYSDATETIME(), id_usuario_modificacion = @IdAdministrador
                WHERE id_usuario = @IdUsuario AND id_modulo = @IdModulo;

            SELECT @IdUsuario;
            """;

        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var result = await connection.ExecuteScalarAsync<int>(sql, new
            {
                Operacion = operacion,
                IdUsuario = idUsuario,
                IdAdministrador = idAdministrador,
                IdRol = idRol,
                PasswordHash = passwordHash,
                datos.Usuario,
                datos.Nombre,
                datos.PrimerApellido,
                datos.SegundoApellido,
                datos.CorreoElectronico,
                datos.Rfc,
                datos.Curp,
                datos.TelefonoContacto,
                datos.IdEntidadFederativa,
                datos.Rol,
                datos.HabilitaBanci,
                datos.HabilitaCarga,
                datos.HabilitaModificacion
            }, transaction);
            if (operacion != "GLOBALES") await UsuarioModuloAcceso.SincronizarSuperUsuarioAsync(connection, transaction, result);
            await transaction.CommitAsync();
            return result;
        }
        catch (SqlException ex) when (ex.Number is >= 51000 and <= 51004 || ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync();
            if (ex.Number == 51001) throw new UnauthorizedAccessException(ex.Message);
            if (ex.Number == 51002) throw new KeyNotFoundException(ex.Message);
            if (ex.Number is 2601 or 2627) throw new InvalidOperationException("Ya existe una cuenta con el usuario, correo, RFC o CURP indicado.");
            throw new InvalidOperationException(ex.Message);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
