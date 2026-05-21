using System.Data;
using Microsoft.Data.SqlClient;

namespace SIIID2.Api.Data;

public class SqlServerConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqlServerConnectionFactory(IConfiguration configuration)
    {
        // Lee la cadena de conexión desde appsettings.json.
        _connectionString = configuration.GetConnectionString("SiiidDb") ?? throw new InvalidOperationException("No se encontró la cadena de conexión SiiidDb.");
    }

    public IDbConnection CrearConexion()
    {
        // Crea una conexión nueva a SQL Server.
        return new SqlConnection(_connectionString);
    }
}