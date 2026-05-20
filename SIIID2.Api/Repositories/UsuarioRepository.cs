using Dapper;
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
}