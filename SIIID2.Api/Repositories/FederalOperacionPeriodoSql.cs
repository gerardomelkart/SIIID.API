using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

internal static class FederalOperacionPeriodoSql
{
    internal sealed class ConflictoException(string mensaje) : InvalidOperationException(mensaje) { }

    private sealed class PeriodoInfo
    {
        public int MesCorte { get; set; }
        public int AnioCorte { get; set; }
    }

    public static async Task BloquearAsync(SqlConnection connection, SqlTransaction transaction, int mesCorte, int anioCorte)
    {
        const string sql = """
            DECLARE @Resultado int;
            EXEC @Resultado = sys.sp_getapplock @Resource = @Recurso, @LockMode = N'Exclusive',
                @LockOwner = N'Transaction', @LockTimeout = 10000;
            SELECT @Resultado;
            """;

        var resultado = await connection.ExecuteScalarAsync<int>(sql, new { Recurso = $"SIIID2:FEDERAL:PERIODO:{anioCorte}:{mesCorte}" }, transaction);
        if (resultado < 0) throw new ConflictoException("Hay otra operación federal en curso para este periodo. Espere a que termine e intente nuevamente.");
    }

    public static async Task BloquearPorCodigoAsync(SqlConnection connection, SqlTransaction transaction, string codigoReferencia)
    {
        // Leer el corte antes de tomar bloqueos de escritura sobre la carga. Todos los flujos conservan este orden.
        const string sql = "SELECT mes_corte AS MesCorte, anio_corte AS AnioCorte FROM dbo.federal_carga WHERE codigo_referencia = @CodigoReferencia;";
        var periodo = await connection.QueryFirstOrDefaultAsync<PeriodoInfo>(sql, new { CodigoReferencia = codigoReferencia }, transaction);
        if (periodo != null) await BloquearAsync(connection, transaction, periodo.MesCorte, periodo.AnioCorte);
    }

    public static async Task ExpirarPendientesAsync(SqlConnection connection, SqlTransaction transaction, int mesCorte, int anioCorte)
    {
        // El llamador mantiene el bloqueo del periodo hasta terminar su transacción.
        const string sql = """
            DECLARE @Ahora datetime2 = SYSDATETIME();
            DECLARE @Expiradas TABLE (id_federal_carga bigint PRIMARY KEY, estado_anterior nvarchar(50));

            UPDATE dbo.federal_carga
            SET estado = N'EXPIRADO', mensaje_error = N'La validación federal venció antes de ser confirmada. Debe validar nuevamente los archivos.'
            OUTPUT inserted.id_federal_carga, deleted.estado INTO @Expiradas
            WHERE mes_corte = @MesCorte AND anio_corte = @AnioCorte AND activo = 1
              AND estado IN (N'VALIDADO_PENDIENTE', N'VALIDADO_PENDIENTE_ACTUALIZACION')
              AND fecha_expiracion IS NOT NULL AND fecha_expiracion <= @Ahora;

            UPDATE t SET estado = N'EXPIRADO', fecha_procesamiento = @Ahora
            FROM dbo.federal_carga_tmp_carpeta t INNER JOIN @Expiradas e ON e.id_federal_carga = t.id_federal_carga;
            UPDATE t SET estado = N'EXPIRADO', fecha_procesamiento = @Ahora
            FROM dbo.federal_carga_tmp_delito t INNER JOIN @Expiradas e ON e.id_federal_carga = t.id_federal_carga;
            UPDATE t SET estado = N'EXPIRADO', fecha_procesamiento = @Ahora
            FROM dbo.federal_carga_tmp_victima t INNER JOIN @Expiradas e ON e.id_federal_carga = t.id_federal_carga;

            INSERT INTO dbo.federal_carga_bitacora_estado
                (id_federal_carga, estado_anterior, estado_nuevo, id_usuario, fecha, comentario, activo)
            SELECT id_federal_carga, estado_anterior, N'EXPIRADO', NULL, @Ahora,
                N'Validación federal vencida; se libera el periodo para una nueva operación.', 1 FROM @Expiradas;
            """;

        await connection.ExecuteAsync(sql, new { MesCorte = mesCorte, AnioCorte = anioCorte }, transaction);
    }

    public static async Task<CargaPendienteInfo?> ObtenerPendienteAsync(SqlConnection connection, SqlTransaction transaction, int mesCorte, int anioCorte, string tipoCarga)
    {
        await BloquearAsync(connection, transaction, mesCorte, anioCorte);
        await ExpirarPendientesAsync(connection, transaction, mesCorte, anioCorte);

        const string sql = """
            SELECT TOP (1) codigo_referencia AS CodigoReferencia, estado AS Estado
            FROM dbo.federal_carga
            WHERE mes_corte = @MesCorte AND anio_corte = @AnioCorte AND tipo_carga = @TipoCarga AND activo = 1
              AND estado IN (N'VALIDADO_PENDIENTE', N'VALIDADO_PENDIENTE_ACTUALIZACION', N'PENDIENTE_APROBACION')
            ORDER BY fecha_validacion DESC, id_federal_carga DESC;
            """;

        return await connection.QueryFirstOrDefaultAsync<CargaPendienteInfo>(sql, new { MesCorte = mesCorte, AnioCorte = anioCorte, TipoCarga = tipoCarga }, transaction);
    }

    public static async Task ValidarDisponibilidadAsync(SqlConnection connection, SqlTransaction transaction, int mesCorte, int anioCorte, bool actualizacion, long? idFederalCarga = null)
    {
        const string sql = """
            SELECT CASE
                WHEN @Actualizacion = 0 AND EXISTS (
                    SELECT 1 FROM dbo.federal_carga WHERE mes_corte = @MesCorte AND anio_corte = @AnioCorte
                      AND activo = 1 AND estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                ) THEN N'El periodo federal ya tiene información confirmada. Debe utilizar Actualización.'
                WHEN @Actualizacion = 1 AND NOT EXISTS (
                    SELECT 1 FROM dbo.federal_carga WHERE mes_corte = @MesCorte AND anio_corte = @AnioCorte
                      AND activo = 1 AND estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                ) THEN N'El periodo federal no tiene información confirmada para actualizar.'
                WHEN @Actualizacion = 1 AND @IdFederalCarga IS NOT NULL AND EXISTS (
                    SELECT 1 FROM dbo.federal_carga c INNER JOIN dbo.federal_carga actual ON actual.id_federal_carga = @IdFederalCarga
                    WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.activo = 1
                      AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.id_federal_carga <> @IdFederalCarga
                      AND (c.fecha_confirmacion > actual.fecha_validacion OR c.id_federal_carga > actual.id_federal_carga)
                ) THEN N'La información federal cambió después de esta validación. Rechace esta solicitud y valide nuevamente los archivos.'
                WHEN EXISTS (
                    SELECT 1 FROM dbo.federal_carga WHERE mes_corte = @MesCorte AND anio_corte = @AnioCorte AND activo = 1
                      AND estado IN (N'VALIDADO_PENDIENTE', N'VALIDADO_PENDIENTE_ACTUALIZACION', N'PENDIENTE_APROBACION')
                      AND (estado = N'PENDIENTE_APROBACION' OR fecha_expiracion IS NULL OR fecha_expiracion > SYSDATETIME())
                      AND (@IdFederalCarga IS NULL OR id_federal_carga < @IdFederalCarga)
                ) THEN N'Ya existe otra operación federal pendiente para este periodo. Atienda la solicitud anterior antes de continuar.'
                ELSE NULL END;
            """;

        var mensaje = await connection.ExecuteScalarAsync<string?>(sql, new { MesCorte = mesCorte, AnioCorte = anioCorte, Actualizacion = actualizacion, IdFederalCarga = idFederalCarga }, transaction);
        if (mensaje != null) throw new ConflictoException(mensaje);
    }
}
