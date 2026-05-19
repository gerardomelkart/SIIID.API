using Dapper;
using SIIID2.Api.Data;

namespace SIIID2.Api.Repositories;

public class CatalogoRepository : ICatalogoRepository
{
    private class MunicipioCatalogo
    {
        public int IdEntidadFederativa { get; set; }
        public string ClaveMunicipio { get; set; } = string.Empty;
    }

    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CatalogoRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }
    public async Task<HashSet<string>> ObtenerMunicipiosPorEntidadActivosAsync()
    {
        // El municipio se valida por combinación:
        // id_entidad_federativa + clave del municipio.
        var sql = @"
        SELECT
            id_entidad_federativa AS IdEntidadFederativa,
            clave AS ClaveMunicipio
        FROM catalogo_municipio
        WHERE activo = 1;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var municipios = await connection.QueryAsync<MunicipioCatalogo>(sql);

        return municipios
            .Where(x => !string.IsNullOrWhiteSpace(x.ClaveMunicipio))
            .Select(x => $"{x.IdEntidadFederativa}|{NormalizarClaveMunicipio(x.ClaveMunicipio)}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> ExisteClaveNumericaAsync(string tabla, string columnaClave, int clave)
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

    public async Task<bool> ExisteClaveTextoAsync(string tabla, string columnaClave, string clave)
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

    public async Task<HashSet<int>> ObtenerClavesNumericasActivasAsync(string tabla, string columnaClave)
    {
        // Carga todas las claves activas del catálogo una sola vez.
        var sql = $@"
            SELECT {columnaClave}
            FROM {tabla}
            WHERE activo = 1;
        ";
        using var connection = _dbConnectionFactory.CrearConexion();
        var claves = await connection.QueryAsync<int>(sql);
        return claves.ToHashSet();
    }

    public async Task<HashSet<string>> ObtenerClavesTextoActivasAsync(string tabla, string columnaClave)
    {
        // Carga todas las claves activas del catálogo una sola vez.
        var sql = $@"
            SELECT {columnaClave}
            FROM {tabla}
            WHERE activo = 1;
        ";
        using var connection = _dbConnectionFactory.CrearConexion();
        var claves = await connection.QueryAsync<string>(sql);
        return claves
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    private static string NormalizarClaveMunicipio(string clave)
    {
        clave = clave.Trim();

        if (int.TryParse(clave, out var numero))
        {
            return numero.ToString("000");
        }

        return clave;
    }
}