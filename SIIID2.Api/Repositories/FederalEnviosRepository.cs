using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class FederalEnviosRepository : IFederalEnviosRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public FederalEnviosRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<List<InformePeriodoItem>> ObtenerPeriodosAsync()
    {
        const string sql = """
            SELECT DISTINCT
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte
            FROM dbo.federal_carga c
            WHERE c.activo = 1
              AND (c.estado NOT LIKE N'RECHAZADO%' OR c.estado = N'RECHAZADO_ADMIN')
              AND c.mes_corte BETWEEN 1 AND 12
              AND c.anio_corte BETWEEN 2000 AND 2100
            ORDER BY
                c.anio_corte DESC,
                c.mes_corte DESC;
            """;

        using var connection = _dbConnectionFactory.CrearConexion();

        var periodos = (await connection.QueryAsync<InformePeriodoItem>(sql)).ToList();

        foreach (var periodo in periodos)
            periodo.Corte = $"{ObtenerNombreMes(periodo.MesCorte)} {periodo.AnioCorte}";

        return periodos;
    }

    public async Task<List<InformeEnvioItem>> ObtenerEnviosAsync(int? mesCorte, int? anioCorte)
    {
        const string sql = """
            WITH ultimo_visible AS
            (
                SELECT
                    c.id_federal_carga,
                    c.codigo_referencia,
                    c.tipo_carga,
                    c.estado,
                    c.id_usuario_carga,
                    c.mes_corte,
                    c.anio_corte,
                    c.fecha_validacion,
                    c.fecha_confirmacion,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY c.mes_corte, c.anio_corte
                        ORDER BY COALESCE(c.fecha_confirmacion, c.fecha_validacion) DESC, c.id_federal_carga DESC
                    ) AS rn
                FROM dbo.federal_carga c
                WHERE c.activo = 1
                  AND c.estado NOT LIKE N'RECHAZADO%'
                  AND (@MesCorte IS NULL OR c.mes_corte = @MesCorte)
                  AND (@AnioCorte IS NULL OR c.anio_corte = @AnioCorte)
            ),
            ultimo_rechazo AS
            (
                SELECT
                    c.id_federal_carga,
                    c.codigo_referencia,
                    c.tipo_carga,
                    c.estado,
                    c.id_usuario_carga,
                    c.id_usuario_confirmacion,
                    c.mes_corte,
                    c.anio_corte,
                    c.fecha_validacion,
                    c.fecha_confirmacion,
                    c.mensaje_error,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY c.mes_corte, c.anio_corte
                        ORDER BY COALESCE(c.fecha_confirmacion, c.fecha_validacion) DESC, c.id_federal_carga DESC
                    ) AS rn
                FROM dbo.federal_carga c
                WHERE c.activo = 1
                  AND c.estado = N'RECHAZADO_ADMIN'
                  AND (@MesCorte IS NULL OR c.mes_corte = @MesCorte)
                  AND (@AnioCorte IS NULL OR c.anio_corte = @AnioCorte)
            )
            SELECT
                c.id_federal_carga AS IdCarga,
                c.codigo_referencia AS CodigoReferencia,
                c.tipo_carga AS TipoCarga,
                c.estado AS Estado,
                0 AS IdEntidadFederativa,
                N'Federal' AS EntidadFederativa,
                N'FGR' AS ClaveEntidad,
                c.fecha_validacion AS FechaEnvio,
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte,
                u.usuario AS UsuarioEnvio,
                CAST(0 AS bit) AS EsRechazadoAdministrador,
                CAST(NULL AS nvarchar(max)) AS MotivoRechazo,
                CAST(NULL AS datetime2(0)) AS FechaRechazo,
                CAST(NULL AS nvarchar(150)) AS UsuarioRechazo,
                CONVERT(bit,
                    CASE WHEN
                        EXISTS
                        (
                            SELECT 1
                            FROM dbo.federal_carga_tmp_carpeta tc
                            WHERE tc.id_federal_carga = c.id_federal_carga
                              AND tc.activo = 1
                        )
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.federal_carga_tmp_delito td
                            WHERE td.id_federal_carga = c.id_federal_carga
                              AND td.activo = 1
                        )
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.federal_carga_tmp_victima tv
                            WHERE tv.id_federal_carga = c.id_federal_carga
                              AND tv.activo = 1
                        )
                        THEN 1
                        ELSE 0
                    END) AS TieneStagingDisponible
            FROM ultimo_visible c
            INNER JOIN dbo.usuario u
                ON u.id_usuario = c.id_usuario_carga
            WHERE c.rn = 1

            UNION ALL

            SELECT
                c.id_federal_carga AS IdCarga,
                c.codigo_referencia AS CodigoReferencia,
                c.tipo_carga AS TipoCarga,
                c.estado AS Estado,
                0 AS IdEntidadFederativa,
                N'Federal' AS EntidadFederativa,
                N'FGR' AS ClaveEntidad,
                c.fecha_validacion AS FechaEnvio,
                c.mes_corte AS MesCorte,
                c.anio_corte AS AnioCorte,
                u.usuario AS UsuarioEnvio,
                CAST(1 AS bit) AS EsRechazadoAdministrador,
                c.mensaje_error AS MotivoRechazo,
                c.fecha_confirmacion AS FechaRechazo,
                ur.usuario AS UsuarioRechazo,
                CONVERT(bit,
                    CASE WHEN
                        EXISTS
                        (
                            SELECT 1
                            FROM dbo.federal_carga_tmp_carpeta tc
                            WHERE tc.id_federal_carga = c.id_federal_carga
                              AND tc.activo = 1
                        )
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.federal_carga_tmp_delito td
                            WHERE td.id_federal_carga = c.id_federal_carga
                              AND td.activo = 1
                        )
                        OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.federal_carga_tmp_victima tv
                            WHERE tv.id_federal_carga = c.id_federal_carga
                              AND tv.activo = 1
                        )
                        THEN 1
                        ELSE 0
                    END) AS TieneStagingDisponible
            FROM ultimo_rechazo c
            INNER JOIN dbo.usuario u
                ON u.id_usuario = c.id_usuario_carga
            LEFT JOIN dbo.usuario ur
                ON ur.id_usuario = c.id_usuario_confirmacion
            WHERE c.rn = 1

            ORDER BY
                AnioCorte DESC,
                MesCorte DESC,
                FechaEnvio DESC
            OPTION (RECOMPILE);
            """;

        using var connection = _dbConnectionFactory.CrearConexion();

        var envios = (await connection.QueryAsync<InformeEnvioItem>(sql, new
        {
            MesCorte = mesCorte,
            AnioCorte = anioCorte
        })).ToList();

        foreach (var envio in envios)
        {
            envio.FechaEnvioTexto = envio.FechaEnvio.ToString("dd-MM-yyyy");
            envio.FechaRechazoTexto = envio.FechaRechazo?.ToString("dd-MM-yyyy") ?? string.Empty;
            envio.Corte = $"{ObtenerNombreMes(envio.MesCorte)} {envio.AnioCorte}";

            envio.EsConfirmado =
                string.Equals(envio.Estado, "CONFIRMADO", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(envio.Estado, "CONFIRMADO_ACTUALIZACION", StringComparison.OrdinalIgnoreCase);

            envio.EstadoTexto = ObtenerEstadoTexto(envio.Estado, envio.TipoCarga);

            if (envio.EsRechazadoAdministrador)
            {
                envio.EndpointAcuse = envio.TieneStagingDisponible && string.Equals(envio.TipoCarga, "CARGA_INICIAL", StringComparison.OrdinalIgnoreCase)
                    ? $"/api/federal/cargas/{envio.CodigoReferencia}/acuse"
                    : string.Empty;

                envio.EndpointExcel = string.Empty;
                continue;
            }

            if (string.Equals(envio.TipoCarga, "CARGA_INICIAL", StringComparison.OrdinalIgnoreCase))
            {
                envio.EndpointAcuse = envio.EsConfirmado
                    ? $"/api/federal/cargas/{envio.CodigoReferencia}/acuse-confirmado"
                    : $"/api/federal/cargas/{envio.CodigoReferencia}/acuse";
            }
            else
            {
                envio.EndpointAcuse = string.Empty;
            }

            envio.EndpointExcel = string.Empty;
        }

        return envios;
    }

    private static string ObtenerEstadoTexto(string estado, string tipoCarga)
    {
        var estadoNormalizado = (estado ?? string.Empty).Trim().ToUpperInvariant();
        var esActualizacion = string.Equals(tipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase);
        var sufijo = esActualizacion ? "actualización" : "carga";

        return estadoNormalizado switch
        {
            "CONFIRMADO" => $"Confirmado {sufijo}",
            "CONFIRMADO_ACTUALIZACION" => $"Confirmado {sufijo}",
            "PENDIENTE_APROBACION" => "Pendiente de aprobación",
            "VALIDADO_PENDIENTE" => $"Pendiente {sufijo}",
            "VALIDADO_PENDIENTE_ACTUALIZACION" => $"Pendiente {sufijo}",
            "RECHAZADO_ADMIN" => "Rechazado por administración",
            _ => estadoNormalizado.Replace("_", " ")
        };
    }

    private static string ObtenerNombreMes(int mes)
    {
        return mes switch
        {
            1 => "Enero",
            2 => "Febrero",
            3 => "Marzo",
            4 => "Abril",
            5 => "Mayo",
            6 => "Junio",
            7 => "Julio",
            8 => "Agosto",
            9 => "Septiembre",
            10 => "Octubre",
            11 => "Noviembre",
            12 => "Diciembre",
            _ => string.Empty
        };
    }
}