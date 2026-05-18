using System.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace SIIID2.Api.Data;

public class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public MySqlConnectionFactory(IConfiguration configuration)
    {
        // Lee la cadena de conexión desde appsettings.json.
        _connectionString = configuration.GetConnectionString("SiiidDb")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión SiiidDb.");
    }

    public IDbConnection CrearConexion()
    {
        // Crea una conexión MySQL nueva.
        return new MySqlConnection(_connectionString);
    }
}