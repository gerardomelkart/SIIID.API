using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

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

    public async Task<bool> CoordenadasCorrespondenMunicipioAsync(int idEntidadFederativa, int idMunicipio, decimal coordX, decimal coordY)
    {
        const string sql = @"
        DECLARE @Punto geometry = geometry::Point(@CoordX, @CoordY, 4326);

        SELECT CAST(
            CASE
                WHEN EXISTS
                (
                    SELECT 1
                    FROM dbo.cat_municipios_inegi
                    WHERE cve_ent = @ClaveEntidad
                      AND cve_mun = @ClaveMunicipio
                      AND poligono.STIntersects(@Punto) = 1
                )
                THEN 1
                ELSE 0
            END
        AS bit);
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.ExecuteScalarAsync<bool>(
            sql,
            new
            {
                ClaveEntidad = idEntidadFederativa.ToString("00"),
                ClaveMunicipio = idMunicipio.ToString("000"),
                CoordX = coordX,
                CoordY = coordY
            });
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

    public async Task<List<EntidadFederativaCatalogoItem>> ObtenerEntidadesFederativasActivasAsync()
    {
        // Catálogo de entidades federativas activas.
        // Se usa para combos del front, principalmente para SUPER_USUARIO.
        var sql = @"
        SELECT
            id_entidad_federativa AS IdEntidadFederativa,
            clave AS Clave,
            nombre AS Nombre
        FROM catalogo_entidad_federativa
        WHERE activo = 1
        ORDER BY
            nombre;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var entidades = await connection.QueryAsync<EntidadFederativaCatalogoItem>(sql);

        return entidades.ToList();
    }

    public async Task<List<RolCatalogoItem>> ObtenerRolesActivosAsync()
    {
        // Catálogo de roles activos.
        // Se usa para combos del front en administración de usuarios.
        var sql = @"
        SELECT
            id_rol AS IdRol,
            rol AS Rol
        FROM roles
        WHERE activo = 1
        ORDER BY
            rol;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var roles = await connection.QueryAsync<RolCatalogoItem>(sql);

        return roles.ToList();
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