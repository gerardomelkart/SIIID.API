namespace SIIID2.Api.Repositories;
// Se leen definitivos aceptados, nunca staging ni cargas pendientes de aprobación.
internal static class BanciMensualSql
{
    // La misma consulta alimenta la pantalla y valida Excel/manual, sin crear objetos en la BD.
    public const string Motivos = """
        SELECT c.clave AS condicion,CONVERT(nvarchar(1000),cd.clave2) AS clave,
            CONVERT(nvarchar(1000),cd.delito) AS descripcion
        FROM dbo.banci_catalogo_constitutiva_delito c CROSS JOIN dbo.catalogo_delito cd
        WHERE c.activo=1 AND c.codigo_regla=N'DELITO' AND cd.activo=1
        UNION ALL
        SELECT c.clave,CONVERT(nvarchar(1000),md.clave4),
            CONVERT(nvarchar(1000),CONCAT(bj.bien_juridico,N' / ',cd.delito,N' / ',sd.subtipo_delito,N' / ',md.modalidad_delito))
        FROM dbo.banci_catalogo_constitutiva_delito c CROSS JOIN dbo.catalogo_modalidad_delito md
        JOIN dbo.catalogo_subtipo_delito sd ON sd.id_subtipo_delito=md.id_subtipo_delito AND sd.activo=1
        JOIN dbo.catalogo_delito cd ON cd.id_delito=sd.id_delito AND cd.activo=1
        JOIN dbo.catalogo_bien_juridico bj ON bj.id_bien_juridico=cd.id_bien_juridico AND bj.activo=1
        WHERE c.activo=1 AND c.codigo_regla=N'DELITO' AND md.activo=1
        UNION ALL
        SELECT c.clave,CONVERT(nvarchar(1000),m.clave),m.descripcion
        FROM dbo.banci_catalogo_constitutiva_delito c CROSS JOIN dbo.banci_catalogo_motivo_no_delito m
        WHERE c.activo=1 AND c.codigo_regla=N'NO_DELITO' AND m.activo=1
        """;
    public const string Origen = """
        SELECT ci.identificador_carpeta_fiscalia AS id_ci, ci.nomenclatura_carpeta_fiscalia AS ntra_ci,
            CONVERT(varchar(10),ci.fecha_inicio,23) AS fha_de_ini, CONVERT(varchar(8),ci.fecha_inicio,108) AS hra_de_ini,
            ci.resumen_hechos AS rmen_de_hchos
        FROM dbo.carpeta_investigacion ci JOIN dbo.carga c ON c.id_carga=ci.id_carga
        WHERE c.id_entidad_federativa=@Entidad AND c.mes_corte=@Mes AND c.anio_corte=@Anio AND c.activo=1
          AND c.estado IN ('CONFIRMADO','CONFIRMADO_ACTUALIZACION') AND ci.activo=1
          AND EXISTS(SELECT 1 FROM dbo.delito d JOIN dbo.catalogo_modalidad_delito md ON md.id_modalidad_delito=d.id_modalidad_delito
            JOIN dbo.banci_catalogo_clasificacion_delito bc ON bc.clave=md.clave4 AND bc.activo=1
            WHERE d.id_carpeta_investigacion=ci.id_carpeta_investigacion AND d.activo=1)
        ORDER BY ci.identificador_carpeta_fiscalia;
        SELECT ci.identificador_carpeta_fiscalia AS id_ci,d.identificador_delito_fiscalia AS id_delito,
            d.delito_fiscalia AS dto,d.modalidad_delito_fiscalia AS moda_dto,fa.clave AS forma_acc,
            CONVERT(varchar(10),d.fecha_hechos,23) AS fha_de_hchos,CONVERT(varchar(8),d.fecha_hechos,108) AS hra_de_hchos,
            ic.clave AS emto_com_dto,gc.clave AS grdo_cons,md.clave4 AS clasf_de_dto,
            ef.nombre AS nom_ent_hchos,ef.id_entidad_federativa AS id_ent_hchos,mu.nombre AS nom_mun_hchos,mu.clave AS id_mun_hchos,
            d.localidad_fiscalia_nombre AS nom_loc_hchos,d.id_localidad_fiscalia AS id_loc_hchos,
            d.colonia_fiscalia_nombre AS nom_col_hchos,d.id_colonia_fiscalia AS id_col_hchos,COALESCE(d.codigo_postal_fiscalia,cp.codigo_postal) AS cp,
            d.coordenada_x AS coord_x,d.coordenada_y AS coord_y,d.domicilio_hechos AS dom_hchos
        FROM dbo.delito d JOIN dbo.carpeta_investigacion ci ON ci.id_carpeta_investigacion=d.id_carpeta_investigacion AND ci.activo=1
        JOIN dbo.carga c ON c.id_carga=ci.id_carga AND c.activo=1
        JOIN dbo.catalogo_modalidad_delito md ON md.id_modalidad_delito=d.id_modalidad_delito
        JOIN dbo.banci_catalogo_clasificacion_delito bc ON bc.clave=md.clave4 AND bc.activo=1
        JOIN dbo.catalogo_forma_accion fa ON fa.id_forma_accion=d.id_forma_accion
        JOIN dbo.catalogo_instrumento_comision ic ON ic.id_instrumento_comision=d.id_instrumento_comision
        JOIN dbo.catalogo_grado_consumacion gc ON gc.id_grado_consumacion=d.id_grado_consumacion
        JOIN dbo.catalogo_entidad_federativa ef ON ef.id_entidad_federativa=d.id_entidad_federativa
        JOIN dbo.catalogo_municipio mu ON mu.id_municipio=d.id_municipio
        LEFT JOIN dbo.catalogo_codigo_postal cp ON cp.id_codigo_postal=d.id_codigo_postal
        WHERE c.id_entidad_federativa=@Entidad AND c.mes_corte=@Mes AND c.anio_corte=@Anio
          AND c.estado IN ('CONFIRMADO','CONFIRMADO_ACTUALIZACION') AND d.activo=1
        ORDER BY ci.identificador_carpeta_fiscalia,d.identificador_delito_fiscalia;
        SELECT ci.identificador_carpeta_fiscalia AS id_ci,d.identificador_delito_fiscalia AS id_delito,
            v.identificador_victima_fiscalia AS id_vicf,tv.clave AS id_tv,tvm.clave AS id_tpm,s.clave AS sexo,g.clave AS genero,
            p.clave AS pob,di.clave AS disc,CONVERT(varchar(10),v.fecha_nacimiento,23) AS fha_nac,v.edad,n.clave AS nacional,
            v.nombre_vicfem,v.primer_apellido_vicfem AS [1apellido_vicfem],v.segundo_apellido_vicfem AS [2apellido_vicfem],v.curp_vicfem
        FROM dbo.victima v JOIN dbo.delito d ON d.id_delito=v.id_delito AND d.activo=1
        JOIN dbo.carpeta_investigacion ci ON ci.id_carpeta_investigacion=d.id_carpeta_investigacion AND ci.activo=1
        JOIN dbo.carga c ON c.id_carga=ci.id_carga AND c.activo=1
        JOIN dbo.catalogo_modalidad_delito md ON md.id_modalidad_delito=d.id_modalidad_delito
        JOIN dbo.banci_catalogo_clasificacion_delito bc ON bc.clave=md.clave4 AND bc.activo=1
        JOIN dbo.catalogo_tipo_victima tv ON tv.id_tipo_victima=v.id_tipo_victima
        LEFT JOIN dbo.catalogo_tipo_victima_moral tvm ON tvm.id_tipo_victima_moral=v.id_tipo_victima_moral
        LEFT JOIN dbo.catalogo_sexo s ON s.id_sexo=v.id_sexo LEFT JOIN dbo.catalogo_genero g ON g.id_genero=v.id_genero
        LEFT JOIN dbo.catalogo_pertenece_poblacion_indigena p ON p.id_pertenece_poblacion_indigena=v.id_pertenece_poblacion_indigena
        LEFT JOIN dbo.catalogo_presenta_discapacidad di ON di.id_presenta_discapacidad=v.id_presenta_discapacidad
        LEFT JOIN dbo.catalogo_nacionalidad n ON n.id_nacionalidad=v.id_nacionalidad
        WHERE c.id_entidad_federativa=@Entidad AND c.mes_corte=@Mes AND c.anio_corte=@Anio
          AND c.estado IN ('CONFIRMADO','CONFIRMADO_ACTUALIZACION') AND v.activo=1
        ORDER BY ci.identificador_carpeta_fiscalia,d.identificador_delito_fiscalia,v.identificador_victima_fiscalia;
        """;
    public const string Operaciones = """
        SELECT b.id_banci_carga AS Id,b.codigo_referencia AS Referencia,b.id_entidad_federativa AS Entidad,
            b.mes_corte AS Mes,b.anio_corte AS Anio,b.tipo_carga AS Tipo,b.estado AS Estado,
            b.id_usuario_carga AS Usuario,u.usuario AS NombreUsuario,ef.nombre AS EntidadNombre,
            b.snapshot_origen AS Snapshot,b.huella_origen AS Huella,b.fecha_validacion AS Fecha,b.motivo_resolucion AS Motivo
        FROM dbo.banci_carga b JOIN dbo.usuario u ON u.id_usuario=b.id_usuario_carga
        JOIN dbo.catalogo_entidad_federativa ef ON ef.id_entidad_federativa=b.id_entidad_federativa
        """;
}
