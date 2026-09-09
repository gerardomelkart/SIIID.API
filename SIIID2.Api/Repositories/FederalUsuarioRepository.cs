using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class FederalUsuarioRepository : IFederalUsuarioRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    public FederalUsuarioRepository(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    private const string SqlUsuarios = """
        SELECT u.id_usuario AS IdUsuario, u.usuario AS Usuario, u.nombre AS Nombre,
            u.primer_apellido AS PrimerApellido, u.segundo_apellido AS SegundoApellido,
            u.correo_electronico AS CorreoElectronico, u.rfc AS Rfc, u.curp AS Curp,
            u.telefono_contacto AS TelefonoContacto, u.id_rol AS IdRol, r.rol AS Rol,
            u.id_entidad_federativa AS IdEntidadFederativa, ef.nombre AS EntidadFederativa,
            CONVERT(bit, CASE WHEN um.activo = 1 THEN um.habilitado ELSE 0 END) AS HabilitaFederal,
            CONVERT(bit, CASE WHEN um.activo = 1 AND um.habilitado = 1 AND r.rol <> N'CONSULTA' THEN um.habilita_carga ELSE 0 END) AS HabilitaCarga,
            CONVERT(bit, CASE WHEN um.activo = 1 AND um.habilitado = 1 AND r.rol <> N'CONSULTA' THEN um.habilita_modificacion ELSE 0 END) AS HabilitaModificacion,
            CONVERT(bit, CASE WHEN u.activo = 1 AND COALESCE(um.activo, 1) = 1 THEN 1 ELSE 0 END) AS Activo,
            CONVERT(bit, CASE WHEN um.id_usuario IS NULL THEN 0 ELSE 1 END) AS TieneFederal,
            u.activo AS ActivoCuenta, u.fecha_alta AS FechaAlta, u.fecha_modificacion AS FechaModificacion,
            CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM dbo.usuario_modulo otro WHERE otro.id_usuario = u.id_usuario AND otro.id_modulo <> m.id_modulo) THEN 1 ELSE 0 END) AS TieneOtrosModulos
        FROM dbo.usuario u
        INNER JOIN dbo.roles r ON r.id_rol = u.id_rol
        LEFT JOIN dbo.catalogo_entidad_federativa ef ON ef.id_entidad_federativa = u.id_entidad_federativa
        INNER JOIN dbo.catalogo_modulo m ON m.clave = N'FEDERAL'
        LEFT JOIN dbo.usuario_modulo um ON um.id_usuario = u.id_usuario AND um.id_modulo = m.id_modulo
        WHERE (@IdUsuario IS NULL OR u.id_usuario = @IdUsuario)
          AND (@IncluirInactivos = 1 OR (u.activo = 1 AND COALESCE(um.activo, 1) = 1))
        ORDER BY u.nombre, u.primer_apellido, u.segundo_apellido, u.id_usuario;
        """;

    public async Task<List<FederalUsuarioDetalle>> ObtenerUsuariosAsync(bool incluirInactivos)
    {
        using var connection = _dbConnectionFactory.CrearConexion();
        return (await connection.QueryAsync<FederalUsuarioDetalle>(SqlUsuarios, new { IdUsuario = (int?)null, IncluirInactivos = incluirInactivos })).ToList();
    }

    public async Task<FederalUsuarioDetalle?> ObtenerDetalleAsync(int idUsuario)
    {
        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QuerySingleOrDefaultAsync<FederalUsuarioDetalle>(SqlUsuarios, new { IdUsuario = (int?)idUsuario, IncluirInactivos = true });
    }

    public async Task<int> GuardarAsync(string operacion, int idUsuario, FederalUsuarioDatos datos, int? idRol, string? passwordHash, int idAdministrador)
    {
        if (operacion is not ("CREAR" or "EDITAR" or "DESACTIVAR" or "REACTIVAR" or "PERMISOS" or "GLOBALES")) throw new InvalidOperationException("La operación de usuario no es válida.");
        const string sql = """
            DECLARE @IdModulo tinyint;
            SELECT @IdModulo = id_modulo FROM dbo.catalogo_modulo WITH (UPDLOCK, HOLDLOCK) WHERE clave = N'FEDERAL' AND activo = 1;
            IF @IdModulo IS NULL THROW 51000, 'El módulo Federal no está activo.', 1;

            IF NOT EXISTS (
                SELECT 1 FROM dbo.usuario u WITH (UPDLOCK, HOLDLOCK)
                INNER JOIN dbo.roles r ON r.id_rol = u.id_rol AND r.activo = 1 AND r.rol = N'SUPER_USUARIO'
                INNER JOIN dbo.usuario_modulo um WITH (UPDLOCK, HOLDLOCK) ON um.id_usuario = u.id_usuario AND um.id_modulo = @IdModulo AND um.activo = 1 AND um.habilitado = 1
                WHERE u.id_usuario = @IdAdministrador AND u.activo = 1
            ) THROW 51001, 'Se requiere un superusuario con acceso activo al módulo Federal.', 1;

            IF @Operacion = N'GLOBALES'
            BEGIN
                UPDATE um SET habilita_carga = CASE WHEN um.habilitado = 1 AND r.rol <> N'CONSULTA' THEN @HabilitaCarga ELSE 0 END,
                    habilita_modificacion = CASE WHEN um.habilitado = 1 AND r.rol <> N'CONSULTA' THEN @HabilitaModificacion ELSE 0 END,
                    fecha_modificacion = SYSDATETIME(), id_usuario_modificacion = @IdAdministrador
                FROM dbo.usuario_modulo um
                INNER JOIN dbo.usuario u ON u.id_usuario = um.id_usuario
                INNER JOIN dbo.roles r ON r.id_rol = u.id_rol
                WHERE um.id_modulo = @IdModulo AND um.activo = 1 AND u.activo = 1 AND r.activo = 1 AND u.id_entidad_federativa IS NULL;

                SELECT COUNT(*) FROM dbo.usuario_modulo um
                INNER JOIN dbo.usuario u ON u.id_usuario = um.id_usuario
                INNER JOIN dbo.roles r ON r.id_rol = u.id_rol
                WHERE um.id_modulo = @IdModulo AND um.activo = 1 AND um.habilitado = 1
                  AND u.activo = 1 AND r.activo = 1 AND r.rol <> N'CONSULTA' AND u.id_entidad_federativa IS NULL;
                RETURN;
            END;

            DECLARE @RolActual nvarchar(50), @ActivoCuenta bit, @ActivoFederal bit, @AccesoFederal bit;
            IF @Operacion <> N'CREAR'
            BEGIN
                SELECT @RolActual = r.rol, @ActivoCuenta = u.activo, @ActivoFederal = um.activo, @AccesoFederal = um.habilitado
                FROM dbo.usuario u WITH (UPDLOCK, HOLDLOCK)
                INNER JOIN dbo.roles r ON r.id_rol = u.id_rol
                LEFT JOIN dbo.usuario_modulo um WITH (UPDLOCK, HOLDLOCK) ON um.id_usuario = u.id_usuario AND um.id_modulo = @IdModulo
                WHERE u.id_usuario = @IdUsuario;
                IF @RolActual IS NULL THROW 51002, 'El usuario no existe.', 1;
                IF @Operacion IN (N'DESACTIVAR', N'REACTIVAR') AND @ActivoFederal IS NULL
                    THROW 51003, 'El usuario aún no pertenece a Federal. Habilite su acceso desde Editar usuario.', 1;
                IF @Operacion IN (N'EDITAR', N'DESACTIVAR', N'PERMISOS') AND (@ActivoCuenta = 0 OR @ActivoFederal = 0)
                    THROW 51003, 'El usuario no está activo en Federal.', 1;
                IF @Operacion = N'REACTIVAR' AND @ActivoCuenta = 1 AND @ActivoFederal = 1
                    THROW 51003, 'El usuario ya está activo en Federal; utilice la edición para cambiar sus permisos.', 1;

                IF @IdUsuario = @IdAdministrador AND (@Operacion = N'DESACTIVAR' OR @HabilitaFederal = 0 OR (@Operacion = N'EDITAR' AND @Rol <> N'SUPER_USUARIO'))
                    THROW 51003, 'No puede deshabilitar su propio acceso Federal ni quitarse el rol de superusuario.', 1;

                IF @RolActual = N'SUPER_USUARIO' AND @ActivoCuenta = 1 AND @ActivoFederal = 1 AND @AccesoFederal = 1
                   AND (@Operacion = N'DESACTIVAR' OR @HabilitaFederal = 0 OR (@Operacion = N'EDITAR' AND @Rol <> N'SUPER_USUARIO'))
                   AND NOT EXISTS (
                       SELECT 1 FROM dbo.usuario u WITH (UPDLOCK, HOLDLOCK)
                       INNER JOIN dbo.roles r ON r.id_rol = u.id_rol AND r.rol = N'SUPER_USUARIO' AND r.activo = 1
                       INNER JOIN dbo.usuario_modulo um WITH (UPDLOCK, HOLDLOCK) ON um.id_usuario = u.id_usuario AND um.id_modulo = @IdModulo AND um.activo = 1 AND um.habilitado = 1
                       WHERE u.activo = 1 AND u.id_usuario <> @IdUsuario
                   ) THROW 51003, 'Debe conservar al menos un superusuario con acceso activo a Federal.', 1;

                IF EXISTS (SELECT 1 FROM dbo.usuario_modulo WHERE id_usuario = @IdUsuario AND id_modulo <> @IdModulo)
                   AND ((@Operacion = N'EDITAR' AND @Rol <> @RolActual) OR (@Operacion = N'REACTIVAR' AND @ActivoCuenta = 0))
                    THROW 51003, 'La cuenta pertenece a otros módulos. Su rol y su estado general deben administrarse desde la gestión de esa cuenta.', 1;
            END;

            IF @Operacion = N'PERMISOS' AND NOT EXISTS (
                SELECT 1 FROM dbo.usuario u INNER JOIN dbo.roles r ON r.id_rol = u.id_rol
                WHERE u.id_usuario = @IdUsuario AND r.activo = 1
                  AND r.rol IN (N'SUPER_USUARIO', N'ENLACE_ESTATAL', N'CONSULTA')
            ) THROW 51003, 'El rol del usuario no permite configurar permisos Federal.', 1;

            IF @Operacion IN (N'CREAR', N'EDITAR')
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM dbo.roles WHERE id_rol = @IdRol AND rol = @Rol AND activo = 1)
                    THROW 51003, 'El rol no existe o no está activo.', 1;
                IF @Rol NOT IN (N'SUPER_USUARIO', N'ENLACE_ESTATAL', N'CONSULTA')
                    THROW 51003, 'El rol indicado no está permitido.', 1;
                IF EXISTS (SELECT 1 FROM dbo.usuario WITH (UPDLOCK, HOLDLOCK) WHERE id_usuario <> @IdUsuario AND
                    (usuario = @Usuario OR correo_electronico = @CorreoElectronico OR (@Rfc IS NOT NULL AND rfc = @Rfc) OR (@Curp IS NOT NULL AND curp = @Curp)))
                    THROW 51004, 'Ya existe una cuenta con el usuario, correo, RFC o CURP indicado.', 1;
            END;

            IF @Operacion = N'CREAR'
            BEGIN
                INSERT INTO dbo.usuario (usuario, password, requiere_cambio_password, nombre, primer_apellido, segundo_apellido,
                    correo_electronico, rfc, curp, telefono_contacto, id_entidad_federativa, fecha_alta, fecha_modificacion, id_usuario_alta, id_usuario_modificacion, id_rol, activo)
                VALUES (@Usuario, @PasswordHash, 1, @Nombre, @PrimerApellido, @SegundoApellido,
                    @CorreoElectronico, @Rfc, @Curp, @TelefonoContacto, NULL, SYSDATETIME(), SYSDATETIME(), @IdAdministrador, @IdAdministrador, @IdRol, 1);
                SET @IdUsuario = CONVERT(int, SCOPE_IDENTITY());
            END;

            IF @Operacion = N'EDITAR'
                UPDATE dbo.usuario SET usuario = @Usuario, nombre = @Nombre, primer_apellido = @PrimerApellido,
                    segundo_apellido = @SegundoApellido, correo_electronico = @CorreoElectronico, rfc = @Rfc, curp = @Curp,
                    telefono_contacto = @TelefonoContacto, id_rol = @IdRol,
                    password = COALESCE(@PasswordHash, password),
                    requiere_cambio_password = CASE WHEN @PasswordHash IS NULL THEN requiere_cambio_password ELSE 1 END,
                    fecha_modificacion = SYSDATETIME(), id_usuario_modificacion = @IdAdministrador
                WHERE id_usuario = @IdUsuario AND activo = 1;

            IF @Operacion = N'REACTIVAR' AND @ActivoCuenta = 0
                UPDATE dbo.usuario SET activo = 1, fecha_modificacion = SYSDATETIME(), id_usuario_modificacion = @IdAdministrador WHERE id_usuario = @IdUsuario;

            DECLARE @Acceso bit = CASE WHEN @Operacion = N'DESACTIVAR' THEN 0 ELSE @HabilitaFederal END;
            DECLARE @RolPermisos nvarchar(50) = CASE WHEN @Operacion IN (N'CREAR', N'EDITAR') THEN @Rol ELSE @RolActual END;
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
                datos.Rol,
                datos.HabilitaFederal,
                datos.HabilitaCarga,
                datos.HabilitaModificacion
            }, transaction);
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
