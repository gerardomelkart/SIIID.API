using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;

namespace SIIID2.Api.Services;

public class CodigoPostalCatalogoService
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CodigoPostalCatalogoService(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<bool> EsSuperUsuarioAsync(int idUsuario)
    {
        const string sql = @"
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM dbo.usuario u
                INNER JOIN dbo.roles r ON r.id_rol = u.id_rol AND r.activo = 1
                WHERE u.id_usuario = @IdUsuario
                  AND u.activo = 1
                  AND r.rol = N'SUPER_USUARIO'
            ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;";

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.ExecuteScalarAsync<bool>(sql, new { IdUsuario = idUsuario });
    }

    public async Task AsegurarPorCodigoReferenciaAsync(string codigoReferencia)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            await AsegurarAsync(connection, transaction, codigoReferencia);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task AsegurarYRemapearDelitosAsync(string codigoReferencia)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);

        try
        {
            var idCarga = await AsegurarAsync(connection, transaction, codigoReferencia);

            if (idCarga.HasValue)
            {
                await RemapearDelitosAsync(connection, transaction, idCarga.Value);
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<long?> AsegurarAsync(SqlConnection connection, SqlTransaction transaction, string codigoReferencia)
    {
        const string obtenerCargaSql = @"
            SELECT TOP 1 c.id_carga
            FROM dbo.carga c WITH (UPDLOCK, HOLDLOCK)
            WHERE c.codigo_referencia = @CodigoReferencia
              AND c.activo = 1
              AND c.estado IN (
                  N'VALIDADO_PENDIENTE',
                  N'VALIDADO_PENDIENTE_ACTUALIZACION',
                  N'PENDIENTE_APROBACION',
                  N'CONFIRMADO',
                  N'CONFIRMADO_ACTUALIZACION'
              )
            ORDER BY c.id_carga DESC;";

        var idCarga = await connection.QueryFirstOrDefaultAsync<long?>(obtenerCargaSql, new { CodigoReferencia = codigoReferencia }, transaction);

        if (!idCarga.HasValue)
        {
            return null;
        }

        const string reactivarSql = @"
            ;WITH codigos AS (
                SELECT DISTINCT
                    LTRIM(RTRIM(d.cp)) AS codigo_postal,
                    mun.id_municipio
                FROM dbo.carga_tmp_delito d
                INNER JOIN dbo.catalogo_entidad_federativa ef
                    ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos)
                   AND ef.activo = 1
                INNER JOIN dbo.catalogo_municipio mun
                    ON mun.id_entidad_federativa = ef.id_entidad_federativa
                   AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos)
                   AND mun.activo = 1
                WHERE d.id_carga = @IdCarga
                  AND d.activo = 1
                  AND LEN(LTRIM(RTRIM(d.cp))) = 5
                  AND LTRIM(RTRIM(d.cp)) NOT LIKE '%[^0-9]%'
                  AND LTRIM(RTRIM(d.cp)) <> N'00000'
            )
            UPDATE ccp
            SET ccp.activo = 1
            FROM dbo.catalogo_codigo_postal ccp
            INNER JOIN codigos c
                ON c.codigo_postal = ccp.codigo_postal
               AND c.id_municipio = ccp.id_municipio
            WHERE ccp.activo = 0
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.catalogo_codigo_postal activo
                  WHERE activo.codigo_postal = c.codigo_postal
                    AND activo.id_municipio = c.id_municipio
                    AND activo.activo = 1
              );";

        await connection.ExecuteAsync(reactivarSql, new { IdCarga = idCarga.Value }, transaction);

        const string insertarSql = @"
            DECLARE @SiguienteId int;

            SELECT @SiguienteId = CASE
                WHEN MIN(ccp.id_codigo_postal) IS NULL THEN -1
                WHEN MIN(ccp.id_codigo_postal) < 0 THEN MIN(ccp.id_codigo_postal) - 1
                ELSE -1
            END
            FROM dbo.catalogo_codigo_postal ccp WITH (UPDLOCK, HOLDLOCK);

            ;WITH codigos AS (
                SELECT DISTINCT
                    LTRIM(RTRIM(d.cp)) AS codigo_postal,
                    mun.id_municipio
                FROM dbo.carga_tmp_delito d
                INNER JOIN dbo.catalogo_entidad_federativa ef
                    ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos)
                   AND ef.activo = 1
                INNER JOIN dbo.catalogo_municipio mun
                    ON mun.id_entidad_federativa = ef.id_entidad_federativa
                   AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos)
                   AND mun.activo = 1
                WHERE d.id_carga = @IdCarga
                  AND d.activo = 1
                  AND LEN(LTRIM(RTRIM(d.cp))) = 5
                  AND LTRIM(RTRIM(d.cp)) NOT LIKE '%[^0-9]%'
                  AND LTRIM(RTRIM(d.cp)) <> N'00000'
            ),
            faltantes AS (
                SELECT c.codigo_postal, c.id_municipio
                FROM codigos c
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM dbo.catalogo_codigo_postal ccp
                    WHERE ccp.codigo_postal = c.codigo_postal
                      AND ccp.id_municipio = c.id_municipio
                      AND ccp.activo = 1
                )
            ),
            con_plantilla AS (
                SELECT
                    f.codigo_postal,
                    f.id_municipio,
                    plantilla.id_asentamiento,
                    plantilla.id_oficina,
                    plantilla.id_tipo_asentamiento,
                    plantilla.id_tipo_zona,
                    plantilla.id_ciudad
                FROM faltantes f
                CROSS APPLY (
                    SELECT TOP 1
                        ccp.id_asentamiento,
                        ccp.id_oficina,
                        ccp.id_tipo_asentamiento,
                        ccp.id_tipo_zona,
                        ccp.id_ciudad
                    FROM dbo.catalogo_codigo_postal ccp
                    WHERE ccp.id_municipio = f.id_municipio
                    ORDER BY
                        CASE WHEN ccp.activo = 1 THEN 0 ELSE 1 END,
                        CASE WHEN ccp.id_codigo_postal > 0 THEN 0 ELSE 1 END,
                        ccp.id_codigo_postal
                ) plantilla
            ),
            numerados AS (
                SELECT
                    cp.*,
                    ROW_NUMBER() OVER (ORDER BY cp.id_municipio, cp.codigo_postal) AS numero
                FROM con_plantilla cp
            )
            INSERT INTO dbo.catalogo_codigo_postal (
                id_codigo_postal,
                codigo_postal,
                clave_asentamiento_cp_consecutivo,
                id_asentamiento,
                id_oficina,
                id_tipo_asentamiento,
                id_municipio,
                id_tipo_zona,
                id_ciudad,
                activo
            )
            SELECT
                @SiguienteId - CONVERT(int, n.numero) + 1,
                n.codigo_postal,
                NULL,
                n.id_asentamiento,
                n.id_oficina,
                n.id_tipo_asentamiento,
                n.id_municipio,
                n.id_tipo_zona,
                n.id_ciudad,
                1
            FROM numerados n;";

        await connection.ExecuteAsync(insertarSql, new { IdCarga = idCarga.Value }, transaction);
        return idCarga.Value;
    }

    private static async Task RemapearDelitosAsync(SqlConnection connection, SqlTransaction transaction, long idCarga)
    {
        const string sql = @"
            UPDATE de
            SET de.id_codigo_postal = cp.id_codigo_postal
            FROM dbo.delito de
            INNER JOIN dbo.carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = de.id_carpeta_investigacion
               AND ci.activo = 1
            INNER JOIN dbo.carga_tmp_delito d
                ON d.id_carga = @IdCarga
               AND d.id_ci = ci.identificador_carpeta_fiscalia
               AND d.id_delito = de.identificador_delito_fiscalia
               AND d.activo = 1
            INNER JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos)
               AND ef.activo = 1
            INNER JOIN dbo.catalogo_municipio mun
                ON mun.id_entidad_federativa = ef.id_entidad_federativa
               AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos)
               AND mun.activo = 1
            CROSS APPLY (
                SELECT TOP 1 ccp.id_codigo_postal
                FROM dbo.catalogo_codigo_postal ccp
                WHERE ccp.codigo_postal = LTRIM(RTRIM(d.cp))
                  AND ccp.id_municipio = mun.id_municipio
                  AND ccp.activo = 1
                ORDER BY ccp.id_codigo_postal
            ) cp
            WHERE de.id_carga = @IdCarga
              AND de.activo = 1
              AND de.id_codigo_postal IS NULL
              AND LEN(LTRIM(RTRIM(d.cp))) = 5
              AND LTRIM(RTRIM(d.cp)) NOT LIKE '%[^0-9]%'
              AND LTRIM(RTRIM(d.cp)) <> N'00000';";

        await connection.ExecuteAsync(sql, new { IdCarga = idCarga }, transaction);
    }
}
