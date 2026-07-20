using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class SemanalDelitoRepository : ISemanalDelitoRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SemanalDelitoRepository(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    public async Task<bool> PuedeAdministrarDelitosAsync(int idUsuario)
    {
        const string sql = @"
        SELECT COUNT(1)
        FROM dbo.usuario u
        INNER JOIN dbo.roles r ON r.id_rol = u.id_rol AND r.activo = 1
        INNER JOIN dbo.usuario_modulo um ON um.id_usuario = u.id_usuario AND um.habilitado = 1 AND um.administra_delitos = 1 AND um.activo = 1
        INNER JOIN dbo.catalogo_modulo m ON m.id_modulo = um.id_modulo AND m.clave = N'SEMANAL' AND m.activo = 1
        WHERE u.id_usuario = @IdUsuario
          AND u.activo = 1
          AND r.rol = N'SUPER_USUARIO';
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.ExecuteScalarAsync<int>(sql, new { IdUsuario = idUsuario }) > 0;
    }

    public async Task<List<ConfiguracionDelitoSemanalItem>> ObtenerConfiguracionAsync()
    {
        const string sql = @"
        SELECT
            cd.id_delito AS IdDelito,
            cd.clave2 AS Clave,
            cd.delito AS Delito,
            bj.bien_juridico AS BienJuridico,
            CONVERT(bit, CASE WHEN configuracion.activo = 1 THEN 1 ELSE 0 END) AS Seleccionado,
            CONVERT(bit, ISNULL(configuracion.es_obligatorio, 0)) AS EsObligatorio,
            CONVERT(bit, ISNULL(configuracion.conservar_entre_periodos, 0)) AS ConservarEntrePeriodos,
            CONVERT(smallint, ISNULL(configuracion.orden, 0)) AS Orden
        FROM dbo.catalogo_delito cd
        INNER JOIN dbo.catalogo_bien_juridico bj ON bj.id_bien_juridico = cd.id_bien_juridico AND bj.activo = 1
        LEFT JOIN dbo.semanal_configuracion_delito configuracion ON configuracion.id_delito = cd.id_delito
        WHERE cd.activo = 1
        ORDER BY
            CASE WHEN configuracion.activo = 1 THEN 0 ELSE 1 END,
            CASE WHEN configuracion.activo = 1 THEN configuracion.orden ELSE 32767 END,
            cd.clave2;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return (await connection.QueryAsync<ConfiguracionDelitoSemanalItem>(sql)).ToList();
    }

    public async Task GuardarConfiguracionAsync(List<ConfiguracionDelitoSemanalItem> delitos, int idUsuarioModificacion)
    {
        const string sqlDesactivar = @"
        UPDATE dbo.semanal_configuracion_delito
        SET activo = 0,
            fecha_modificacion = SYSDATETIME(),
            id_usuario_modificacion = @IdUsuarioModificacion
        WHERE es_obligatorio = 0;
    ";

        const string sqlGuardar = @"
        IF EXISTS (SELECT 1 FROM dbo.semanal_configuracion_delito WHERE id_delito = @IdDelito)
        BEGIN
            UPDATE dbo.semanal_configuracion_delito
            SET es_obligatorio = @EsObligatorio,
                conservar_entre_periodos = @ConservarEntrePeriodos,
                orden = @Orden,
                fecha_modificacion = SYSDATETIME(),
                id_usuario_modificacion = @IdUsuarioModificacion,
                activo = 1
            WHERE id_delito = @IdDelito;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.semanal_configuracion_delito (id_delito, es_obligatorio, conservar_entre_periodos, orden, id_usuario_modificacion, activo)
            VALUES (@IdDelito, @EsObligatorio, @ConservarEntrePeriodos, @Orden, @IdUsuarioModificacion, 1);
        END;
    ";

        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();

        await connection.OpenAsync();

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            await connection.ExecuteAsync(sqlDesactivar, new { IdUsuarioModificacion = idUsuarioModificacion }, transaction);
            await connection.ExecuteAsync(sqlGuardar, delitos.Select(x => new { x.IdDelito, x.EsObligatorio, x.ConservarEntrePeriodos, x.Orden, IdUsuarioModificacion = idUsuarioModificacion }), transaction);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}