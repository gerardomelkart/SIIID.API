using System.Data;

namespace SIIID2.Api.Data;

public interface IDbConnectionFactory
{
    // Crea una conexión nueva a la base de datos.
    IDbConnection CrearConexion();
}