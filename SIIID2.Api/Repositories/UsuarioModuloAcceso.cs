using Dapper;
using Microsoft.Data.SqlClient;

namespace SIIID2.Api.Repositories;

public static class UsuarioModuloAcceso
{
    // Se invoca dentro de la transacción de alta/edición, después de los permisos solicitados.
    // No depende de ninguna tabla BANCI; un superusuario sólo entra a módulos activos.
    public static Task SincronizarSuperUsuarioAsync(SqlConnection connection, SqlTransaction transaction, int idUsuario)
    {
        const string sql = """
            IF EXISTS (SELECT 1 FROM dbo.usuario u INNER JOIN dbo.roles r ON r.id_rol = u.id_rol AND r.activo = 1 WHERE u.id_usuario = @IdUsuario AND u.activo = 1 AND r.rol = N'SUPER_USUARIO')
            BEGIN
                UPDATE um SET habilitado = 1, habilita_carga = 1, habilita_modificacion = 1, administra_delitos = 1, activo = 1, fecha_modificacion = SYSDATETIME()
                FROM dbo.usuario_modulo um INNER JOIN dbo.catalogo_modulo m ON m.id_modulo = um.id_modulo AND m.activo = 1 WHERE um.id_usuario = @IdUsuario;
                INSERT INTO dbo.usuario_modulo (id_usuario, id_modulo, habilitado, habilita_carga, habilita_modificacion, administra_delitos, activo)
                SELECT @IdUsuario, m.id_modulo, 1, 1, 1, 1, 1 FROM dbo.catalogo_modulo m
                WHERE m.activo = 1 AND NOT EXISTS (SELECT 1 FROM dbo.usuario_modulo um WITH (UPDLOCK, HOLDLOCK) WHERE um.id_usuario = @IdUsuario AND um.id_modulo = m.id_modulo);
                IF EXISTS (SELECT 1 FROM dbo.habilita_carga_modificacion WITH (UPDLOCK, HOLDLOCK) WHERE id_usuario = @IdUsuario)
                    UPDATE dbo.habilita_carga_modificacion SET habilita_carga = 1, habilita_modificacion = 1, activo = 1 WHERE id_usuario = @IdUsuario;
                ELSE INSERT INTO dbo.habilita_carga_modificacion (id_usuario, habilita_carga, habilita_modificacion, activo) VALUES (@IdUsuario, 1, 1, 1);
            END;
            """;
        return connection.ExecuteAsync(sql, new { IdUsuario = idUsuario }, transaction);
    }
}
