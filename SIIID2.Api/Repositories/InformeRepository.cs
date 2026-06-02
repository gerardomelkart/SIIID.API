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

    public async Task<InformeArchivoCargaInfo?> ObtenerCargaConfirmadaParaArchivosAsync(string codigoReferencia)
    {
        // Obtiene una carga o actualización confirmada.
        // Solo se permite descargar archivos de envíos ya confirmados.
        var sql = @"
        SELECT
            c.id_carga AS IdCarga,
            c.codigo_referencia AS CodigoReferencia,
            c.tipo_carga AS TipoCarga,
            c.estado AS Estado,
            c.id_entidad_federativa AS IdEntidadFederativa,
            ef.nombre AS EntidadFederativa,
            c.mes_corte AS MesCorte,
            c.anio_corte AS AnioCorte
        FROM carga c
        INNER JOIN catalogo_entidad_federativa ef
            ON ef.id_entidad_federativa = c.id_entidad_federativa
        WHERE c.codigo_referencia = @CodigoReferencia
          AND c.activo = 1
          AND (
                (c.tipo_carga = 'CARGA_INICIAL' AND c.estado = 'CONFIRMADO')
             OR (c.tipo_carga = 'ACTUALIZACION' AND c.estado = 'CONFIRMADO_ACTUALIZACION')
          );
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QueryFirstOrDefaultAsync<InformeArchivoCargaInfo>(sql, new
        {
            CodigoReferencia = codigoReferencia
        });
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerCarpetasConfirmadasPeriodoAsync(InformeArchivoCargaInfo carga)
    {
        // Reconstruye el archivo de carpetas desde tabla final.
        // Se toman todas las carpetas activas del periodo de la carga/actualización.
        var sql = @"
        WITH cargas_periodo AS (
            SELECT
                id_carga
            FROM carga
            WHERE id_entidad_federativa = @IdEntidadFederativa
              AND mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND activo = 1
              AND (
                    (tipo_carga = 'CARGA_INICIAL' AND estado = 'CONFIRMADO')
                 OR (tipo_carga = 'ACTUALIZACION' AND estado = 'CONFIRMADO_ACTUALIZACION')
              )
        )
        SELECT
            ci.identificador_carpeta_fiscalia AS id_ci,
            ci.nomenclatura_carpeta_fiscalia AS ntra_ci,
            CONVERT(varchar(10), ci.fecha_inicio, 103) AS fha_de_ini,
            ci.resumen_hechos AS rmen_de_hchos
        FROM carpeta_investigacion ci
        INNER JOIN cargas_periodo cp
            ON cp.id_carga = ci.id_carga
        WHERE ci.activo = 1
        ORDER BY
            ci.identificador_carpeta_fiscalia;
    ";

        return await QueryDictionaryAsync(sql, carga);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerDelitosConfirmadosPeriodoAsync(InformeArchivoCargaInfo carga)
    {
        // Reconstruye el archivo de delitos desde tabla final.
        // Para catálogos, se regresan las claves originales esperadas por Excel.
        var sql = @"
        WITH cargas_periodo AS (
            SELECT
                id_carga
            FROM carga
            WHERE id_entidad_federativa = @IdEntidadFederativa
              AND mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND activo = 1
              AND (
                    (tipo_carga = 'CARGA_INICIAL' AND estado = 'CONFIRMADO')
                 OR (tipo_carga = 'ACTUALIZACION' AND estado = 'CONFIRMADO_ACTUALIZACION')
              )
        )
        SELECT
            ci.identificador_carpeta_fiscalia AS id_ci,
            d.identificador_delito_fiscalia AS id_delito,
            d.delito_fiscalia AS dto,
            d.modalidad_delito_fiscalia AS moda_dto,
            CONVERT(varchar(10), fa.clave) AS forma_acc,
            CONVERT(varchar(10), d.fecha_hechos, 103) AS fha_de_hchos,
            CONVERT(varchar(8), d.fecha_hechos, 108) AS hra_de_hchos,
            CONVERT(varchar(10), ic.clave) AS emto_com_dto,
            CONVERT(varchar(10), gc.clave) AS grdo_cons,
            md.clave4 AS clasf_de_dto,
            CONVERT(varchar(10), d.id_entidad_federativa) AS id_ent_hchos,
            mun.clave AS id_mun_hchos,
            d.id_localidad_fiscalia AS id_loc_hchos,
            d.localidad_fiscalia_nombre AS nom_loc_hchos,
            d.id_colonia_fiscalia AS id_col_hchos,
            d.colonia_fiscalia_nombre AS nom_col_hchos,
            cp.codigo_postal AS cp,
            d.coordenada_x AS coord_x,
            d.coordenada_y AS coord_y,
            d.domicilio_hechos AS dom_hchos
        FROM delito d
        INNER JOIN cargas_periodo cper
            ON cper.id_carga = d.id_carga
        INNER JOIN carpeta_investigacion ci
            ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN catalogo_modalidad_delito md
            ON md.id_modalidad_delito = d.id_modalidad_delito
        INNER JOIN catalogo_forma_accion fa
            ON fa.id_forma_accion = d.id_forma_accion
        INNER JOIN catalogo_instrumento_comision ic
            ON ic.id_instrumento_comision = d.id_instrumento_comision
        INNER JOIN catalogo_grado_consumacion gc
            ON gc.id_grado_consumacion = d.id_grado_consumacion
        INNER JOIN catalogo_municipio mun
            ON mun.id_municipio = d.id_municipio
        LEFT JOIN catalogo_codigo_postal cp
            ON cp.id_codigo_postal = d.id_codigo_postal
        WHERE d.activo = 1
        ORDER BY
            ci.identificador_carpeta_fiscalia,
            d.identificador_delito_fiscalia;
    ";

        return await QueryDictionaryAsync(sql, carga);
    }

    public async Task<List<IDictionary<string, object?>>> ObtenerVictimasConfirmadasPeriodoAsync(InformeArchivoCargaInfo carga)
    {
        // Reconstruye el archivo de víctimas desde tabla final.
        // Para catálogos, se regresan las claves originales esperadas por Excel.
        var sql = @"
        WITH cargas_periodo AS (
            SELECT
                id_carga
            FROM carga
            WHERE id_entidad_federativa = @IdEntidadFederativa
              AND mes_corte = @MesCorte
              AND anio_corte = @AnioCorte
              AND activo = 1
              AND (
                    (tipo_carga = 'CARGA_INICIAL' AND estado = 'CONFIRMADO')
                 OR (tipo_carga = 'ACTUALIZACION' AND estado = 'CONFIRMADO_ACTUALIZACION')
              )
        )
        SELECT
            ci.identificador_carpeta_fiscalia AS id_ci,
            d.identificador_delito_fiscalia AS id_delito,
            v.identificador_victima_fiscalia AS id_vicf,
            CONVERT(varchar(10), tv.clave) AS id_tv,
            CONVERT(varchar(10), tvm.clave) AS id_tpm,
            CONVERT(varchar(10), sx.clave) AS sexo,
            CONVERT(varchar(10), gen.clave) AS genero,
            CONVERT(varchar(10), pob.clave) AS pob,
            CONVERT(varchar(10), disc.clave) AS disc,
            CONVERT(varchar(10), v.fecha_nacimiento, 103) AS fha_nac,
            v.edad AS edad,
            nac.clave AS nacional
        FROM victima v
        INNER JOIN cargas_periodo cper
            ON cper.id_carga = v.id_carga
        INNER JOIN delito d
            ON d.id_delito = v.id_delito
           AND d.activo = 1
        INNER JOIN carpeta_investigacion ci
            ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN catalogo_tipo_victima tv
            ON tv.id_tipo_victima = v.id_tipo_victima
        LEFT JOIN catalogo_tipo_victima_moral tvm
            ON tvm.id_tipo_victima_moral = v.id_tipo_victima_moral
        LEFT JOIN catalogo_sexo sx
            ON sx.id_sexo = v.id_sexo
        LEFT JOIN catalogo_genero gen
            ON gen.id_genero = v.id_genero
        LEFT JOIN catalogo_pertenece_poblacion_indigena pob
            ON pob.id_pertenece_poblacion_indigena = v.id_pertenece_poblacion_indigena
        LEFT JOIN catalogo_presenta_discapacidad disc
            ON disc.id_presenta_discapacidad = v.id_presenta_discapacidad
        LEFT JOIN catalogo_nacionalidad nac
            ON nac.id_nacionalidad = v.id_nacionalidad
        WHERE v.activo = 1
        ORDER BY
            ci.identificador_carpeta_fiscalia,
            d.identificador_delito_fiscalia,
            v.identificador_victima_fiscalia;
    ";

        return await QueryDictionaryAsync(sql, carga);
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryAsync(string sql, InformeArchivoCargaInfo carga)
    {
        using var connection = _dbConnectionFactory.CrearConexion();

        var filas = await connection.QueryAsync(sql, new
        {
            carga.IdEntidadFederativa,
            carga.MesCorte,
            carga.AnioCorte
        });

        return filas
            .Select(fila => ((IDictionary<string, object?>)fila)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase))
            .Cast<IDictionary<string, object?>>()
            .ToList();
    }
}