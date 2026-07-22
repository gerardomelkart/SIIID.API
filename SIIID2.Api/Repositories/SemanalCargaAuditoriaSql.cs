using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

internal static class SemanalCargaAuditoriaSql
{
    public static async Task GuardarAdvertenciasAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, IEnumerable<CargaValidacionError> advertencias)
    {
        var lista = advertencias?.ToList() ?? [];

        if (lista.Count == 0) return;

        const string sql = """
            INSERT INTO dbo.semanal_carga_advertencia
            (
                id_semanal_carga,
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
                @IdSemanalCarga,
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
            IdSemanalCarga = idSemanalCarga,
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

    public static async Task MarcarAdvertenciasAceptadasAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, int idUsuarioAceptacion)
    {
        const string sql = """
            UPDATE dbo.semanal_carga_advertencia
            SET aceptada_usuario = 1,
                id_usuario_aceptacion = @IdUsuarioAceptacion,
                fecha_aceptacion = SYSDATETIME()
            WHERE id_semanal_carga = @IdSemanalCarga
              AND activo = 1
              AND aceptada_usuario = 0;
            """;

        await connection.ExecuteAsync(sql, new { IdSemanalCarga = idSemanalCarga, IdUsuarioAceptacion = idUsuarioAceptacion }, transaction);
    }

    private static string Limitar(string? valor, int longitudMaxima)
    {
        var texto = valor?.Trim() ?? string.Empty;
        return texto.Length <= longitudMaxima ? texto : texto[..longitudMaxima];
    }

    private static string? LimitarNullable(string? valor, int longitudMaxima)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;

        var texto = valor.Trim();
        return texto.Length <= longitudMaxima ? texto : texto[..longitudMaxima];
    }
}