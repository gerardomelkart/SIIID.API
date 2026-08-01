using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public class SemanalCargaRepository : ISemanalCargaRepository
{
    private class SemanalCargaConfirmacionInfo
    {
        public long IdSemanalCarga { get; set; }
        public int IdUsuarioCarga { get; set; }
        public string CodigoReferencia { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public DateTime? FechaExpiracion { get; set; }
        public int? IdEntidadFederativaCarga { get; set; }
        public int? IdEntidadFederativaUsuario { get; set; }
        public bool EsSuperUsuario { get; set; }
        public bool HabilitaSemanal { get; set; }
        public bool HabilitaCarga { get; set; }
        public bool HabilitaModificacion { get; set; }
        public string TipoCarga { get; set; } = string.Empty;
        public int MesCorte { get; set; }
        public int AnioCorte { get; set; }
        public DateTime FechaInicioTramo { get; set; }
        public DateTime FechaFinTramo { get; set; }
        public int TotalCarpetasIncluidas { get; set; }
        public int TotalDelitosIncluidos { get; set; }
        public int TotalVictimasIncluidas { get; set; }
    }

    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SemanalCargaRepository(IDbConnectionFactory dbConnectionFactory) => _dbConnectionFactory = dbConnectionFactory;

    public async Task<UsuarioCargaInfo?> ObtenerUsuarioCargaAsync(int idUsuario)
    {
        const string sql = @"
        SELECT
            u.id_usuario AS IdUsuario,
            u.id_entidad_federativa AS IdEntidadFederativa,
            r.rol AS Rol,
            CONVERT(bit, um.habilita_carga) AS HabilitaCarga,
            CONVERT(bit, um.habilita_modificacion) AS HabilitaModificacion
        FROM dbo.usuario u
        INNER JOIN dbo.roles r ON r.id_rol = u.id_rol AND r.activo = 1
        INNER JOIN dbo.usuario_modulo um ON um.id_usuario = u.id_usuario AND um.habilitado = 1 AND um.activo = 1
        INNER JOIN dbo.catalogo_modulo m ON m.id_modulo = um.id_modulo AND m.clave = N'SEMANAL' AND m.activo = 1
        WHERE u.id_usuario = @IdUsuario
          AND u.activo = 1;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();
        return await connection.QueryFirstOrDefaultAsync<UsuarioCargaInfo>(sql, new { IdUsuario = idUsuario });
    }

    public async Task<SemanalCargaAcuseInfo?> ObtenerCargaParaAcuseAsync(string codigoReferencia)
    {
        const string sql = @"
        SELECT
            sc.id_semanal_carga AS IdSemanalCarga,
            sc.codigo_referencia AS CodigoReferencia,
            sc.id_entidad_federativa AS IdEntidadFederativa,
            ISNULL(e.nombre, N'') AS EntidadFederativa,
            sc.tipo_carga AS TipoCarga,
            sc.tipo_contenido AS TipoContenido,
            sc.anio_semana AS AnioSemana,
            sc.numero_semana AS NumeroSemana,
            sc.fecha_inicio_semana AS FechaInicioSemana,
            sc.fecha_fin_semana AS FechaFinSemana,
            sc.fecha_inicio_tramo AS FechaInicioTramo,
            sc.fecha_fin_tramo AS FechaFinTramo,
            sc.mes_corte AS MesCorte,
            sc.anio_corte AS AnioCorte,
            sc.total_carpetas_incluidas AS TotalCarpetasIncluidas,
            sc.total_delitos_incluidos AS TotalDelitosIncluidos,
            sc.total_victimas_incluidas AS TotalVictimasIncluidas,
            sc.total_carpetas_excluidas AS TotalCarpetasExcluidas,
            sc.total_delitos_excluidos AS TotalDelitosExcluidos,
            sc.total_victimas_excluidas AS TotalVictimasExcluidas,
            sc.estado AS Estado,
            sc.fecha_validacion AS FechaValidacion,
            sc.fecha_confirmacion AS FechaConfirmacion,
            sc.id_usuario_carga AS IdUsuarioCarga,
            u.usuario AS UsuarioCarga
        FROM dbo.semanal_carga sc
        INNER JOIN dbo.usuario u
            ON u.id_usuario = sc.id_usuario_carga
        LEFT JOIN dbo.catalogo_entidad_federativa e
            ON e.id_entidad_federativa = sc.id_entidad_federativa
        WHERE sc.codigo_referencia = @CodigoReferencia
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.activo = 1;

        SELECT
            bloque.anio_semana AS AnioSemana,
            bloque.numero_semana AS NumeroSemana,
            bloque.fecha_inicio_semana AS FechaInicioSemana,
            bloque.fecha_fin_semana AS FechaFinSemana,
            bloque.anio_corte AS AnioCorte,
            bloque.mes_corte AS MesCorte,
            bloque.fecha_inicio_tramo AS FechaInicioTramo,
            bloque.fecha_fin_tramo AS FechaFinTramo,
            bloque.total_carpetas AS TotalCarpetas,
            bloque.total_delitos AS TotalDelitos,
            bloque.total_victimas AS TotalVictimas,
            CONVERT(bit, bloque.reemplaza_informacion) AS ReemplazaInformacion
        FROM dbo.semanal_carga_bloque bloque
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = bloque.id_semanal_carga
        WHERE sc.codigo_referencia = @CodigoReferencia
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.activo = 1
          AND bloque.activo = 1
        ORDER BY
            bloque.fecha_inicio_semana,
            bloque.anio_corte,
            bloque.mes_corte;
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        using var resultados = await connection.QueryMultipleAsync(sql, new { CodigoReferencia = codigoReferencia });

        var carga = await resultados.ReadFirstOrDefaultAsync<SemanalCargaAcuseInfo>();

        if (carga == null) return null;

        carga.Bloques = (await resultados.ReadAsync<SemanalCargaAcuseBloque>()).ToList();
        return carga;
    }

    public async Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseAsync(long idSemanalCarga)
    {
        const string sql = @"
    SELECT
        cd.clave2 AS ClaveDelito,
        cd.delito AS TipoDelito,
        sd.clave3 AS ClaveSubtipo,
        sd.subtipo_delito AS SubtipoDelito,
        COUNT(DISTINCT d.id_semanal_carga_tmp_delito) AS TotalDelitos,
        COUNT(DISTINCT v.id_semanal_carga_tmp_victima) AS TotalVictimas,
        MIN(configuracion.orden) AS Orden
    FROM dbo.semanal_carga_delito_configurado configuracion
    INNER JOIN dbo.catalogo_modalidad_delito md
        ON md.id_modalidad_delito = configuracion.id_modalidad_delito
    INNER JOIN dbo.catalogo_subtipo_delito sd
        ON sd.id_subtipo_delito = md.id_subtipo_delito
    INNER JOIN dbo.catalogo_delito cd
        ON cd.id_delito = sd.id_delito
    LEFT JOIN dbo.semanal_carga_tmp_delito d
        ON d.id_semanal_carga = configuracion.id_semanal_carga
       AND LTRIM(RTRIM(d.clasf_de_dto)) = LTRIM(RTRIM(md.clave4))
       AND d.incluido = 1
       AND d.activo = 1
    LEFT JOIN dbo.semanal_carga_tmp_victima v
        ON v.id_semanal_carga = d.id_semanal_carga
       AND v.id_ci = d.id_ci
       AND v.id_delito = d.id_delito
       AND v.incluido = 1
       AND v.activo = 1
    WHERE configuracion.id_semanal_carga = @IdSemanalCarga
    GROUP BY
        cd.clave2,
        cd.delito,
        sd.clave3,
        sd.subtipo_delito
    ORDER BY
        Orden,
        cd.clave2,
        sd.clave3;
";

        using var connection = _dbConnectionFactory.CrearConexion();

        return (await connection.QueryAsync<CargaAcuseResumenItem>(
            sql,
            new { IdSemanalCarga = idSemanalCarga })).ToList();
    }

    public async Task<List<CargaAcuseResumenItem>> ObtenerResumenAcuseConfirmadoAsync(long idSemanalCarga)
    {
        const string sql = @"
    SELECT
        cd.clave2 AS ClaveDelito,
        cd.delito AS TipoDelito,
        sd.clave3 AS ClaveSubtipo,
        sd.subtipo_delito AS SubtipoDelito,
        COUNT(DISTINCT d.id_semanal_delito) AS TotalDelitos,
        COUNT(DISTINCT v.id_semanal_victima) AS TotalVictimas,
        MIN(configuracion.orden) AS Orden
    FROM dbo.semanal_carga_delito_configurado configuracion
    INNER JOIN dbo.catalogo_modalidad_delito md
        ON md.id_modalidad_delito = configuracion.id_modalidad_delito
    INNER JOIN dbo.catalogo_subtipo_delito sd
        ON sd.id_subtipo_delito = md.id_subtipo_delito
    INNER JOIN dbo.catalogo_delito cd
        ON cd.id_delito = sd.id_delito
    LEFT JOIN dbo.semanal_delito d
        ON d.id_semanal_carga = configuracion.id_semanal_carga
       AND d.id_modalidad_delito = configuracion.id_modalidad_delito
       AND d.activo = 1
    LEFT JOIN dbo.semanal_victima v
        ON v.id_semanal_carga = d.id_semanal_carga
       AND v.id_semanal_delito = d.id_semanal_delito
       AND v.activo = 1
    WHERE configuracion.id_semanal_carga = @IdSemanalCarga
    GROUP BY
        cd.clave2,
        cd.delito,
        sd.clave3,
        sd.subtipo_delito
    ORDER BY
        Orden,
        cd.clave2,
        sd.clave3;
";

        using var connection = _dbConnectionFactory.CrearConexion();

        return (await connection.QueryAsync<CargaAcuseResumenItem>(
            sql,
            new { IdSemanalCarga = idSemanalCarga })).ToList();
    }

    public async Task<List<SemanalCargaBloqueConfirmado>> ObtenerBloquesConfirmadosAsync(int idEntidadFederativa, int idUsuarioCarga, DateTime fechaInicio, DateTime fechaFin)
    {
        const string sql = @"
        SELECT DISTINCT
            DATEADD(DAY, -(DATEDIFF(DAY, CONVERT(date, '19000101', 112), CONVERT(date, ci.fecha_inicio)) % 7), CONVERT(date, ci.fecha_inicio)) AS FechaInicioSemana,
            YEAR(ci.fecha_inicio) AS AnioCorte,
            MONTH(ci.fecha_inicio) AS MesCorte
        FROM dbo.semanal_carpeta_investigacion ci
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = ci.id_semanal_carga
        WHERE sc.id_entidad_federativa = @IdEntidadFederativa
          AND sc.id_usuario_carga = @IdUsuarioCarga
          AND ci.fecha_inicio >= @FechaInicio
          AND ci.fecha_inicio < @FechaFinExclusiva
          AND ci.activo = 1
          AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
          AND sc.activo = 1
        ORDER BY
            FechaInicioSemana,
            AnioCorte,
            MesCorte;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return (await connection.QueryAsync<SemanalCargaBloqueConfirmado>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga,
            FechaInicio = fechaInicio.Date,
            FechaFinExclusiva = fechaFin.Date.AddDays(1)
        })).ToList();
    }

    public async Task<List<SemanalCargaBloquePendiente>> ObtenerBloquesPendientesAsync(int idEntidadFederativa, int idUsuarioCarga, DateTime fechaInicio, DateTime fechaFin)
    {
        const string sql = @"
        SELECT DISTINCT
            sc.codigo_referencia AS CodigoReferencia,
            sc.estado AS Estado,
            bloque.anio_semana AS AnioSemana,
            bloque.numero_semana AS NumeroSemana,
            bloque.fecha_inicio_semana AS FechaInicioSemana,
            bloque.anio_corte AS AnioCorte,
            bloque.mes_corte AS MesCorte
        FROM dbo.semanal_carga sc
        INNER JOIN dbo.semanal_carga_bloque bloque
            ON bloque.id_semanal_carga = sc.id_semanal_carga
           AND bloque.activo = 1
        WHERE sc.id_entidad_federativa = @IdEntidadFederativa
          AND sc.id_usuario_carga = @IdUsuarioCarga
          AND bloque.fecha_inicio_tramo <= @FechaFin
          AND bloque.fecha_fin_tramo >= @FechaInicio
          AND sc.estado IN
          (
              N'VALIDADO_PENDIENTE',
              N'VALIDADO_PENDIENTE_ACTUALIZACION',
              N'PENDIENTE_APROBACION'
          )
          AND sc.activo = 1
        ORDER BY
            FechaInicioSemana,
            AnioCorte,
            MesCorte,
            CodigoReferencia;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return (await connection.QueryAsync<SemanalCargaBloquePendiente>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga,
            FechaInicio = fechaInicio.Date,
            FechaFin = fechaFin.Date
        })).ToList();
    }
    
    public async Task<SemanalSemanaEstadoInfo> ObtenerEstadoSemanaAsync(int idEntidadFederativa, int idUsuarioCarga, int anioSemana, int numeroSemana)
    {
        const string sql = @"
        SELECT
            CONVERT(bit, CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.semanal_carga sc
                WHERE sc.id_entidad_federativa = @IdEntidadFederativa
                  AND sc.id_usuario_carga = @IdUsuarioCarga
                  AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                  AND sc.activo = 1
                  AND
                  (
                      EXISTS
                      (
                          SELECT 1
                          FROM dbo.semanal_carga_bloque bloque
                          WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                            AND bloque.anio_semana = @AnioSemana
                            AND bloque.numero_semana = @NumeroSemana
                            AND bloque.activo = 1
                      )
                      OR
                      (
                          NOT EXISTS
                          (
                              SELECT 1
                              FROM dbo.semanal_carga_bloque bloque
                              WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                                AND bloque.activo = 1
                          )
                          AND sc.anio_semana = @AnioSemana
                          AND sc.numero_semana = @NumeroSemana
                      )
                  )
            ) THEN 1 ELSE 0 END) AS TieneCargaConfirmada,
            pendiente.codigo_referencia AS CodigoReferenciaPendiente,
            pendiente.estado AS EstadoPendiente,
            pendiente.tipo_carga AS TipoCargaPendiente,
            pendiente.id_usuario_carga AS IdUsuarioCargaPendiente
        FROM (VALUES (1)) base(valor)
        OUTER APPLY
        (
            SELECT TOP (1)
                sc.codigo_referencia,
                sc.estado,
                sc.tipo_carga,
                sc.id_usuario_carga
            FROM dbo.semanal_carga sc
            WHERE sc.id_entidad_federativa = @IdEntidadFederativa
              AND sc.id_usuario_carga = @IdUsuarioCarga
              AND sc.estado IN
              (
                  N'VALIDADO_PENDIENTE',
                  N'VALIDADO_PENDIENTE_ACTUALIZACION',
                  N'PENDIENTE_APROBACION'
              )
              AND sc.activo = 1
              AND
              (
                  EXISTS
                  (
                      SELECT 1
                      FROM dbo.semanal_carga_bloque bloque
                      WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                        AND bloque.anio_semana = @AnioSemana
                        AND bloque.numero_semana = @NumeroSemana
                        AND bloque.activo = 1
                  )
                  OR
                  (
                      NOT EXISTS
                      (
                          SELECT 1
                          FROM dbo.semanal_carga_bloque bloque
                          WHERE bloque.id_semanal_carga = sc.id_semanal_carga
                            AND bloque.activo = 1
                      )
                      AND sc.anio_semana = @AnioSemana
                      AND sc.numero_semana = @NumeroSemana
                  )
              )
            ORDER BY
                sc.fecha_validacion DESC,
                sc.id_semanal_carga DESC
        ) pendiente;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        return await connection.QuerySingleAsync<SemanalSemanaEstadoInfo>(sql, new
        {
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga,
            AnioSemana = anioSemana,
            NumeroSemana = numeroSemana
        });
    }

    public async Task<SemanalDatosComparacion> ObtenerDatosComparacionAsync(long idSemanalCarga, int idEntidadFederativa, int idUsuarioCarga)
    {
        const string sql = @"
        SELECT
            sc.id_semanal_carga AS IdSemanalCarga,
            sc.fecha_confirmacion AS FechaConfirmacion,
            ci.identificador_carpeta_fiscalia AS IdCi,
            (
                SELECT
                    ci.identificador_carpeta_fiscalia AS id_ci,
                    ci.nomenclatura_carpeta_fiscalia AS ntra_ci,
                    CONVERT(varchar(10), ci.fecha_inicio, 23) AS fha_de_ini,
                    CONVERT(varchar(8), ci.fecha_inicio, 108) AS hra_de_ini,
                    ci.resumen_hechos AS rmen_de_hchos
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
            ) AS Datos
        FROM dbo.semanal_carpeta_investigacion ci
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = ci.id_semanal_carga
        WHERE sc.id_entidad_federativa = @IdEntidadFederativa
          AND sc.id_usuario_carga = @IdUsuarioCarga
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
          AND sc.activo = 1
          AND ci.activo = 1
          AND EXISTS
          (
              SELECT 1
              FROM dbo.semanal_carga_bloque bloque
              WHERE bloque.id_semanal_carga = @IdSemanalCarga
                AND bloque.id_entidad_federativa = @IdEntidadFederativa
                AND bloque.reemplaza_informacion = 1
                AND bloque.activo = 1
                AND ci.fecha_inicio >= bloque.fecha_inicio_tramo
                AND ci.fecha_inicio < DATEADD(DAY, 1, bloque.fecha_fin_tramo)
          );

        SELECT
            sc.id_semanal_carga AS IdSemanalCarga,
            sc.fecha_confirmacion AS FechaConfirmacion,
            ci.identificador_carpeta_fiscalia AS IdCi,
            (
                SELECT
                    ci.identificador_carpeta_fiscalia AS id_ci,
                    d.identificador_delito_fiscalia AS id_delito,
                    d.delito_fiscalia AS dto,
                    d.modalidad_delito_fiscalia AS moda_dto,
                    CONVERT(varchar(10), fa.clave) AS forma_acc,
                    CONVERT(varchar(10), d.fecha_hechos, 23) AS fha_de_hchos,
                    CONVERT(varchar(8), d.fecha_hechos, 108) AS hra_de_hchos,
                    CONVERT(varchar(10), ic.clave) AS emto_com_dto,
                    CONVERT(varchar(10), gc.clave) AS grdo_cons,
                    md.clave4 AS clasf_de_dto,
                    CONVERT(varchar(10), d.id_entidad_federativa) AS id_ent_hchos,
                    CONVERT(varchar(20), mun.clave) AS id_mun_hchos,
                    d.id_localidad_fiscalia AS id_loc_hchos,
                    d.localidad_fiscalia_nombre AS nom_loc_hchos,
                    d.id_colonia_fiscalia AS id_col_hchos,
                    d.colonia_fiscalia_nombre AS nom_col_hchos,
                    cp.codigo_postal AS cp,
                    CONVERT(varchar(50), d.coordenada_x) AS coord_x,
                    CONVERT(varchar(50), d.coordenada_y) AS coord_y,
                    d.domicilio_hechos AS dom_hchos
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
            ) AS Datos
        FROM dbo.semanal_delito d
        INNER JOIN dbo.semanal_carpeta_investigacion ci
            ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = d.id_semanal_carga
        INNER JOIN dbo.catalogo_forma_accion fa
            ON fa.id_forma_accion = d.id_forma_accion
        INNER JOIN dbo.catalogo_instrumento_comision ic
            ON ic.id_instrumento_comision = d.id_instrumento_comision
        INNER JOIN dbo.catalogo_grado_consumacion gc
            ON gc.id_grado_consumacion = d.id_grado_consumacion
        INNER JOIN dbo.catalogo_modalidad_delito md
            ON md.id_modalidad_delito = d.id_modalidad_delito
        INNER JOIN dbo.catalogo_municipio mun
            ON mun.id_municipio = d.id_municipio
        LEFT JOIN dbo.catalogo_codigo_postal cp
            ON cp.id_codigo_postal = d.id_codigo_postal
        WHERE sc.id_entidad_federativa = @IdEntidadFederativa
          AND sc.id_usuario_carga = @IdUsuarioCarga
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
          AND sc.activo = 1
          AND d.activo = 1
          AND EXISTS
          (
              SELECT 1
              FROM dbo.semanal_carga_bloque bloque
              WHERE bloque.id_semanal_carga = @IdSemanalCarga
                AND bloque.id_entidad_federativa = @IdEntidadFederativa
                AND bloque.reemplaza_informacion = 1
                AND bloque.activo = 1
                AND ci.fecha_inicio >= bloque.fecha_inicio_tramo
                AND ci.fecha_inicio < DATEADD(DAY, 1, bloque.fecha_fin_tramo)
          );

        SELECT
            sc.id_semanal_carga AS IdSemanalCarga,
            sc.fecha_confirmacion AS FechaConfirmacion,
            ci.identificador_carpeta_fiscalia AS IdCi,
            (
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
                    CONVERT(varchar(10), v.fecha_nacimiento, 23) AS fha_nac,
                    CONVERT(varchar(10), v.edad) AS edad,
                    CONVERT(varchar(20), nac.clave) AS nacional
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
            ) AS Datos
        FROM dbo.semanal_victima v
        INNER JOIN dbo.semanal_delito d
            ON d.id_semanal_delito = v.id_semanal_delito
           AND d.activo = 1
        INNER JOIN dbo.semanal_carpeta_investigacion ci
            ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
           AND ci.activo = 1
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = v.id_semanal_carga
        INNER JOIN dbo.catalogo_tipo_victima tv
            ON tv.id_tipo_victima = v.id_tipo_victima
        LEFT JOIN dbo.catalogo_tipo_victima_moral tvm
            ON tvm.id_tipo_victima_moral = v.id_tipo_victima_moral
        LEFT JOIN dbo.catalogo_sexo sx
            ON sx.id_sexo = v.id_sexo
        LEFT JOIN dbo.catalogo_genero gen
            ON gen.id_genero = v.id_genero
        LEFT JOIN dbo.catalogo_nacionalidad nac
            ON nac.id_nacionalidad = v.id_nacionalidad
        LEFT JOIN dbo.catalogo_pertenece_poblacion_indigena pob
            ON pob.id_pertenece_poblacion_indigena = v.id_pertenece_poblacion_indigena
        LEFT JOIN dbo.catalogo_presenta_discapacidad disc
            ON disc.id_presenta_discapacidad = v.id_presenta_discapacidad
        WHERE sc.id_entidad_federativa = @IdEntidadFederativa
          AND sc.id_usuario_carga = @IdUsuarioCarga
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
          AND sc.activo = 1
          AND v.activo = 1
          AND EXISTS
          (
              SELECT 1
              FROM dbo.semanal_carga_bloque bloque
              WHERE bloque.id_semanal_carga = @IdSemanalCarga
                AND bloque.id_entidad_federativa = @IdEntidadFederativa
                AND bloque.reemplaza_informacion = 1
                AND bloque.activo = 1
                AND ci.fecha_inicio >= bloque.fecha_inicio_tramo
                AND ci.fecha_inicio < DATEADD(DAY, 1, bloque.fecha_fin_tramo)
          );
        ";

        using var connection = _dbConnectionFactory.CrearConexion();
        using var resultados = await connection.QueryMultipleAsync(sql, new
        {
            IdSemanalCarga = idSemanalCarga,
            IdEntidadFederativa = idEntidadFederativa,
            IdUsuarioCarga = idUsuarioCarga
        });

        return new SemanalDatosComparacion
        {
            CarpetasConfirmadas = (await resultados.ReadAsync<SemanalFilaComparacion>()).ToList(),
            DelitosConfirmados = (await resultados.ReadAsync<SemanalFilaComparacion>()).ToList(),
            VictimasConfirmadas = (await resultados.ReadAsync<SemanalFilaComparacion>()).ToList()
        };
    }

    public async Task<SemanalCargaDiferenciasInfo?> ObtenerCargaParaDiferenciasAsync(string codigoReferencia)
    {
        const string sqlContexto = @"
        SELECT TOP (1)
            sc.id_semanal_carga AS IdSemanalCarga,
            sc.codigo_referencia AS CodigoReferencia,
            sc.id_usuario_carga AS IdUsuarioCarga,
            sc.id_entidad_federativa AS IdEntidadFederativa,
            sc.tipo_carga AS TipoCarga,
            sc.estado AS Estado,
            sc.mes_corte AS MesCorte,
            sc.anio_corte AS AnioCorte,
            sc.anio_semana AS AnioSemana,
            sc.numero_semana AS NumeroSemana,
            sc.fecha_inicio_semana AS FechaInicioSemana,
            sc.fecha_fin_semana AS FechaFinSemana
        FROM dbo.semanal_carga sc
        WHERE sc.codigo_referencia = @CodigoReferencia
          AND sc.tipo_carga = N'ACTUALIZACION'
          AND sc.estado IN
          (
              N'VALIDADO_PENDIENTE_ACTUALIZACION',
              N'PENDIENTE_APROBACION'
          )
          AND sc.activo = 1;
    ";

        using var connection = _dbConnectionFactory.CrearConexion();

        var contexto = await connection.QueryFirstOrDefaultAsync<SemanalCargaDiferenciasInfo>(
            sqlContexto,
            new { CodigoReferencia = codigoReferencia });

        if (contexto == null) return null;

        const string sqlDatos = @"
        SELECT
            c.id_semanal_carga AS IdSemanalCarga,
            GETDATE() AS FechaConfirmacion,
            c.id_ci AS IdCi,
            (
                SELECT
                    c.id_ci AS id_ci,
                    c.ntra_ci AS ntra_ci,
                    c.fha_de_ini AS fha_de_ini,
                    c.hra_de_ini AS hra_de_ini,
                    c.rmen_de_hchos AS rmen_de_hchos
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
            ) AS Datos
        FROM dbo.semanal_carga_tmp_carpeta c
        WHERE c.id_semanal_carga = @IdSemanalCarga
          AND c.incluido = 1
          AND c.activo = 1
        ORDER BY c.numero_fila;

        SELECT
            d.id_semanal_carga AS IdSemanalCarga,
            GETDATE() AS FechaConfirmacion,
            d.id_ci AS IdCi,
            (
                SELECT
                    d.id_ci AS id_ci,
                    d.id_delito AS id_delito,
                    d.dto AS dto,
                    d.moda_dto AS moda_dto,
                    d.forma_acc AS forma_acc,
                    d.fha_de_hchos AS fha_de_hchos,
                    d.hra_de_hchos AS hra_de_hchos,
                    d.emto_com_dto AS emto_com_dto,
                    d.grdo_cons AS grdo_cons,
                    d.clasf_de_dto AS clasf_de_dto,
                    d.id_ent_hchos AS id_ent_hchos,
                    d.id_mun_hchos AS id_mun_hchos,
                    d.id_loc_hchos AS id_loc_hchos,
                    d.nom_loc_hchos AS nom_loc_hchos,
                    d.id_col_hchos AS id_col_hchos,
                    d.nom_col_hchos AS nom_col_hchos,
                    d.cp AS cp,
                    d.coord_x AS coord_x,
                    d.coord_y AS coord_y,
                    d.dom_hchos AS dom_hchos
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
            ) AS Datos
        FROM dbo.semanal_carga_tmp_delito d
        WHERE d.id_semanal_carga = @IdSemanalCarga
          AND d.incluido = 1
          AND d.activo = 1
        ORDER BY d.numero_fila;

        SELECT
            v.id_semanal_carga AS IdSemanalCarga,
            GETDATE() AS FechaConfirmacion,
            v.id_ci AS IdCi,
            (
                SELECT
                    v.id_ci AS id_ci,
                    v.id_delito AS id_delito,
                    v.id_vicf AS id_vicf,
                    v.id_tv AS id_tv,
                    v.id_tpm AS id_tpm,
                    v.sexo AS sexo,
                    v.genero AS genero,
                    v.pob AS pob,
                    v.disc AS disc,
                    v.fha_nac AS fha_nac,
                    v.edad AS edad,
                    v.nacional AS nacional
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES
            ) AS Datos
        FROM dbo.semanal_carga_tmp_victima v
        WHERE v.id_semanal_carga = @IdSemanalCarga
          AND v.incluido = 1
          AND v.activo = 1
        ORDER BY v.numero_fila;
    ";

        using var resultados = await connection.QueryMultipleAsync(
            sqlDatos,
            new { contexto.IdSemanalCarga });

        contexto.Carpetas = (await resultados.ReadAsync<SemanalFilaComparacion>()).ToList();
        contexto.Delitos = (await resultados.ReadAsync<SemanalFilaComparacion>()).ToList();
        contexto.Victimas = (await resultados.ReadAsync<SemanalFilaComparacion>()).ToList();

        return contexto;
    }

    public async Task<long> GuardarIntentoCargaAsync(SemanalCargaPersistencia carga)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var idSemanalCarga = await CrearCargaAsync(connection, transaction, carga);
            await GuardarBloquesCargaAsync(connection, transaction, idSemanalCarga, carga);
            await GuardarConfiguracionCargaAsync(connection, transaction, idSemanalCarga, carga.ModalidadesConfiguradas);
            await GuardarTmpCarpetasAsync(connection, transaction, idSemanalCarga, carga.Carpetas);
            await GuardarTmpDelitosAsync(connection, transaction, idSemanalCarga, carga.Delitos);
            await GuardarTmpVictimasAsync(connection, transaction, idSemanalCarga, carga.Victimas);
            await SemanalCargaAuditoriaSql.GuardarAdvertenciasAsync(connection, transaction, idSemanalCarga, carga.Advertencias);
            await transaction.CommitAsync();
            return idSemanalCarga;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ConfirmarCargaResponse> ConfirmarCargaAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerCargaConfirmacionAsync(connection, transaction, codigoReferencia, idUsuarioConfirmacion);

            if (carga == null)
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, "NO_ENCONTRADA", "No se encontró una carga semanal válida para continuar.");
            }

            var esActualizacion = string.Equals(carga.TipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase);
            var estadoEsperado = esActualizacion ? "VALIDADO_PENDIENTE_ACTUALIZACION" : "VALIDADO_PENDIENTE";

            if (!string.Equals(carga.Estado, estadoEsperado, StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, carga.Estado, $"La operación semanal no se encuentra en estado {estadoEsperado}.");
            }

            if (carga.IdUsuarioCarga != idUsuarioConfirmacion)
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, carga.Estado, "Solo el usuario que realizó la carga puede aceptarla o rechazarla.");
            }

            if (!carga.HabilitaSemanal || (esActualizacion ? !carga.HabilitaModificacion : !carga.HabilitaCarga))
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, carga.Estado, esActualizacion ? "El usuario no tiene habilitada la modificación de información en el módulo semanal." : "El usuario no tiene habilitada la carga de información en el módulo semanal.");
            }

            if (carga.FechaExpiracion.HasValue && carga.FechaExpiracion.Value < DateTime.Now)
            {
                await ActualizarCargaExpiradaAsync(connection, transaction, carga.IdSemanalCarga, esActualizacion);
                await transaction.CommitAsync();
                return Error(codigoReferencia, esActualizacion ? "EXPIRADO_ACTUALIZACION" : "EXPIRADO", "La operación semanal expiró. Debe validar nuevamente los archivos.");
            }

            if (!carga.EsSuperUsuario && carga.IdEntidadFederativaUsuario != carga.IdEntidadFederativaCarga)
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, carga.Estado, "El usuario no puede procesar cargas semanales de otra entidad federativa.");
            }

            if (aceptar && EsPeriodoAnteriorAlMesActual(carga.MesCorte, carga.AnioCorte))
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, "PERIODO_CONSOLIDADO", "La carga corresponde a un mes anterior al mes en curso y ya no puede confirmarse porque ese periodo pertenece al consolidado.");
            }

            if (!aceptar)
            {
                await RechazarCargaAsync(connection, transaction, carga.IdSemanalCarga, idUsuarioConfirmacion, esActualizacion);
                await transaction.CommitAsync();
                return Exito(codigoReferencia, esActualizacion ? "RECHAZADO_VALIDACION_ACTUALIZACION" : "RECHAZADO_VALIDACION", esActualizacion ? "La actualización semanal fue rechazada correctamente." : "La carga semanal fue rechazada correctamente.");
            }

            await SemanalCargaAuditoriaSql.MarcarAdvertenciasAceptadasAsync(connection, transaction, carga.IdSemanalCarga, idUsuarioConfirmacion);

            if (!carga.EsSuperUsuario)
            {
                await EnviarCargaAprobacionAsync(connection, transaction, carga.IdSemanalCarga);
                await transaction.CommitAsync();
                return Exito(codigoReferencia, "PENDIENTE_APROBACION", esActualizacion ? "La actualización semanal fue enviada correctamente a revisión administrativa." : "La carga semanal fue enviada correctamente a revisión administrativa.");
            }

            await PrepararReemplazoBloquesAsync(connection, transaction, carga, idUsuarioConfirmacion);

            var totalCarpetas = await InsertarCarpetasFinalesAsync(connection, transaction, carga.IdSemanalCarga, idUsuarioConfirmacion);
            var totalDelitos = await InsertarDelitosFinalesAsync(connection, transaction, carga.IdSemanalCarga, idUsuarioConfirmacion);
            var totalVictimas = await InsertarVictimasFinalesAsync(connection, transaction, carga.IdSemanalCarga, idUsuarioConfirmacion);

            if (totalCarpetas != carga.TotalCarpetasIncluidas || totalDelitos != carga.TotalDelitosIncluidos || totalVictimas != carga.TotalVictimasIncluidas)
            {
                throw new InvalidOperationException($"Los totales insertados no coinciden con la validación semanal. Carpetas {totalCarpetas}/{carga.TotalCarpetasIncluidas}, delitos {totalDelitos}/{carga.TotalDelitosIncluidos}, víctimas {totalVictimas}/{carga.TotalVictimasIncluidas}.");
            }

            await ConfirmarCargaFinalAsync(connection, transaction, carga.IdSemanalCarga, idUsuarioConfirmacion, esActualizacion);
            await transaction.CommitAsync();
            return Exito(codigoReferencia, esActualizacion ? "CONFIRMADO_ACTUALIZACION" : "CONFIRMADO", esActualizacion ? "La actualización semanal fue confirmada correctamente." : "La carga semanal fue confirmada correctamente.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ConfirmarCargaResponse> AprobarCargaPendienteAsync(string codigoReferencia, int idUsuarioAprobacion)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerCargaConfirmacionAsync(connection, transaction, codigoReferencia, idUsuarioAprobacion);

            if (carga == null)
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, "NO_ENCONTRADA", "No se encontró la carga semanal pendiente de aprobación.");
            }

            if (!carga.EsSuperUsuario || !carga.HabilitaSemanal)
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, carga.Estado, "Solo un superusuario con acceso al módulo semanal puede aprobar cargas.");
            }

            if (!string.Equals(carga.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, carga.Estado, "La carga semanal ya no se encuentra pendiente de aprobación.");
            }

            if (EsPeriodoAnteriorAlMesActual(carga.MesCorte, carga.AnioCorte))
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, "PERIODO_CONSOLIDADO", "La carga corresponde a un mes anterior al mes en curso y ya no puede aprobarse porque ese periodo pertenece al consolidado.");
            }

            var esActualizacion = string.Equals(carga.TipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase);

            await PrepararReemplazoBloquesAsync(connection, transaction, carga, idUsuarioAprobacion);

            var totalCarpetas = await InsertarCarpetasFinalesAsync(connection, transaction, carga.IdSemanalCarga, carga.IdUsuarioCarga);
            var totalDelitos = await InsertarDelitosFinalesAsync(connection, transaction, carga.IdSemanalCarga, carga.IdUsuarioCarga);
            var totalVictimas = await InsertarVictimasFinalesAsync(connection, transaction, carga.IdSemanalCarga, carga.IdUsuarioCarga);

            if (totalCarpetas != carga.TotalCarpetasIncluidas || totalDelitos != carga.TotalDelitosIncluidos || totalVictimas != carga.TotalVictimasIncluidas)
            {
                throw new InvalidOperationException($"Los totales insertados no coinciden con la validación semanal. Carpetas {totalCarpetas}/{carga.TotalCarpetasIncluidas}, delitos {totalDelitos}/{carga.TotalDelitosIncluidos}, víctimas {totalVictimas}/{carga.TotalVictimasIncluidas}.");
            }

            await ConfirmarCargaFinalAsync(connection, transaction, carga.IdSemanalCarga, idUsuarioAprobacion, esActualizacion);
            await transaction.CommitAsync();

            return Exito(codigoReferencia, esActualizacion ? "CONFIRMADO_ACTUALIZACION" : "CONFIRMADO", esActualizacion ? "La actualización semanal fue aprobada y confirmada correctamente." : "La carga semanal fue aprobada y confirmada correctamente.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ConfirmarCargaResponse> RechazarCargaPendienteAsync(string codigoReferencia, int idUsuarioRechazo, string motivo)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerCargaConfirmacionAsync(connection, transaction, codigoReferencia, idUsuarioRechazo);

            if (carga == null)
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, "NO_ENCONTRADA", "No se encontró la carga semanal pendiente de aprobación.");
            }

            if (!carga.EsSuperUsuario || !carga.HabilitaSemanal)
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, carga.Estado, "Solo un superusuario con acceso al módulo semanal puede rechazar cargas.");
            }

            if (!string.Equals(carga.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();
                return Error(codigoReferencia, carga.Estado, "La carga semanal ya no se encuentra pendiente de aprobación.");
            }

            await RechazarCargaAdministracionAsync(connection, transaction, carga.IdSemanalCarga, idUsuarioRechazo, motivo);
            await transaction.CommitAsync();

            return Exito(codigoReferencia, "RECHAZADO_ADMIN", "La carga semanal fue rechazada por el administrador.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<long> CrearCargaAsync(SqlConnection connection, SqlTransaction transaction, SemanalCargaPersistencia carga)
    {
        const string sql = @"
        INSERT INTO dbo.semanal_carga
        (
            id_usuario_carga,
            id_entidad_federativa,
            codigo_referencia,
            tipo_carga,
            tipo_contenido,
            anio_semana,
            numero_semana,
            fecha_inicio_semana,
            fecha_fin_semana,
            fecha_inicio_tramo,
            fecha_fin_tramo,
            mes_corte,
            anio_corte,
            total_carpetas_incluidas,
            total_delitos_incluidos,
            total_victimas_incluidas,
            total_carpetas_excluidas,
            total_delitos_excluidos,
            total_victimas_excluidas,
            estado,
            fecha_validacion,
            fecha_expiracion,
            activo
        )
        OUTPUT INSERTED.id_semanal_carga
        VALUES
        (
            @IdUsuarioCarga,
            @IdEntidadFederativa,
            @CodigoReferencia,
            @TipoCarga,
            @TipoContenido,
            @AnioSemana,
            @NumeroSemana,
            @FechaInicioSemana,
            @FechaFinSemana,
            @FechaInicioTramo,
            @FechaFinTramo,
            @MesCorte,
            @AnioCorte,
            @TotalCarpetasIncluidas,
            @TotalDelitosIncluidos,
            @TotalVictimasIncluidas,
            @TotalCarpetasExcluidas,
            @TotalDelitosExcluidos,
            @TotalVictimasExcluidas,
            @EstadoInicial,
            SYSDATETIME(),
            DATEADD(HOUR, 48, SYSDATETIME()),
            1
        );
    ";

        return await connection.ExecuteScalarAsync<long>(sql, new
        {
            carga.IdUsuarioCarga,
            carga.IdEntidadFederativa,
            carga.CodigoReferencia,
            carga.TipoCarga,
            EstadoInicial = string.Equals(carga.TipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase) ? "VALIDADO_PENDIENTE_ACTUALIZACION" : "VALIDADO_PENDIENTE",
            carga.Periodo.TipoContenido,
            carga.Periodo.AnioSemana,
            carga.Periodo.NumeroSemana,
            FechaInicioSemana = carga.Periodo.FechaInicioSemana.Date,
            FechaFinSemana = carga.Periodo.FechaFinSemana.Date,
            FechaInicioTramo = carga.Periodo.FechaInicioTramo.Date,
            FechaFinTramo = carga.Periodo.FechaFinTramo.Date,
            carga.Periodo.MesCorte,
            carga.Periodo.AnioCorte,
            carga.TotalCarpetasIncluidas,
            carga.TotalDelitosIncluidos,
            carga.TotalVictimasIncluidas,
            carga.TotalCarpetasExcluidas,
            carga.TotalDelitosExcluidos,
            carga.TotalVictimasExcluidas
        }, transaction);
    }

    private static async Task GuardarBloquesCargaAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, SemanalCargaPersistencia carga)
    {
        const string sql = @"
        INSERT INTO dbo.semanal_carga_bloque
        (
            id_semanal_carga,
            id_entidad_federativa,
            id_usuario_carga,
            anio_semana,
            numero_semana,
            fecha_inicio_semana,
            fecha_fin_semana,
            anio_corte,
            mes_corte,
            fecha_inicio_tramo,
            fecha_fin_tramo,
            total_carpetas,
            total_delitos,
            total_victimas,
            reemplaza_informacion,
            estado
        )
        VALUES
        (
            @IdSemanalCarga,
            @IdEntidadFederativa,
            @IdUsuarioCarga,
            @AnioSemana,
            @NumeroSemana,
            @FechaInicioSemana,
            @FechaFinSemana,
            @AnioCorte,
            @MesCorte,
            @FechaInicioTramo,
            @FechaFinTramo,
            @TotalCarpetas,
            @TotalDelitos,
            @TotalVictimas,
            @ReemplazaInformacion,
            @Estado
        );
    ";

        var estadoInicial = string.Equals(carga.TipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase) ? "VALIDADO_PENDIENTE_ACTUALIZACION" : "VALIDADO_PENDIENTE";

        await connection.ExecuteAsync(sql, carga.Bloques.Select(bloque => new
        {
            IdSemanalCarga = idSemanalCarga,
            carga.IdEntidadFederativa,
            carga.IdUsuarioCarga,
            bloque.AnioSemana,
            bloque.NumeroSemana,
            FechaInicioSemana = bloque.FechaInicioSemana.Date,
            FechaFinSemana = bloque.FechaFinSemana.Date,
            bloque.AnioCorte,
            bloque.MesCorte,
            FechaInicioTramo = bloque.FechaInicioTramo.Date,
            FechaFinTramo = bloque.FechaFinTramo.Date,
            bloque.TotalCarpetas,
            bloque.TotalDelitos,
            bloque.TotalVictimas,
            bloque.ReemplazaInformacion,
            Estado = estadoInicial
        }), transaction);
    }

    private static async Task GuardarConfiguracionCargaAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, List<ConfiguracionModalidadSemanalItem> modalidades)
    {
        const string sql = @"
        INSERT INTO dbo.semanal_carga_delito_configurado
        (
            id_semanal_carga,
            id_modalidad_delito,
            es_obligatorio,
            conservar_entre_periodos,
            orden
        )
        VALUES
        (
            @IdSemanalCarga,
            @IdModalidadDelito,
            @EsObligatorio,
            @ConservarEntrePeriodos,
            @Orden
        );
    ";

        await connection.ExecuteAsync(sql, modalidades.Select(x => new { IdSemanalCarga = idSemanalCarga, x.IdModalidadDelito, x.EsObligatorio, x.ConservarEntrePeriodos, x.Orden }), transaction);
    }

    private static async Task GuardarTmpCarpetasAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, List<SemanalArchivoFilaCarga> filas)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("id_semanal_carga", typeof(long));
        tabla.Columns.Add("numero_fila", typeof(int));
        tabla.Columns.Add("id_ci", typeof(string));
        tabla.Columns.Add("ntra_ci", typeof(string));
        tabla.Columns.Add("fha_de_ini", typeof(string));
        tabla.Columns.Add("hra_de_ini", typeof(string));
        tabla.Columns.Add("rmen_de_hchos", typeof(string));
        tabla.Columns.Add("incluido", typeof(bool));
        tabla.Columns.Add("codigo_exclusion", typeof(string));
        tabla.Columns.Add("estado", typeof(string));
        tabla.Columns.Add("activo", typeof(bool));

        foreach (var item in filas)
        {
            tabla.Rows.Add(idSemanalCarga, item.Fila.NumeroFila ?? 0, ValorTextoStaging(ObtenerValor(item.Fila, "id_ci")), ValorTextoStaging(ObtenerValor(item.Fila, "ntra_ci")), ValorTextoStaging(ObtenerValor(item.Fila, "fha_de_ini")), ValorTextoStaging(ObtenerValor(item.Fila, "hra_de_ini")), ValorTextoStaging(ObtenerValor(item.Fila, "rmen_de_hchos")), item.Incluido, item.CodigoExclusion == null ? DBNull.Value : item.CodigoExclusion, "PENDIENTE", true);
        }

        await EscribirBulkAsync(connection, transaction, "dbo.semanal_carga_tmp_carpeta", tabla);
    }

    private static async Task GuardarTmpDelitosAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, List<SemanalArchivoFilaCarga> filas)
    {
        var columnas = new[] { "id_ci", "id_delito", "dto", "moda_dto", "forma_acc", "fha_de_hchos", "hra_de_hchos", "emto_com_dto", "grdo_cons", "clasf_de_dto", "id_ent_hchos", "id_mun_hchos", "id_loc_hchos", "nom_loc_hchos", "id_col_hchos", "nom_col_hchos", "cp", "coord_x", "coord_y", "dom_hchos" };
        var tabla = CrearTablaTemporal(columnas);

        foreach (var item in filas)
        {
            var valores = new List<object> { idSemanalCarga, item.Fila.NumeroFila ?? 0 };
            valores.AddRange(columnas.Select(columna => (object)ValorTextoStaging(ObtenerValor(item.Fila, columna))));
            valores.Add(item.Incluido);
            valores.Add(item.CodigoExclusion == null ? DBNull.Value : item.CodigoExclusion);
            valores.Add("PENDIENTE");
            valores.Add(true);
            tabla.Rows.Add(valores.ToArray());
        }

        await EscribirBulkAsync(connection, transaction, "dbo.semanal_carga_tmp_delito", tabla);
    }

    private static async Task GuardarTmpVictimasAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, List<SemanalArchivoFilaCarga> filas)
    {
        var columnas = new[] { "id_ci", "id_delito", "id_vicf", "id_tv", "id_tpm", "sexo", "genero", "pob", "disc", "fha_nac", "edad", "nacional" };
        var tabla = CrearTablaTemporal(columnas);

        foreach (var item in filas)
        {
            var valores = new List<object> { idSemanalCarga, item.Fila.NumeroFila ?? 0 };
            valores.AddRange(columnas.Select(columna => (object)ValorTextoStaging(ObtenerValor(item.Fila, columna))));
            valores.Add(item.Incluido);
            valores.Add(item.CodigoExclusion == null ? DBNull.Value : item.CodigoExclusion);
            valores.Add("PENDIENTE");
            valores.Add(true);
            tabla.Rows.Add(valores.ToArray());
        }

        await EscribirBulkAsync(connection, transaction, "dbo.semanal_carga_tmp_victima", tabla);
    }

    private static DataTable CrearTablaTemporal(IEnumerable<string> columnasArchivo)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("id_semanal_carga", typeof(long));
        tabla.Columns.Add("numero_fila", typeof(int));
        foreach (var columna in columnasArchivo) tabla.Columns.Add(columna, typeof(string));
        tabla.Columns.Add("incluido", typeof(bool));
        tabla.Columns.Add("codigo_exclusion", typeof(string));
        tabla.Columns.Add("estado", typeof(string));
        tabla.Columns.Add("activo", typeof(bool));
        return tabla;
    }

    private static async Task EscribirBulkAsync(SqlConnection connection, SqlTransaction transaction, string tablaDestino, DataTable tabla)
    {
        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction) { DestinationTableName = tablaDestino };
        foreach (DataColumn columna in tabla.Columns) bulkCopy.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);
        await bulkCopy.WriteToServerAsync(tabla);
    }

    private static async Task<SemanalCargaConfirmacionInfo?> ObtenerCargaConfirmacionAsync(SqlConnection connection, SqlTransaction transaction, string codigoReferencia, int idUsuarioConfirmacion)
    {
        const string sql = @"
        SELECT
            sc.id_semanal_carga AS IdSemanalCarga,
            sc.id_usuario_carga AS IdUsuarioCarga,
            sc.codigo_referencia AS CodigoReferencia,
            sc.estado AS Estado,
            sc.fecha_expiracion AS FechaExpiracion,
            sc.id_entidad_federativa AS IdEntidadFederativaCarga,
            u.id_entidad_federativa AS IdEntidadFederativaUsuario,
            CONVERT(bit, CASE WHEN r.rol = N'SUPER_USUARIO' THEN 1 ELSE 0 END) AS EsSuperUsuario,
            CONVERT(bit, CASE WHEN um.habilitado = 1 AND um.activo = 1 THEN 1 ELSE 0 END) AS HabilitaSemanal,
            CONVERT(bit, ISNULL(um.habilita_carga, 0)) AS HabilitaCarga,
            CONVERT(bit, ISNULL(um.habilita_modificacion, 0)) AS HabilitaModificacion,
            sc.tipo_carga AS TipoCarga,
            sc.mes_corte AS MesCorte,
            sc.anio_corte AS AnioCorte,
            sc.fecha_inicio_tramo AS FechaInicioTramo,
            sc.fecha_fin_tramo AS FechaFinTramo,
            sc.total_carpetas_incluidas AS TotalCarpetasIncluidas,
            sc.total_delitos_incluidos AS TotalDelitosIncluidos,
            sc.total_victimas_incluidas AS TotalVictimasIncluidas
        FROM dbo.semanal_carga sc WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN dbo.usuario u ON u.id_usuario = @IdUsuarioConfirmacion AND u.activo = 1
        INNER JOIN dbo.roles r ON r.id_rol = u.id_rol AND r.activo = 1
        INNER JOIN dbo.catalogo_modulo m ON m.clave = N'SEMANAL' AND m.activo = 1
        LEFT JOIN dbo.usuario_modulo um ON um.id_usuario = u.id_usuario AND um.id_modulo = m.id_modulo
        WHERE sc.codigo_referencia = @CodigoReferencia
          AND sc.tipo_carga IN (N'CARGA_INICIAL', N'ACTUALIZACION')
          AND sc.activo = 1;
    ";

        return await connection.QueryFirstOrDefaultAsync<SemanalCargaConfirmacionInfo>(sql, new { CodigoReferencia = codigoReferencia, IdUsuarioConfirmacion = idUsuarioConfirmacion }, transaction);
    }

    private static bool EsPeriodoAnteriorAlMesActual(int mesCorte, int anioCorte)
    {
        var fechaActual = DateTime.Today;
        return anioCorte < fechaActual.Year || (anioCorte == fechaActual.Year && mesCorte < fechaActual.Month);
    }

    private static async Task ActualizarCargaExpiradaAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, bool esActualizacion)
    {
        const string sql = @"
        UPDATE dbo.semanal_carga SET estado = @Estado, mensaje_error = N'La operación semanal expiró antes de ser confirmada.' WHERE id_semanal_carga = @IdSemanalCarga;
        UPDATE dbo.semanal_carga_bloque SET estado = @Estado WHERE id_semanal_carga = @IdSemanalCarga AND activo = 1;
        UPDATE dbo.semanal_carga_tmp_carpeta SET estado = @Estado, fecha_procesamiento = SYSDATETIME() WHERE id_semanal_carga = @IdSemanalCarga;
        UPDATE dbo.semanal_carga_tmp_delito SET estado = @Estado, fecha_procesamiento = SYSDATETIME() WHERE id_semanal_carga = @IdSemanalCarga;
        UPDATE dbo.semanal_carga_tmp_victima SET estado = @Estado, fecha_procesamiento = SYSDATETIME() WHERE id_semanal_carga = @IdSemanalCarga;
    ";

        await connection.ExecuteAsync(sql, new { IdSemanalCarga = idSemanalCarga, Estado = esActualizacion ? "EXPIRADO_ACTUALIZACION" : "EXPIRADO" }, transaction);
    }

    private static async Task RechazarCargaAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, int idUsuarioConfirmacion, bool esActualizacion)
    {
        const string sql = @"
        UPDATE dbo.semanal_carga
        SET estado = @Estado, fecha_confirmacion = SYSDATETIME(), id_usuario_confirmacion = @IdUsuarioConfirmacion, mensaje_error = N'La operación semanal fue rechazada por el usuario.'
        WHERE id_semanal_carga = @IdSemanalCarga;

        UPDATE dbo.semanal_carga_bloque
        SET estado = @Estado
        WHERE id_semanal_carga = @IdSemanalCarga
          AND activo = 1;

        UPDATE dbo.semanal_carga_tmp_carpeta SET estado = @Estado, fecha_procesamiento = SYSDATETIME() WHERE id_semanal_carga = @IdSemanalCarga;
        UPDATE dbo.semanal_carga_tmp_delito SET estado = @Estado, fecha_procesamiento = SYSDATETIME() WHERE id_semanal_carga = @IdSemanalCarga;
        UPDATE dbo.semanal_carga_tmp_victima SET estado = @Estado, fecha_procesamiento = SYSDATETIME() WHERE id_semanal_carga = @IdSemanalCarga;
        ";

        await connection.ExecuteAsync(sql, new { IdSemanalCarga = idSemanalCarga, IdUsuarioConfirmacion = idUsuarioConfirmacion, Estado = esActualizacion ? "RECHAZADO_VALIDACION_ACTUALIZACION" : "RECHAZADO_VALIDACION" }, transaction);
    }

    private static async Task EnviarCargaAprobacionAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga)
    {
        const string sql = @"
        UPDATE dbo.semanal_carga SET estado = N'PENDIENTE_APROBACION', fecha_expiracion = NULL, mensaje_error = NULL WHERE id_semanal_carga = @IdSemanalCarga;
        UPDATE dbo.semanal_carga_bloque SET estado = N'PENDIENTE_APROBACION' WHERE id_semanal_carga = @IdSemanalCarga AND activo = 1;
        UPDATE dbo.semanal_carga_tmp_carpeta SET estado = N'PENDIENTE_APROBACION', fecha_procesamiento = NULL WHERE id_semanal_carga = @IdSemanalCarga;
        UPDATE dbo.semanal_carga_tmp_delito SET estado = N'PENDIENTE_APROBACION', fecha_procesamiento = NULL WHERE id_semanal_carga = @IdSemanalCarga;
        UPDATE dbo.semanal_carga_tmp_victima SET estado = N'PENDIENTE_APROBACION', fecha_procesamiento = NULL WHERE id_semanal_carga = @IdSemanalCarga;
        ";

        await connection.ExecuteAsync(sql, new { IdSemanalCarga = idSemanalCarga }, transaction);
    }

    private static async Task RechazarCargaAdministracionAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, int idUsuarioRechazo, string motivo)
    {
        const string sql = @"
        UPDATE dbo.semanal_carga
        SET estado = N'RECHAZADO_ADMIN',
            fecha_confirmacion = SYSDATETIME(),
            fecha_expiracion = NULL,
            id_usuario_confirmacion = @IdUsuarioRechazo,
            mensaje_error = @Motivo
        WHERE id_semanal_carga = @IdSemanalCarga;

        UPDATE dbo.semanal_carga_bloque
        SET estado = N'RECHAZADO_ADMIN'
        WHERE id_semanal_carga = @IdSemanalCarga
          AND activo = 1;

        UPDATE dbo.semanal_carga_tmp_carpeta
        SET estado = N'RECHAZADO_ADMIN',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_semanal_carga = @IdSemanalCarga;

        UPDATE dbo.semanal_carga_tmp_delito
        SET estado = N'RECHAZADO_ADMIN',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_semanal_carga = @IdSemanalCarga;

        UPDATE dbo.semanal_carga_tmp_victima
        SET estado = N'RECHAZADO_ADMIN',
            fecha_procesamiento = SYSDATETIME()
        WHERE id_semanal_carga = @IdSemanalCarga;
    ";

        await connection.ExecuteAsync(sql, new { IdSemanalCarga = idSemanalCarga, IdUsuarioRechazo = idUsuarioRechazo, Motivo = motivo }, transaction);
    }

    private static async Task PrepararReemplazoBloquesAsync(SqlConnection connection, SqlTransaction transaction, SemanalCargaConfirmacionInfo carga, int idUsuarioModificacion)
    {
        const string sql = @"
        SELECT DISTINCT
            ci.id_semanal_carpeta_investigacion
        INTO #CarpetasVersionAnterior
        FROM dbo.semanal_carpeta_investigacion ci WITH (UPDLOCK, HOLDLOCK)
        INNER JOIN dbo.semanal_carga sc
            ON sc.id_semanal_carga = ci.id_semanal_carga
        INNER JOIN dbo.semanal_carga_bloque bloque
            ON bloque.id_semanal_carga = @IdSemanalCargaNueva
           AND bloque.id_entidad_federativa = @IdEntidadFederativa
           AND bloque.reemplaza_informacion = 1
           AND bloque.activo = 1
           AND ci.fecha_inicio >= bloque.fecha_inicio_tramo
           AND ci.fecha_inicio < DATEADD(DAY, 1, bloque.fecha_fin_tramo)
        WHERE sc.id_entidad_federativa = @IdEntidadFederativa
          AND sc.id_usuario_carga = @IdUsuarioCarga
          AND ci.activo = 1
          AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
          AND sc.activo = 1;

        IF EXISTS
        (
            SELECT 1
            FROM dbo.semanal_carga_bloque bloque
            WHERE bloque.id_semanal_carga = @IdSemanalCargaNueva
              AND bloque.id_entidad_federativa = @IdEntidadFederativa
              AND bloque.reemplaza_informacion = 1
              AND bloque.activo = 1
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM dbo.semanal_carpeta_investigacion ci WITH (UPDLOCK, HOLDLOCK)
                  INNER JOIN dbo.semanal_carga sc
                      ON sc.id_semanal_carga = ci.id_semanal_carga
                    WHERE sc.id_entidad_federativa = bloque.id_entidad_federativa
                      AND sc.id_usuario_carga = @IdUsuarioCarga
                      AND ci.fecha_inicio >= bloque.fecha_inicio_tramo
                    AND ci.fecha_inicio < DATEADD(DAY, 1, bloque.fecha_fin_tramo)
                    AND ci.activo = 1
                    AND sc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')
                    AND sc.activo = 1
              )
        )
        BEGIN
            THROW 50040, 'Uno o más bloques marcados para reemplazo no tienen información semanal confirmada activa.', 1;
        END;

        SELECT d.id_semanal_delito
        INTO #DelitosVersionAnterior
        FROM dbo.semanal_delito d
        INNER JOIN #CarpetasVersionAnterior c ON c.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
        WHERE d.activo = 1;

        SELECT v.id_semanal_victima
        INTO #VictimasVersionAnterior
        FROM dbo.semanal_victima v
        INNER JOIN #DelitosVersionAnterior d ON d.id_semanal_delito = v.id_semanal_delito
        WHERE v.activo = 1;

        INSERT INTO dbo.semanal_victima_historico
        (
            id_semanal_victima,
            id_semanal_delito,
            id_semanal_carga,
            identificador_victima_fiscalia,
            id_tipo_victima,
            id_tipo_victima_moral,
            id_sexo,
            id_genero,
            id_nacionalidad,
            id_pertenece_poblacion_indigena,
            id_presenta_discapacidad,
            fecha_nacimiento,
            edad,
            id_usuario_registro,
            fecha_registro,
            id_usuario_modificacion,
            id_semanal_carga_nueva,
            tipo_movimiento,
            fecha_modificacion,
            activo
        )
        SELECT
            v.id_semanal_victima,
            v.id_semanal_delito,
            v.id_semanal_carga,
            v.identificador_victima_fiscalia,
            v.id_tipo_victima,
            v.id_tipo_victima_moral,
            v.id_sexo,
            v.id_genero,
            v.id_nacionalidad,
            v.id_pertenece_poblacion_indigena,
            v.id_presenta_discapacidad,
            v.fecha_nacimiento,
            v.edad,
            v.id_usuario_registro,
            v.fecha_registro,
            @IdUsuarioModificacion,
            @IdSemanalCargaNueva,
            CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.semanal_delito d
                INNER JOIN dbo.semanal_carpeta_investigacion ci ON ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
                INNER JOIN dbo.semanal_carga_tmp_victima nv
                    ON nv.id_semanal_carga = @IdSemanalCargaNueva
                   AND nv.id_ci = ci.identificador_carpeta_fiscalia
                   AND nv.id_delito = d.identificador_delito_fiscalia
                   AND nv.id_vicf = v.identificador_victima_fiscalia
                   AND nv.incluido = 1
                   AND nv.activo = 1
                WHERE d.id_semanal_delito = v.id_semanal_delito
            ) THEN N'MODIFICADO' ELSE N'ELIMINADO' END,
            SYSDATETIME(),
            1
        FROM dbo.semanal_victima v
        INNER JOIN #VictimasVersionAnterior objetivo ON objetivo.id_semanal_victima = v.id_semanal_victima;

        INSERT INTO dbo.semanal_delito_historico
        (
            id_semanal_delito,
            id_semanal_carpeta_investigacion,
            id_semanal_carga,
            identificador_delito_fiscalia,
            delito_fiscalia,
            modalidad_delito_fiscalia,
            id_catalogo_delito,
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
            coordenada_x,
            coordenada_y,
            domicilio_hechos,
            id_usuario_registro,
            fecha_registro,
            id_usuario_modificacion,
            id_semanal_carga_nueva,
            tipo_movimiento,
            fecha_modificacion,
            activo
        )
        SELECT
            d.id_semanal_delito,
            d.id_semanal_carpeta_investigacion,
            d.id_semanal_carga,
            d.identificador_delito_fiscalia,
            d.delito_fiscalia,
            d.modalidad_delito_fiscalia,
            d.id_catalogo_delito,
            d.id_forma_accion,
            d.fecha_hechos,
            d.id_instrumento_comision,
            d.id_grado_consumacion,
            d.id_modalidad_delito,
            d.id_entidad_federativa,
            d.id_municipio,
            d.id_localidad_fiscalia,
            d.localidad_fiscalia_nombre,
            d.id_colonia_fiscalia,
            d.colonia_fiscalia_nombre,
            d.id_codigo_postal,
            d.coordenada_x,
            d.coordenada_y,
            d.domicilio_hechos,
            d.id_usuario_registro,
            d.fecha_registro,
            @IdUsuarioModificacion,
            @IdSemanalCargaNueva,
            CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.semanal_carpeta_investigacion ci
                INNER JOIN dbo.semanal_carga_tmp_delito nd
                    ON nd.id_semanal_carga = @IdSemanalCargaNueva
                   AND nd.id_ci = ci.identificador_carpeta_fiscalia
                   AND nd.id_delito = d.identificador_delito_fiscalia
                   AND nd.incluido = 1
                   AND nd.activo = 1
                WHERE ci.id_semanal_carpeta_investigacion = d.id_semanal_carpeta_investigacion
            ) THEN N'MODIFICADO' ELSE N'ELIMINADO' END,
            SYSDATETIME(),
            1
        FROM dbo.semanal_delito d
        INNER JOIN #DelitosVersionAnterior objetivo ON objetivo.id_semanal_delito = d.id_semanal_delito;

        INSERT INTO dbo.semanal_carpeta_investigacion_historico
        (
            id_semanal_carpeta_investigacion,
            id_semanal_carga,
            identificador_carpeta_fiscalia,
            nomenclatura_carpeta_fiscalia,
            fecha_inicio,
            resumen_hechos,
            id_usuario_registro,
            fecha_registro,
            id_usuario_modificacion,
            id_semanal_carga_nueva,
            tipo_movimiento,
            fecha_modificacion,
            activo
        )
        SELECT
            ci.id_semanal_carpeta_investigacion,
            ci.id_semanal_carga,
            ci.identificador_carpeta_fiscalia,
            ci.nomenclatura_carpeta_fiscalia,
            ci.fecha_inicio,
            ci.resumen_hechos,
            ci.id_usuario_registro,
            ci.fecha_registro,
            @IdUsuarioModificacion,
            @IdSemanalCargaNueva,
            CASE WHEN EXISTS
            (
                SELECT 1
                FROM dbo.semanal_carga_tmp_carpeta nc
                WHERE nc.id_semanal_carga = @IdSemanalCargaNueva
                  AND nc.id_ci = ci.identificador_carpeta_fiscalia
                  AND nc.incluido = 1
                  AND nc.activo = 1
            ) THEN N'MODIFICADO' ELSE N'ELIMINADO' END,
            SYSDATETIME(),
            1
        FROM dbo.semanal_carpeta_investigacion ci
        INNER JOIN #CarpetasVersionAnterior objetivo ON objetivo.id_semanal_carpeta_investigacion = ci.id_semanal_carpeta_investigacion;

        UPDATE v
        SET activo = 0
        FROM dbo.semanal_victima v
        INNER JOIN #VictimasVersionAnterior objetivo ON objetivo.id_semanal_victima = v.id_semanal_victima;

        UPDATE d
        SET activo = 0
        FROM dbo.semanal_delito d
        INNER JOIN #DelitosVersionAnterior objetivo ON objetivo.id_semanal_delito = d.id_semanal_delito;

        UPDATE ci
        SET activo = 0
        FROM dbo.semanal_carpeta_investigacion ci
        INNER JOIN #CarpetasVersionAnterior objetivo ON objetivo.id_semanal_carpeta_investigacion = ci.id_semanal_carpeta_investigacion;
    ";

        await connection.ExecuteAsync(sql, new
        {
            IdSemanalCargaNueva = carga.IdSemanalCarga,
            IdEntidadFederativa = carga.IdEntidadFederativaCarga,
            IdUsuarioCarga = carga.IdUsuarioCarga,
            IdUsuarioModificacion = idUsuarioModificacion
        }, transaction);
    }

    private static async Task<int> InsertarCarpetasFinalesAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, int idUsuarioRegistro)
    {
        const string sql = @"
        INSERT INTO dbo.semanal_carpeta_investigacion
        (
            id_semanal_carga,
            identificador_carpeta_fiscalia,
            nomenclatura_carpeta_fiscalia,
            fecha_inicio,
            resumen_hechos,
            id_usuario_registro,
            activo
        )
        SELECT
            c.id_semanal_carga,
            c.id_ci,
            c.ntra_ci,
            COALESCE(
                TRY_CONVERT(datetime2, CONCAT(c.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(c.hra_de_ini)), '')), 103),
                TRY_CONVERT(datetime2, CONCAT(c.fha_de_ini, ' ', NULLIF(LTRIM(RTRIM(c.hra_de_ini)), ''))),
                CASE
                    WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), ''), ',', '.')) >= 0 AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), ''), ',', '.')) < 1
                    THEN DATEADD(SECOND, CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), ''), ',', '.')) * 86400, 0)), COALESCE(TRY_CONVERT(datetime2, c.fha_de_ini, 103), TRY_CONVERT(datetime2, c.fha_de_ini)))
                END,
                TRY_CONVERT(datetime2, c.fha_de_ini, 103),
                TRY_CONVERT(datetime2, c.fha_de_ini)
            ),
            c.rmen_de_hchos,
            @IdUsuarioRegistro,
            1
        FROM dbo.semanal_carga_tmp_carpeta c
        WHERE c.id_semanal_carga = @IdSemanalCarga
          AND c.incluido = 1
          AND c.activo = 1;
    ";

        return await connection.ExecuteAsync(sql, new { IdSemanalCarga = idSemanalCarga, IdUsuarioRegistro = idUsuarioRegistro }, transaction);
    }

    private static async Task<int> InsertarDelitosFinalesAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, int idUsuarioRegistro)
    {
        const string sql = @"
        INSERT INTO dbo.semanal_delito
        (
            id_semanal_carpeta_investigacion,
            id_semanal_carga,
            identificador_delito_fiscalia,
            delito_fiscalia,
            modalidad_delito_fiscalia,
            id_catalogo_delito,
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
            coordenada_x,
            coordenada_y,
            domicilio_hechos,
            id_usuario_registro,
            activo
        )
        SELECT
            ci.id_semanal_carpeta_investigacion,
            d.id_semanal_carga,
            d.id_delito,
            d.dto,
            d.moda_dto,
            sd.id_delito,
            fa.id_forma_accion,
            COALESCE(
                TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), '')), 103),
                TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, ' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''))),
                CASE
                    WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) >= 0 AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) < 1
                    THEN DATEADD(SECOND, CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), ''), ',', '.')) * 86400, 0)), COALESCE(TRY_CONVERT(datetime2, d.fha_de_hchos, 103), TRY_CONVERT(datetime2, d.fha_de_hchos)))
                END,
                TRY_CONVERT(datetime2, d.fha_de_hchos, 103),
                TRY_CONVERT(datetime2, d.fha_de_hchos)
            ),
            ic.id_instrumento_comision,
            gc.id_grado_consumacion,
            md.id_modalidad_delito,
            ef.id_entidad_federativa,
            mun.id_municipio,
            d.id_loc_hchos,
            d.nom_loc_hchos,
            d.id_col_hchos,
            d.nom_col_hchos,
            cp.id_codigo_postal,
            TRY_CONVERT(decimal(10,6), NULLIF(d.coord_x, '')),
            TRY_CONVERT(decimal(10,6), NULLIF(d.coord_y, '')),
            d.dom_hchos,
            @IdUsuarioRegistro,
            1
        FROM dbo.semanal_carga_tmp_delito d
        INNER JOIN dbo.semanal_carpeta_investigacion ci ON ci.id_semanal_carga = d.id_semanal_carga AND ci.identificador_carpeta_fiscalia = d.id_ci AND ci.activo = 1
        INNER JOIN dbo.catalogo_modalidad_delito md ON md.clave4 = d.clasf_de_dto AND md.activo = 1
        INNER JOIN dbo.catalogo_subtipo_delito sd ON sd.id_subtipo_delito = md.id_subtipo_delito AND sd.activo = 1
        INNER JOIN dbo.semanal_carga_delito_configurado configuracion ON configuracion.id_semanal_carga = d.id_semanal_carga AND configuracion.id_modalidad_delito = md.id_modalidad_delito
        INNER JOIN dbo.catalogo_forma_accion fa ON fa.clave = TRY_CONVERT(tinyint, d.forma_acc) AND fa.activo = 1
        INNER JOIN dbo.catalogo_instrumento_comision ic ON ic.clave = TRY_CONVERT(tinyint, d.emto_com_dto) AND ic.activo = 1
        INNER JOIN dbo.catalogo_grado_consumacion gc ON gc.clave = TRY_CONVERT(tinyint, d.grdo_cons) AND gc.activo = 1
        INNER JOIN dbo.catalogo_entidad_federativa ef ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos) AND ef.activo = 1
        INNER JOIN dbo.catalogo_municipio mun ON mun.id_entidad_federativa = ef.id_entidad_federativa AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos) AND mun.activo = 1
        OUTER APPLY
        (
            SELECT TOP (1) ccp.id_codigo_postal
            FROM dbo.catalogo_codigo_postal ccp
            WHERE ccp.codigo_postal = RIGHT('00000' + LTRIM(RTRIM(d.cp)), 5)
              AND ccp.id_municipio = mun.id_municipio
              AND ccp.activo = 1
            ORDER BY ccp.id_codigo_postal
        ) cp
        WHERE d.id_semanal_carga = @IdSemanalCarga
          AND d.incluido = 1
          AND d.activo = 1;
    ";

        return await connection.ExecuteAsync(sql, new { IdSemanalCarga = idSemanalCarga, IdUsuarioRegistro = idUsuarioRegistro }, transaction);
    }

    private static async Task<int> InsertarVictimasFinalesAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, int idUsuarioRegistro)
    {
        const string sql = @"
        INSERT INTO dbo.semanal_victima
        (
            id_semanal_delito,
            id_semanal_carga,
            identificador_victima_fiscalia,
            id_tipo_victima,
            id_tipo_victima_moral,
            id_sexo,
            id_genero,
            id_nacionalidad,
            id_pertenece_poblacion_indigena,
            id_presenta_discapacidad,
            fecha_nacimiento,
            edad,
            id_usuario_registro,
            activo
        )
        SELECT
            d.id_semanal_delito,
            v.id_semanal_carga,
            v.id_vicf,
            tv.id_tipo_victima,
            tvm.id_tipo_victima_moral,
            sx.id_sexo,
            gen.id_genero,
            nac.id_nacionalidad,
            pob.id_pertenece_poblacion_indigena,
            disc.id_presenta_discapacidad,
            COALESCE(TRY_CONVERT(date, NULLIF(v.fha_nac, ''), 103), TRY_CONVERT(date, NULLIF(v.fha_nac, ''))),
            CASE WHEN TRY_CONVERT(int, NULLIF(v.edad, '')) = 999 THEN NULL ELSE TRY_CONVERT(tinyint, NULLIF(v.edad, '')) END,
            @IdUsuarioRegistro,
            1
        FROM dbo.semanal_carga_tmp_victima v
        INNER JOIN dbo.semanal_carpeta_investigacion ci ON ci.id_semanal_carga = v.id_semanal_carga AND ci.identificador_carpeta_fiscalia = v.id_ci AND ci.activo = 1
        INNER JOIN dbo.semanal_delito d ON d.id_semanal_carga = v.id_semanal_carga AND d.id_semanal_carpeta_investigacion = ci.id_semanal_carpeta_investigacion AND d.identificador_delito_fiscalia = v.id_delito AND d.activo = 1
        INNER JOIN dbo.catalogo_tipo_victima tv ON tv.clave = TRY_CONVERT(tinyint, v.id_tv) AND tv.activo = 1
        LEFT JOIN dbo.catalogo_tipo_victima_moral tvm ON tvm.clave = TRY_CONVERT(tinyint, NULLIF(v.id_tpm, '')) AND tvm.activo = 1
        LEFT JOIN dbo.catalogo_sexo sx ON sx.clave = TRY_CONVERT(tinyint, NULLIF(v.sexo, '')) AND sx.activo = 1
        LEFT JOIN dbo.catalogo_genero gen ON gen.clave = TRY_CONVERT(tinyint, NULLIF(v.genero, '')) AND gen.activo = 1
        LEFT JOIN dbo.catalogo_nacionalidad nac ON TRY_CONVERT(int, nac.clave) = TRY_CONVERT(int, NULLIF(v.nacional, '')) AND nac.activo = 1
        LEFT JOIN dbo.catalogo_pertenece_poblacion_indigena pob ON pob.clave = TRY_CONVERT(tinyint, NULLIF(v.pob, '')) AND pob.activo = 1
        LEFT JOIN dbo.catalogo_presenta_discapacidad disc ON disc.clave = TRY_CONVERT(tinyint, NULLIF(v.disc, '')) AND disc.activo = 1
        WHERE v.id_semanal_carga = @IdSemanalCarga
          AND v.incluido = 1
          AND v.activo = 1;
    ";

        return await connection.ExecuteAsync(sql, new { IdSemanalCarga = idSemanalCarga, IdUsuarioRegistro = idUsuarioRegistro }, transaction);
    }

    private static async Task ConfirmarCargaFinalAsync(SqlConnection connection, SqlTransaction transaction, long idSemanalCarga, int idUsuarioConfirmacion, bool esActualizacion)
    {
        const string sql = @"
        UPDATE dbo.semanal_carga
        SET estado = @EstadoConfirmado, fecha_confirmacion = SYSDATETIME(), fecha_expiracion = NULL, id_usuario_confirmacion = @IdUsuarioConfirmacion, mensaje_error = NULL
        WHERE id_semanal_carga = @IdSemanalCarga;

        UPDATE dbo.semanal_carga_bloque
        SET estado = @EstadoConfirmado
        WHERE id_semanal_carga = @IdSemanalCarga
          AND activo = 1;

        UPDATE dbo.semanal_carga_tmp_carpeta SET estado = N'PROCESADO', fecha_procesamiento = SYSDATETIME() WHERE id_semanal_carga = @IdSemanalCarga;
        UPDATE dbo.semanal_carga_tmp_delito SET estado = N'PROCESADO', fecha_procesamiento = SYSDATETIME() WHERE id_semanal_carga = @IdSemanalCarga;
        UPDATE dbo.semanal_carga_tmp_victima SET estado = N'PROCESADO', fecha_procesamiento = SYSDATETIME() WHERE id_semanal_carga = @IdSemanalCarga;
        ";

        await connection.ExecuteAsync(sql, new { IdSemanalCarga = idSemanalCarga, IdUsuarioConfirmacion = idUsuarioConfirmacion, EstadoConfirmado = esActualizacion ? "CONFIRMADO_ACTUALIZACION" : "CONFIRMADO" }, transaction);
    }

    private static string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }

    private static string ValorTextoStaging(string? valor) => string.IsNullOrWhiteSpace(valor) ? string.Empty : valor.Trim();

    private static ConfirmarCargaResponse Error(string codigoReferencia, string estado, string mensaje) => new() { EsValido = false, CodigoReferencia = codigoReferencia, Estado = estado, Mensaje = mensaje };

    private static ConfirmarCargaResponse Exito(string codigoReferencia, string estado, string mensaje) => new() { EsValido = true, CodigoReferencia = codigoReferencia, Estado = estado, Mensaje = mensaje };
}
