using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

internal static class FederalCargaAuditoriaSql
{
    public static async Task GuardarAdvertenciasAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, IEnumerable<CargaValidacionError> advertencias)
    {
        var lista = advertencias?.ToList() ?? [];

        if (lista.Count == 0) return;

        const string sql = """
            INSERT INTO dbo.federal_carga_advertencia
            (
                id_federal_carga,
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
                @IdFederalCarga,
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
            IdFederalCarga = idFederalCarga,
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

    public static async Task RegistrarCambioEstadoAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, string? estadoAnterior, string estadoNuevo, int? idUsuario, string? comentario)
    {
        const string sql = """
            INSERT INTO dbo.federal_carga_bitacora_estado
            (
                id_federal_carga,
                estado_anterior,
                estado_nuevo,
                id_usuario,
                fecha,
                comentario,
                activo
            )
            VALUES
            (
                @IdFederalCarga,
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
            IdFederalCarga = idFederalCarga,
            EstadoAnterior = LimitarNullable(estadoAnterior, 50),
            EstadoNuevo = Limitar(estadoNuevo, 50),
            IdUsuario = idUsuario,
            Comentario = LimitarNullable(comentario, 2000)
        }, transaction);
    }

    public static async Task MarcarAdvertenciasAceptadasAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, int idUsuarioAceptacion)
    {
        const string sql = """
            UPDATE dbo.federal_carga_advertencia
            SET aceptada_usuario = 1,
                id_usuario_aceptacion = @IdUsuarioAceptacion,
                fecha_aceptacion = SYSDATETIME()
            WHERE id_federal_carga = @IdFederalCarga
              AND activo = 1
              AND aceptada_usuario = 0;
            """;

        await connection.ExecuteAsync(sql, new
        {
            IdFederalCarga = idFederalCarga,
            IdUsuarioAceptacion = idUsuarioAceptacion
        }, transaction);
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
