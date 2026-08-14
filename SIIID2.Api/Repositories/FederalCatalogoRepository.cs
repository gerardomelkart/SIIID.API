using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class FederalCatalogoRepository : IFederalCatalogoRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public FederalCatalogoRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<FederalCatalogoResumen> ObtenerResumenAsync()
    {
        const string sql = @"
            SELECT
                (SELECT COUNT(*) FROM dbo.federal_catalogo_bien_juridico WHERE activo = 1) AS BienesJuridicos,
                (SELECT COUNT(*) FROM dbo.federal_catalogo_delito WHERE activo = 1) AS Delitos,
                (SELECT COUNT(*) FROM dbo.federal_catalogo_subtipo_delito WHERE activo = 1) AS Subtipos,
                (SELECT COUNT(*) FROM dbo.federal_catalogo_modalidad_delito WHERE activo = 1) AS Modalidades,
                (SELECT COUNT(*) FROM dbo.federal_catalogo_modalidad_delito WHERE activo = 1 AND es_fuero_federal = 0) AS ModalidadesComunes,
                (SELECT COUNT(*) FROM dbo.federal_catalogo_modalidad_delito WHERE activo = 1 AND es_fuero_federal = 1) AS ModalidadesFederales,
                (SELECT COUNT(*) FROM dbo.federal_catalogo_delito_sabana WHERE activo = 1) AS CombinacionesSabana,
                (SELECT COUNT(*) FROM dbo.federal_catalogo_delito_sabana WHERE activo = 1 AND es_fuero_federal = 0) AS CombinacionesComunes,
                (SELECT COUNT(*) FROM dbo.federal_catalogo_delito_sabana WHERE activo = 1 AND es_fuero_federal = 1) AS CombinacionesFederales;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QuerySingleAsync<FederalCatalogoResumen>(sql);
    }

    public async Task<List<FederalBienJuridicoCatalogoItem>> ObtenerBienesJuridicosAsync()
    {
        const string sql = @"
            SELECT
                id_bien_juridico AS IdBienJuridico,
                clave1 AS Clave,
                bien_juridico AS BienJuridico
            FROM dbo.federal_catalogo_bien_juridico
            WHERE activo = 1
            ORDER BY id_bien_juridico;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return (await connection.QueryAsync<FederalBienJuridicoCatalogoItem>(sql)).ToList();
    }

    public async Task<List<FederalDelitoCatalogoItem>> ObtenerDelitosAsync(int? idBienJuridico)
    {
        const string sql = @"
            SELECT
                id_delito AS IdDelito,
                id_bien_juridico AS IdBienJuridico,
                clave2 AS Clave,
                delito AS Delito
            FROM dbo.federal_catalogo_delito
            WHERE activo = 1
              AND (@IdBienJuridico IS NULL OR id_bien_juridico = @IdBienJuridico)
            ORDER BY id_bien_juridico, id_delito;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return (await connection.QueryAsync<FederalDelitoCatalogoItem>(sql, new
        {
            IdBienJuridico = idBienJuridico
        })).ToList();
    }

    public async Task<List<FederalSubtipoDelitoCatalogoItem>> ObtenerSubtiposAsync(int? idDelito)
    {
        const string sql = @"
            SELECT
                id_subtipo_delito AS IdSubtipoDelito,
                id_delito AS IdDelito,
                clave3 AS Clave,
                subtipo_delito AS SubtipoDelito
            FROM dbo.federal_catalogo_subtipo_delito
            WHERE activo = 1
              AND (@IdDelito IS NULL OR id_delito = @IdDelito)
            ORDER BY id_delito, id_subtipo_delito;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return (await connection.QueryAsync<FederalSubtipoDelitoCatalogoItem>(sql, new
        {
            IdDelito = idDelito
        })).ToList();
    }

    public async Task<List<FederalModalidadDelitoCatalogoItem>> ObtenerModalidadesAsync(int? idSubtipoDelito)
    {
        const string sql = @"
            SELECT
                id_modalidad_delito AS IdModalidadDelito,
                id_subtipo_delito AS IdSubtipoDelito,
                clave4 AS Clave,
                modalidad_delito AS ModalidadDelito,
                id_admite_tentativa AS IdAdmiteTentativa,
                es_fuero_federal AS EsFueroFederal
            FROM dbo.federal_catalogo_modalidad_delito
            WHERE activo = 1
              AND (@IdSubtipoDelito IS NULL OR id_subtipo_delito = @IdSubtipoDelito)
            ORDER BY id_subtipo_delito, id_modalidad_delito;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return (await connection.QueryAsync<FederalModalidadDelitoCatalogoItem>(sql, new
        {
            IdSubtipoDelito = idSubtipoDelito
        })).ToList();
    }
}