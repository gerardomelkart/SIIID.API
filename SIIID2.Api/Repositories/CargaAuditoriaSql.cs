using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

internal static class CargaAuditoriaSql
{
    public static async Task GuardarAdvertenciasAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, IEnumerable<CargaValidacionError> advertencias)
    {
        var lista = advertencias?.ToList() ?? [];

        if (lista.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO dbo.carga_advertencia
            (
                id_carga,
                codigo,
                archivo,
                numero_fila,
                columna,
                campo,
                valor,
                descripcion_resumen,
                mensaje,
                aceptada_usuario,
                id_usuario_aceptacion,
                fecha_aceptacion,
                activo
            )
            VALUES
            (
                @IdCarga,
                @Codigo,
                @Archivo,
                @NumeroFila,
                @Columna,
                @Campo,
                @Valor,
                @DescripcionResumen,
                @Mensaje,
                0,
                NULL,
                NULL,
                1
            );
            """;

        var parametros = lista.Select(advertencia => new
        {
            IdCarga = idCarga,
            Codigo = Limitar(advertencia.Codigo, 150),
            Archivo = Limitar(advertencia.Archivo, 50),
            NumeroFila = advertencia.Fila,
            Columna = LimitarNullable(advertencia.Columna, 150),
            Campo = LimitarNullable(advertencia.Campo, 150),
            Valor = LimitarNullable(advertencia.Valor, 1000),
            DescripcionResumen = Limitar(advertencia.DescripcionResumen, 500),
            Mensaje = Limitar(advertencia.Mensaje, 2000)
        });

        await connection.ExecuteAsync(sql, parametros, transaction);
    }

    public static async Task RegistrarCambioEstadoAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, string? estadoAnterior, string estadoNuevo, int? idUsuario, string? comentario)
    {
        if (string.Equals(estadoNuevo, "CONFIRMADO", StringComparison.OrdinalIgnoreCase))
        {
            await GuardarCodigoPostalCargaAsync(connection, transaction, idCarga);
        }
        else if (string.Equals(estadoNuevo, "CONFIRMADO_ACTUALIZACION", StringComparison.OrdinalIgnoreCase))
        {
            await GuardarCodigoPostalActualizacionAsync(connection, transaction, idCarga, idUsuario);
        }

        const string sql = """
            INSERT INTO dbo.carga_bitacora_estado
            (
                id_carga,
                estado_anterior,
                estado_nuevo,
                id_usuario,
                fecha,
                comentario,
                activo
            )
            VALUES
            (
                @IdCarga,
                @EstadoAnterior,
                @EstadoNuevo,
                @IdUsuario,
                SYSDATETIME(),
                @Comentario,
                1
            );
            """;

        await connection.ExecuteAsync(sql, new
        {
            IdCarga = idCarga,
            EstadoAnterior = LimitarNullable(estadoAnterior, 50),
            EstadoNuevo = Limitar(estadoNuevo, 50),
            IdUsuario = idUsuario,
            Comentario = LimitarNullable(comentario, 2000)
        }, transaction);
    }

    private static async Task GuardarCodigoPostalCargaAsync(SqlConnection connection, SqlTransaction transaction, long idCarga)
    {
        const string sql = """
            UPDATE de
            SET de.codigo_postal_fiscalia = NULLIF(LTRIM(RTRIM(tmp.cp)), N'')
            FROM dbo.delito de
            INNER JOIN dbo.carpeta_investigacion ci
                ON ci.id_carpeta_investigacion = de.id_carpeta_investigacion
            INNER JOIN dbo.carga_tmp_delito tmp
                ON tmp.id_carga = @IdCarga
               AND tmp.id_ci = ci.identificador_carpeta_fiscalia
               AND tmp.id_delito = de.identificador_delito_fiscalia
               AND tmp.activo = 1
            WHERE de.id_carga = @IdCarga;
            """;

        await connection.ExecuteAsync(sql, new { IdCarga = idCarga }, transaction);
    }

    private static async Task GuardarCodigoPostalActualizacionAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, int? idUsuario)
    {
        const string sql = """
            ;WITH carga_actualizacion AS (
                SELECT id_entidad_federativa, mes_corte, anio_corte
                FROM dbo.carga
                WHERE id_carga = @IdCarga
            ),
            cargas_periodo AS (
                SELECT c.id_carga, c.fecha_confirmacion
                FROM dbo.carga c
                INNER JOIN carga_actualizacion ca
                    ON ca.id_entidad_federativa = c.id_entidad_federativa
                   AND ca.mes_corte = c.mes_corte
                   AND ca.anio_corte = c.anio_corte
                WHERE c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                  AND c.activo = 1
            ),
            candidatos AS (
                SELECT
                    de.id_delito,
                    NULLIF(LTRIM(RTRIM(tmp.cp)), N'') AS codigo_postal_nuevo,
                    ROW_NUMBER() OVER (
                        PARTITION BY ci.identificador_carpeta_fiscalia, de.identificador_delito_fiscalia
                        ORDER BY ISNULL(cp.fecha_confirmacion, '19000101') DESC, de.id_carga DESC, de.id_delito DESC
                    ) AS rn
                FROM dbo.delito de
                INNER JOIN dbo.carpeta_investigacion ci
                    ON ci.id_carpeta_investigacion = de.id_carpeta_investigacion
                INNER JOIN cargas_periodo cp
                    ON cp.id_carga = de.id_carga
                INNER JOIN dbo.carga_tmp_delito tmp
                    ON tmp.id_carga = @IdCarga
                   AND tmp.id_ci = ci.identificador_carpeta_fiscalia
                   AND tmp.id_delito = de.identificador_delito_fiscalia
                   AND tmp.activo = 1
                WHERE de.activo = 1
            )
            SELECT id_delito, codigo_postal_nuevo
            INTO #cp_actuales
            FROM candidatos
            WHERE rn = 1;

            INSERT INTO dbo.delito_historico (
                id_delito,
                id_carpeta_investigacion,
                identificador_delito_fiscalia,
                delito_fiscalia,
                modalidad_delito_fiscalia,
                id_forma_accion,
                fecha_hechos,
                id_instrumento_comision,
                id_grado_consumacion,
                id_modalidad_delito,
                id_entidad_federativa,
                id_municipio,
                id_localidad_fiscalia,
                localidad_fiscalia_nombre,
                id_colonia_fiscalia,
                colonia_fiscalia_nombre,
                id_codigo_postal,
                codigo_postal_fiscalia,
                coordenada_x,
                coordenada_y,
                domicilio_hechos,
                id_usuario_registro,
                fecha_registro,
                id_carga,
                id_usuario_modificacion,
                id_carga_nueva,
                tipo_movimiento,
                fecha_modificacion,
                activo
            )
            SELECT
                de.id_delito,
                de.id_carpeta_investigacion,
                de.identificador_delito_fiscalia,
                de.delito_fiscalia,
                de.modalidad_delito_fiscalia,
                de.id_forma_accion,
                de.fecha_hechos,
                de.id_instrumento_comision,
                de.id_grado_consumacion,
                de.id_modalidad_delito,
                de.id_entidad_federativa,
                de.id_municipio,
                de.id_localidad_fiscalia,
                de.localidad_fiscalia_nombre,
                de.id_colonia_fiscalia,
                de.colonia_fiscalia_nombre,
                de.id_codigo_postal,
                de.codigo_postal_fiscalia,
                de.coordenada_x,
                de.coordenada_y,
                de.domicilio_hechos,
                de.id_usuario_registro,
                de.fecha_registro,
                de.id_carga,
                @IdUsuario,
                @IdCarga,
                N'MODIFICADO',
                SYSDATETIME(),
                de.activo
            FROM dbo.delito de
            INNER JOIN #cp_actuales cp
                ON cp.id_delito = de.id_delito
            WHERE de.id_carga <> @IdCarga
              AND ISNULL(de.codigo_postal_fiscalia, N'') <> ISNULL(cp.codigo_postal_nuevo, N'')
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.delito_historico dh
                  WHERE dh.id_delito = de.id_delito
                    AND dh.id_carga_nueva = @IdCarga
              );

            UPDATE dh
            SET dh.codigo_postal_fiscalia = de.codigo_postal_fiscalia
            FROM dbo.delito_historico dh
            INNER JOIN dbo.delito de
                ON de.id_delito = dh.id_delito
            WHERE dh.id_carga_nueva = @IdCarga;

            UPDATE de
            SET de.codigo_postal_fiscalia = cp.codigo_postal_nuevo,
                de.id_carga = @IdCarga
            FROM dbo.delito de
            INNER JOIN #cp_actuales cp
                ON cp.id_delito = de.id_delito
            WHERE ISNULL(de.codigo_postal_fiscalia, N'') <> ISNULL(cp.codigo_postal_nuevo, N'');

            DROP TABLE #cp_actuales;
            """;

        await connection.ExecuteAsync(sql, new { IdCarga = idCarga, IdUsuario = idUsuario }, transaction);
    }

    private static string Limitar(string? valor, int longitudMaxima)
    {
        var texto = valor?.Trim() ?? string.Empty;
        return texto.Length <= longitudMaxima ? texto : texto[..longitudMaxima];
    }

    private static string? LimitarNullable(string? valor, int longitudMaxima)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var texto = valor.Trim();
        return texto.Length <= longitudMaxima ? texto : texto[..longitudMaxima];
    }

    public static async Task MarcarAdvertenciasAceptadasAsync(SqlConnection connection, SqlTransaction transaction, long idCarga, int idUsuarioAceptacion)
    {
        const string sql = """
        UPDATE dbo.carga_advertencia
        SET aceptada_usuario = 1,
            id_usuario_aceptacion = @IdUsuarioAceptacion,
            fecha_aceptacion = SYSDATETIME()
        WHERE id_carga = @IdCarga
          AND activo = 1
          AND aceptada_usuario = 0;
        """;

        await connection.ExecuteAsync(sql, new { IdCarga = idCarga, IdUsuarioAceptacion = idUsuarioAceptacion }, transaction);
    }
}
