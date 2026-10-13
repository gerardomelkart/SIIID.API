using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using System.Globalization;

namespace SIIID2.Api.Repositories;

// Seguimiento administrativo de remisiones semanales.
public partial class SemanalEnviosRepository : ISemanalEnviosRepository
{
    public async Task<List<SemanalReporteCargaItem>> ObtenerReporteCargasAsync(int? idEntidadFederativa, int? idUsuarioCarga, int? anioCorte, int? mesCorte)
    {
        const string sql = @"
            SET NOCOUNT ON;

            SELECT
                sc.id_semanal_carga,
                sc.id_entidad_federativa,
                sc.id_usuario_carga,
                sc.id_delito,
                bloque.fecha_inicio_semana,
                bloque.fecha_fin_semana,
                bloque.anio_corte,
                bloque.mes_corte,
                sc.codigo_referencia,
                sc.tipo_carga,
                sc.estado,
                sc.fecha_carga,
                sc.fecha_validacion,
                sc.fecha_confirmacion,
                COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) AS fecha_orden
            INTO #BloquesCarga
            FROM dbo.semanal_carga sc
            INNER JOIN dbo.semanal_carga_bloque bloque
                ON bloque.id_semanal_carga = sc.id_semanal_carga
               AND bloque.activo = 1
            WHERE sc.activo = 1
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.id_entidad_federativa IS NOT NULL
              AND sc.id_delito IS NOT NULL
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
              AND (@AnioCorte IS NULL OR bloque.anio_corte = @AnioCorte)
              AND (@MesCorte IS NULL OR bloque.mes_corte = @MesCorte)

            UNION ALL

            SELECT
                sc.id_semanal_carga,
                sc.id_entidad_federativa,
                sc.id_usuario_carga,
                sc.id_delito,
                sc.fecha_inicio_semana,
                sc.fecha_fin_semana,
                sc.anio_corte,
                sc.mes_corte,
                sc.codigo_referencia,
                sc.tipo_carga,
                sc.estado,
                sc.fecha_carga,
                sc.fecha_validacion,
                sc.fecha_confirmacion,
                COALESCE(sc.fecha_confirmacion, sc.fecha_validacion, sc.fecha_carga) AS fecha_orden
            FROM dbo.semanal_carga sc
            WHERE sc.activo = 1
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.id_entidad_federativa IS NOT NULL
              AND sc.id_delito IS NOT NULL
              AND (@IdEntidadFederativa IS NULL OR sc.id_entidad_federativa = @IdEntidadFederativa)
              AND (@IdUsuarioCarga IS NULL OR sc.id_usuario_carga = @IdUsuarioCarga)
              AND (@AnioCorte IS NULL OR sc.anio_corte = @AnioCorte)
              AND (@MesCorte IS NULL OR sc.mes_corte = @MesCorte)
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.semanal_carga_bloque bloque
                  WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                    AND bloque.activo = 1
              )
            OPTION (RECOMPILE);

            CREATE CLUSTERED INDEX IX_BloquesCargaAgrupacion
            ON #BloquesCarga
            (
                id_entidad_federativa,
                id_usuario_carga,
                id_delito,
                fecha_inicio_semana,
                fecha_fin_semana,
                anio_corte,
                mes_corte,
                fecha_orden DESC,
                id_semanal_carga DESC
            );

            SELECT
                bloque.id_entidad_federativa,
                bloque.id_usuario_carga,
                bloque.id_delito,
                bloque.fecha_inicio_semana,
                bloque.fecha_fin_semana,
                bloque.anio_corte,
                bloque.mes_corte,
                COUNT(DISTINCT bloque.id_semanal_carga) AS intentos,
                MIN
                (
                    CASE
                        WHEN bloque.estado IN
                        (
                            N'VALIDADO_PENDIENTE',
                            N'VALIDADO_PENDIENTE_ACTUALIZACION',
                            N'PENDIENTE_APROBACION',
                            N'CONFIRMADO',
                            N'CONFIRMADO_ACTUALIZACION'
                        )
                        THEN bloque.fecha_validacion
                        ELSE NULL
                    END
                ) AS fecha_carga_exitosa
            INTO #Periodos
            FROM #BloquesCarga bloque
            GROUP BY
                bloque.id_entidad_federativa,
                bloque.id_usuario_carga,
                bloque.id_delito,
                bloque.fecha_inicio_semana,
                bloque.fecha_fin_semana,
                bloque.anio_corte,
                bloque.mes_corte;

            CREATE UNIQUE CLUSTERED INDEX IX_PeriodosAgrupacion
            ON #Periodos
            (
                id_entidad_federativa,
                id_usuario_carga,
                id_delito,
                fecha_inicio_semana,
                fecha_fin_semana,
                anio_corte,
                mes_corte
            );

            SELECT
                periodo.id_entidad_federativa AS IdEntidadFederativa,
                entidad.nombre AS EntidadFederativa,
                entidad.clave AS ClaveEntidad,
                periodo.id_delito AS IdDelito,
                catalogo_delito.delito AS Delito,
                periodo.id_usuario_carga AS IdUsuarioCarga,
                usuario.usuario AS UsuarioCarga,
                COALESCE
                (
                    NULLIF
                    (
                        LTRIM(RTRIM(CONCAT
                        (
                            usuario.nombre,
                            N' ',
                            usuario.primer_apellido,
                            CASE
                                WHEN NULLIF(usuario.segundo_apellido, N'') IS NULL THEN N''
                                ELSE CONCAT(N' ', usuario.segundo_apellido)
                            END
                        ))),
                        N''
                    ),
                    usuario.usuario
                ) AS NombreUsuarioCarga,
                periodo.fecha_inicio_semana AS FechaInicioSemana,
                periodo.fecha_fin_semana AS FechaFinSemana,
                periodo.anio_corte AS AnioCorte,
                periodo.mes_corte AS MesCorte,
                periodo.intentos AS Intentos,
                ultimo.codigo_referencia AS UltimoIntento,
                ultimo.tipo_carga AS TipoCargaUltimoIntento,
                ultimo.estado AS EstatusUltimoIntento,
                CASE
                    WHEN ultimo.estado IN
                    (
                        N'VALIDADO_PENDIENTE',
                        N'VALIDADO_PENDIENTE_ACTUALIZACION',
                        N'PENDIENTE_APROBACION',
                        N'CONFIRMADO',
                        N'CONFIRMADO_ACTUALIZACION',
                        N'RECHAZADO_ADMIN'
                    )
                    THEN ultimo.fecha_validacion
                    ELSE NULL
                END AS FechaCargaActualizacion,
                CASE
                    WHEN ultimo.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                    THEN ultimo.fecha_confirmacion
                    ELSE NULL
                END AS FechaAprobacion,
                periodo.fecha_carga_exitosa AS FechaCargaExitosa
            FROM #Periodos periodo
            INNER JOIN dbo.usuario usuario
                ON usuario.id_usuario = periodo.id_usuario_carga
            INNER JOIN dbo.catalogo_entidad_federativa entidad
                ON entidad.id_entidad_federativa = periodo.id_entidad_federativa
            INNER JOIN dbo.catalogo_delito catalogo_delito
                ON catalogo_delito.id_delito = periodo.id_delito
            CROSS APPLY
            (
                SELECT TOP (1)
                    bloque.codigo_referencia,
                    bloque.tipo_carga,
                    bloque.estado,
                    bloque.fecha_validacion,
                    bloque.fecha_confirmacion
                FROM #BloquesCarga bloque
                WHERE bloque.id_entidad_federativa = periodo.id_entidad_federativa
                  AND bloque.id_usuario_carga = periodo.id_usuario_carga
                  AND bloque.id_delito = periodo.id_delito
                  AND bloque.fecha_inicio_semana = periodo.fecha_inicio_semana
                  AND bloque.fecha_fin_semana = periodo.fecha_fin_semana
                  AND bloque.anio_corte = periodo.anio_corte
                  AND bloque.mes_corte = periodo.mes_corte
                ORDER BY bloque.fecha_orden DESC, bloque.id_semanal_carga DESC
            ) ultimo
            ORDER BY
                periodo.anio_corte DESC,
                periodo.mes_corte DESC,
                periodo.fecha_inicio_semana DESC,
                entidad.nombre,
                usuario.usuario,
                catalogo_delito.delito;
        ";
        using var connection = _dbConnectionFactory.CrearConexion();

        var registros = (await connection.QueryAsync<SemanalReporteCargaItem>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga,
            AnioCorte = anioCorte,
            MesCorte = mesCorte
        })).ToList();

        var cultura = CultureInfo.GetCultureInfo("es-MX");

        foreach (var registro in registros)
        {
            var nombreMes = cultura.TextInfo.ToTitleCase(cultura.DateTimeFormat.GetMonthName(registro.MesCorte));

            registro.Periodo = $"{nombreMes} {registro.AnioCorte}";
            registro.Delitos = string.IsNullOrWhiteSpace(registro.Delito) ? new List<string>() : new List<string> { registro.Delito };
            registro.FechaCargaActualizacionTexto = registro.FechaCargaActualizacion.HasValue ? registro.FechaCargaActualizacion.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
            registro.FechaAprobacionTexto = registro.FechaAprobacion.HasValue ? registro.FechaAprobacion.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
        }

        return registros;
    }

}
