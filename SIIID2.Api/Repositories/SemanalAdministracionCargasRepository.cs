using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class SemanalAdministracionCargasRepository : ISemanalAdministracionCargasRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SemanalAdministracionCargasRepository(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    public async Task<List<SemanalCargaPendienteAdministracionItem>> ObtenerPendientesAsync()
    {
        const string sql = @"
            SELECT
                sc.id_semanal_carga AS IdSemanalCarga,
                sc.codigo_referencia AS CodigoReferencia,
                sc.tipo_carga AS TipoCarga,
                sc.tipo_contenido AS TipoContenido,
                sc.id_entidad_federativa AS IdEntidadFederativa,
                ISNULL(ef.nombre, N'') AS EntidadFederativa,
                sc.anio_semana AS AnioSemana,
                sc.numero_semana AS NumeroSemana,
                sc.fecha_inicio_semana AS FechaInicioSemana,
                sc.fecha_fin_semana AS FechaFinSemana,
                sc.fecha_inicio_tramo AS FechaInicioTramo,
                sc.fecha_fin_tramo AS FechaFinTramo,
                sc.mes_corte AS MesCorte,
                sc.anio_corte AS AnioCorte,
                sc.fecha_validacion AS FechaValidacion,
                sc.id_usuario_carga AS IdUsuarioCarga,
                u.usuario AS UsuarioCarga,
                LTRIM(RTRIM(CONCAT(
                    u.nombre,
                    N' ',
                    u.primer_apellido,
                    CASE
                        WHEN NULLIF(u.segundo_apellido, N'') IS NULL THEN N''
                        ELSE CONCAT(N' ', u.segundo_apellido)
                    END
                ))) AS NombreUsuarioCarga,
                sc.total_carpetas_incluidas AS TotalCarpetasIncluidas,
                sc.total_delitos_incluidos AS TotalDelitosIncluidos,
                sc.total_victimas_incluidas AS TotalVictimasIncluidas,
                sc.total_carpetas_excluidas AS TotalCarpetasExcluidas,
                sc.total_delitos_excluidos AS TotalDelitosExcluidos,
                sc.total_victimas_excluidas AS TotalVictimasExcluidas,
                (
                    SELECT COUNT(1)
                    FROM dbo.semanal_carga_advertencia sca
                    WHERE sca.id_semanal_carga = sc.id_semanal_carga
                      AND sca.activo = 1
                ) AS TotalAdvertencias
            FROM dbo.semanal_carga sc
            INNER JOIN dbo.usuario u
                ON u.id_usuario = sc.id_usuario_carga
            LEFT JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = sc.id_entidad_federativa
            WHERE sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado = N'PENDIENTE_APROBACION'
              AND sc.activo = 1
            ORDER BY
                sc.fecha_validacion ASC,
                sc.id_semanal_carga ASC;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        return (await connection.QueryAsync<SemanalCargaPendienteAdministracionItem>(sql)).ToList();
    }

    public async Task<SemanalCargaPendienteAdministracionDetalle?> ObtenerDetalleAsync(string codigoReferencia)
    {
        const string sqlCarga = @"
            SELECT TOP (1)
                sc.id_semanal_carga AS IdSemanalCarga,
                sc.codigo_referencia AS CodigoReferencia,
                sc.tipo_carga AS TipoCarga,
                sc.tipo_contenido AS TipoContenido,
                sc.id_entidad_federativa AS IdEntidadFederativa,
                ISNULL(ef.nombre, N'') AS EntidadFederativa,
                sc.anio_semana AS AnioSemana,
                sc.numero_semana AS NumeroSemana,
                sc.fecha_inicio_semana AS FechaInicioSemana,
                sc.fecha_fin_semana AS FechaFinSemana,
                sc.fecha_inicio_tramo AS FechaInicioTramo,
                sc.fecha_fin_tramo AS FechaFinTramo,
                sc.mes_corte AS MesCorte,
                sc.anio_corte AS AnioCorte,
                sc.fecha_validacion AS FechaValidacion,
                sc.id_usuario_carga AS IdUsuarioCarga,
                u.usuario AS UsuarioCarga,
                LTRIM(RTRIM(CONCAT(
                    u.nombre,
                    N' ',
                    u.primer_apellido,
                    CASE
                        WHEN NULLIF(u.segundo_apellido, N'') IS NULL THEN N''
                        ELSE CONCAT(N' ', u.segundo_apellido)
                    END
                ))) AS NombreUsuarioCarga,
                sc.total_carpetas_incluidas AS TotalCarpetasIncluidas,
                sc.total_delitos_incluidos AS TotalDelitosIncluidos,
                sc.total_victimas_incluidas AS TotalVictimasIncluidas,
                sc.total_carpetas_excluidas AS TotalCarpetasExcluidas,
                sc.total_delitos_excluidos AS TotalDelitosExcluidos,
                sc.total_victimas_excluidas AS TotalVictimasExcluidas,
                (
                    SELECT COUNT(1)
                    FROM dbo.semanal_carga_advertencia sca
                    WHERE sca.id_semanal_carga = sc.id_semanal_carga
                      AND sca.activo = 1
                ) AS TotalAdvertencias
            FROM dbo.semanal_carga sc
            INNER JOIN dbo.usuario u
                ON u.id_usuario = sc.id_usuario_carga
            LEFT JOIN dbo.catalogo_entidad_federativa ef
                ON ef.id_entidad_federativa = sc.id_entidad_federativa
            WHERE sc.codigo_referencia = @CodigoReferencia
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.estado = N'PENDIENTE_APROBACION'
              AND sc.activo = 1;
        ";

        const string sqlAdvertencias = @"
            SELECT
                sca.id_semanal_carga_advertencia AS IdSemanalCargaAdvertencia,
                sca.codigo AS Codigo,
                sca.archivo AS Archivo,
                sca.numero_fila AS NumeroFila,
                sca.columna AS Columna,
                sca.campo AS Campo,
                sca.valor AS Valor,
                sca.descripcion_resumen AS DescripcionResumen,
                sca.mensaje AS Mensaje,
                sca.aceptada_usuario AS AceptadaUsuario,
                sca.fecha_aceptacion AS FechaAceptacion
            FROM dbo.semanal_carga_advertencia sca
            WHERE sca.id_semanal_carga = @IdSemanalCarga
              AND sca.activo = 1
            ORDER BY
                sca.archivo,
                sca.numero_fila,
                sca.id_semanal_carga_advertencia;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var carga = await connection.QueryFirstOrDefaultAsync<SemanalCargaPendienteAdministracionDetalle>(sqlCarga, new { CodigoReferencia = codigoReferencia });

        if (carga == null) return null;

        carga.Advertencias = (await connection.QueryAsync<SemanalCargaAdvertenciaAdministracionItem>(sqlAdvertencias, new { carga.IdSemanalCarga })).ToList();
        return carga;
    }

    public async Task<SemanalCargaReferenciaAdministracionInfo?> ObtenerReferenciaAsync(string codigoReferencia)
    {
        const string sql = @"
            SELECT TOP (1)
                sc.codigo_referencia AS CodigoReferencia,
                sc.tipo_carga AS TipoCarga,
                sc.estado AS Estado
            FROM dbo.semanal_carga sc
            WHERE sc.codigo_referencia = @CodigoReferencia
              AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
              AND sc.activo = 1;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QueryFirstOrDefaultAsync<SemanalCargaReferenciaAdministracionInfo>(sql, new { CodigoReferencia = codigoReferencia });
    }

    public Task<List<IDictionary<string, object?>>> ObtenerCarpetasPendientesAsync(long idSemanalCarga)
    {
        const string sql = @"
            SELECT
                c.id_ci,
                c.ntra_ci,
                c.fha_de_ini,
                c.hra_de_ini,
                c.rmen_de_hchos
            FROM dbo.semanal_carga_tmp_carpeta c
            WHERE c.id_semanal_carga = @IdSemanalCarga
              AND c.activo = 1
            ORDER BY c.numero_fila;
        ";

        return QueryDictionaryAsync(sql, idSemanalCarga);
    }

    public Task<List<IDictionary<string, object?>>> ObtenerDelitosPendientesAsync(long idSemanalCarga)
    {
        const string sql = @"
            SELECT
                d.id_ci,
                d.id_delito,
                d.dto,
                d.moda_dto,
                d.forma_acc,
                d.fha_de_hchos,
                d.hra_de_hchos,
                d.emto_com_dto,
                d.grdo_cons,
                d.clasf_de_dto,
                d.id_ent_hchos,
                d.id_mun_hchos,
                d.id_loc_hchos,
                d.nom_loc_hchos,
                d.id_col_hchos,
                d.nom_col_hchos,
                d.cp,
                d.coord_x,
                d.coord_y,
                d.dom_hchos
            FROM dbo.semanal_carga_tmp_delito d
            WHERE d.id_semanal_carga = @IdSemanalCarga
              AND d.activo = 1
            ORDER BY d.numero_fila;
        ";

        return QueryDictionaryAsync(sql, idSemanalCarga);
    }

    public Task<List<IDictionary<string, object?>>> ObtenerVictimasPendientesAsync(long idSemanalCarga)
    {
        const string sql = @"
            SELECT
                v.id_ci,
                v.id_delito,
                v.id_vicf,
                v.id_tv,
                v.id_tpm,
                v.sexo,
                v.genero,
                v.pob,
                v.disc,
                v.fha_nac,
                v.edad,
                v.nacional
            FROM dbo.semanal_carga_tmp_victima v
            WHERE v.id_semanal_carga = @IdSemanalCarga
              AND v.activo = 1
            ORDER BY v.numero_fila;
        ";

        return QueryDictionaryAsync(sql, idSemanalCarga);
    }

    private async Task<List<IDictionary<string, object?>>> QueryDictionaryAsync(string sql, long idSemanalCarga)
    {
        using var connection = _dbConnectionFactory.CrearConexion();

        var filas = await connection.QueryAsync(sql, new { IdSemanalCarga = idSemanalCarga });

        return filas
            .Select(fila => ((IDictionary<string, object?>)fila).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase))
            .Cast<IDictionary<string, object?>>()
            .ToList();
    }
}
