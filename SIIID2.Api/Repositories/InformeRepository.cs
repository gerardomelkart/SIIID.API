using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class InformeRepository : IInformeRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public InformeRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<List<InformeEnvioItem>> ObtenerEnviosAsync(bool esSuperUsuario, int? idEntidadFederativaUsuario, int? idEntidadFederativa, int? mesCorte, int? anioCorte)
    {
        // Obtiene el último envío confirmado por entidad y periodo.
        // Considera tanto carga inicial confirmada como actualizaciones confirmadas.
        // Si hay varias actualizaciones confirmadas para el mismo corte,
        // se toma únicamente la más reciente.
        var sql = @"
            WITH envios_confirmados AS (
                SELECT
                    c.id_carga,
                    c.codigo_referencia,
                    c.tipo_carga,
                    c.id_entidad_federativa,
                    c.mes_corte,
                    c.anio_corte,
                    COALESCE(c.fecha_confirmacion, c.fecha_validacion) AS fecha_envio,
                    COALESCE(uconf.usuario, ucarga.usuario) AS usuario_envio,
                    ROW_NUMBER() OVER (
                        PARTITION BY
                            c.id_entidad_federativa,
                            c.mes_corte,
                            c.anio_corte
                        ORDER BY
                            COALESCE(c.fecha_confirmacion, c.fecha_validacion) DESC,
                            c.id_carga DESC
                    ) AS rn
                FROM carga c
                INNER JOIN usuario ucarga
                    ON ucarga.id_usuario = c.id_usuario_carga
                LEFT JOIN usuario uconf
                    ON uconf.id_usuario = c.id_usuario_confirmacion
                WHERE c.activo = 1
                  AND (
                        (c.tipo_carga = 'CARGA_INICIAL' AND c.estado = 'CONFIRMADO')
                     OR (c.tipo_carga = 'ACTUALIZACION' AND c.estado = 'CONFIRMADO_ACTUALIZACION')
                  )
            )
            SELECT
                e.id_carga AS IdCarga,
                e.codigo_referencia AS CodigoReferencia,
                e.tipo_carga AS TipoCarga,
                e.id_entidad_federativa AS IdEntidadFederativa,
                ef.nombre AS EntidadFederativa,
                ef.clave AS ClaveEntidad,
                e.fecha_envio AS FechaEnvio,
                e.mes_corte AS MesCorte,
                e.anio_corte AS AnioCorte,
                e.usuario_envio AS UsuarioEnvio
            FROM envios_confirmados e
            INNER JOIN catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = e.id_entidad_federativa
            WHERE e.rn = 1
              AND (@EsSuperUsuario = 1 OR e.id_entidad_federativa = @IdEntidadFederativaUsuario)
              AND (@IdEntidadFederativa IS NULL OR e.id_entidad_federativa = @IdEntidadFederativa)
              AND (@MesCorte IS NULL OR e.mes_corte = @MesCorte)
              AND (@AnioCorte IS NULL OR e.anio_corte = @AnioCorte)
            ORDER BY
                ef.nombre,
                e.anio_corte DESC,
                e.mes_corte DESC;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var envios = await connection.QueryAsync<InformeEnvioItem>(sql, new
        {
            EsSuperUsuario = esSuperUsuario,
            IdEntidadFederativaUsuario = idEntidadFederativaUsuario,
            IdEntidadFederativa = esSuperUsuario ? idEntidadFederativa : null,
            MesCorte = mesCorte,
            AnioCorte = anioCorte
        });

        return envios
            .Select(x =>
            {
                x.FechaEnvioTexto = x.FechaEnvio.ToString("dd-MM-yyyy");
                x.Corte = $"{ObtenerNombreMes(x.MesCorte)} {x.AnioCorte}";
                x.EndpointAcuse = ObtenerEndpointAcuse(x.TipoCarga, x.CodigoReferencia);
                x.EndpointExcel = $"/api/informes/envios/{x.CodigoReferencia}/archivos";

                return x;
            })
            .ToList();
    }

    private static string ObtenerEndpointAcuse(string tipoCarga, string codigoReferencia)
    {
        if (string.Equals(tipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase))
        {
            return $"/api/actualizaciones/{codigoReferencia}/acuse-confirmado";
        }

        return $"/api/cargas/{codigoReferencia}/acuse-confirmado";
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