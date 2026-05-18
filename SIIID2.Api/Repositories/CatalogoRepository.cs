using Dapper;
using SIIID2.Api.Data;

namespace SIIID2.Api.Repositories;

public class CatalogoRepository : ICatalogoRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CatalogoRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<bool> ExisteClaveNumericaAsync(
        string tabla,
        string columnaClave,
        int clave)
    {
        // Los nombres de tabla y columna no se pueden parametrizar.
        // Por eso estos valores solo deben venir de código interno, nunca del usuario.
        var sql = $@"
            SELECT COUNT(1)
            FROM {tabla}
            WHERE {columnaClave} = @clave
              AND activo = 1;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var total = await connection.ExecuteScalarAsync<int>(sql, new
        {
            clave
        });

        return total > 0;
    }

    public async Task<bool> ExisteClaveTextoAsync(
        string tabla,
        string columnaClave,
        string clave)
    {
        var sql = $@"
            SELECT COUNT(1)
            FROM {tabla}
            WHERE {columnaClave} = @clave
              AND activo = 1;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var total = await connection.ExecuteScalarAsync<int>(sql, new
        {
            clave
        });

        return total > 0;
    }
}