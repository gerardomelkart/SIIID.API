using Dapper;
using Microsoft.Data.SqlClient;
using SIIID2.Api.Models;

namespace SIIID2.Api.Repositories;

public partial class FederalActualizacionRepository
{
    private sealed class FederalActualizacionConfirmacionInfo
    {
        public long IdFederalCarga { get; set; }
        public string Estado { get; set; } = string.Empty;
        public DateTime? FechaExpiracion { get; set; }
        public int IdUsuarioCarga { get; set; }
        public bool EsSuperUsuario { get; set; }
        public bool HabilitaModificacion { get; set; }
    }

    public async Task<ConfirmarCargaResponse> ConfirmarAsync(string codigoReferencia, bool aceptar, int idUsuarioConfirmacion)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerActualizacionConfirmacionAsync(connection, transaction, codigoReferencia, idUsuarioConfirmacion);
            if (carga == null) return await CancelarAsync(transaction, Respuesta(false, codigoReferencia, "NO_ENCONTRADA", "No se encontró una actualización federal válida para continuar."));
            if (carga.Estado != "VALIDADO_PENDIENTE_ACTUALIZACION") return await CancelarAsync(transaction, Respuesta(false, codigoReferencia, carga.Estado, "La actualización federal no se encuentra pendiente de decisión del usuario."));
            if (carga.IdUsuarioCarga != idUsuarioConfirmacion) return await CancelarAsync(transaction, Respuesta(false, codigoReferencia, carga.Estado, "Solo el usuario que realizó la actualización federal puede aceptar o rechazarla."));

            if (carga.FechaExpiracion.HasValue && carga.FechaExpiracion.Value < DateTime.Now)
            {
                await CambiarEstadoAsync(connection, transaction, carga.IdFederalCarga, "EXPIRADO", idUsuarioConfirmacion, "La actualización federal expiró antes de ser confirmada.");
                await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(connection, transaction, carga.IdFederalCarga, carga.Estado, "EXPIRADO", idUsuarioConfirmacion, "La actualización federal expiró antes de que el usuario tomara una decisión.");
                await transaction.CommitAsync();
                return Respuesta(false, codigoReferencia, "EXPIRADO", "La actualización federal ya expiró. Debe validar nuevamente los archivos.");
            }

            if (!carga.HabilitaModificacion) return await CancelarAsync(transaction, Respuesta(false, codigoReferencia, carga.Estado, "El usuario no tiene habilitada la actualización de información federal."));

            if (!aceptar)
            {
                await CambiarEstadoAsync(connection, transaction, carga.IdFederalCarga, "RECHAZADO_VALIDACION_ACTUALIZACION", idUsuarioConfirmacion, "La actualización federal fue rechazada por el usuario.");
                await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(connection, transaction, carga.IdFederalCarga, carga.Estado, "RECHAZADO_VALIDACION_ACTUALIZACION", idUsuarioConfirmacion, "El usuario rechazó la actualización federal después de revisar las diferencias y el informe previo.");
                await transaction.CommitAsync();
                return Respuesta(true, codigoReferencia, "RECHAZADO_VALIDACION_ACTUALIZACION", "La actualización federal fue rechazada correctamente.");
            }

            await FederalCargaAuditoriaSql.MarcarAdvertenciasAceptadasAsync(connection, transaction, carga.IdFederalCarga, idUsuarioConfirmacion);

            if (!carga.EsSuperUsuario)
            {
                await CambiarEstadoAsync(connection, transaction, carga.IdFederalCarga, "PENDIENTE_APROBACION", null, null);
                await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(connection, transaction, carga.IdFederalCarga, carga.Estado, "PENDIENTE_APROBACION", idUsuarioConfirmacion, "FGR aceptó la actualización federal y la envió a revisión administrativa.");
                await transaction.CommitAsync();
                return Respuesta(true, codigoReferencia, "PENDIENTE_APROBACION", "La actualización federal fue enviada correctamente a revisión administrativa.");
            }

            await AplicarActualizacionAsync(connection, transaction, carga.IdFederalCarga, carga.IdUsuarioCarga, idUsuarioConfirmacion);
            await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(connection, transaction, carga.IdFederalCarga, carga.Estado, "CONFIRMADO_ACTUALIZACION", idUsuarioConfirmacion, "Actualización federal confirmada directamente por un superusuario.");
            await transaction.CommitAsync();
            return Respuesta(true, codigoReferencia, "CONFIRMADO_ACTUALIZACION", "La actualización federal fue confirmada correctamente.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ConfirmarCargaResponse> AprobarAsync(string codigoReferencia, int idUsuarioAprobacion)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerActualizacionConfirmacionAsync(connection, transaction, codigoReferencia, idUsuarioAprobacion);
            if (carga == null) return await CancelarAsync(transaction, Respuesta(false, codigoReferencia, "NO_ENCONTRADA", "No se encontró la actualización federal pendiente de aprobación."));
            if (!carga.EsSuperUsuario) return await CancelarAsync(transaction, Respuesta(false, codigoReferencia, carga.Estado, "Solo un superusuario puede aprobar actualizaciones federales."));
            if (carga.Estado != "PENDIENTE_APROBACION") return await CancelarAsync(transaction, Respuesta(false, codigoReferencia, carga.Estado, "La actualización federal ya no se encuentra pendiente de aprobación."));

            await AplicarActualizacionAsync(connection, transaction, carga.IdFederalCarga, carga.IdUsuarioCarga, idUsuarioAprobacion);
            await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(connection, transaction, carga.IdFederalCarga, carga.Estado, "CONFIRMADO_ACTUALIZACION", idUsuarioAprobacion, "La actualización federal fue aprobada por el superusuario.");
            await transaction.CommitAsync();
            return Respuesta(true, codigoReferencia, "CONFIRMADO_ACTUALIZACION", "La actualización federal fue aprobada y aplicada correctamente.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<ConfirmarCargaResponse> RechazarAsync(string codigoReferencia, int idUsuarioRechazo, string motivo)
    {
        using var connection = (SqlConnection)_dbConnectionFactory.CrearConexion();
        await connection.OpenAsync();
        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        try
        {
            var carga = await ObtenerActualizacionConfirmacionAsync(connection, transaction, codigoReferencia, idUsuarioRechazo);
            if (carga == null) return await CancelarAsync(transaction, Respuesta(false, codigoReferencia, "NO_ENCONTRADA", "No se encontró la actualización federal pendiente de aprobación."));
            if (!carga.EsSuperUsuario) return await CancelarAsync(transaction, Respuesta(false, codigoReferencia, carga.Estado, "Solo un superusuario puede rechazar actualizaciones federales."));
            if (carga.Estado != "PENDIENTE_APROBACION") return await CancelarAsync(transaction, Respuesta(false, codigoReferencia, carga.Estado, "La actualización federal ya no se encuentra pendiente de aprobación."));

            var motivoLimpio = motivo?.Trim() ?? string.Empty;
            if (motivoLimpio.Length < 5) return await CancelarAsync(transaction, Respuesta(false, codigoReferencia, "MOTIVO_INVALIDO", "Debe capturar un motivo de rechazo de al menos 5 caracteres."));

            await CambiarEstadoAsync(connection, transaction, carga.IdFederalCarga, "RECHAZADO_ADMIN", idUsuarioRechazo, motivoLimpio, rechazoVisto: false);
            await FederalCargaAuditoriaSql.RegistrarCambioEstadoAsync(connection, transaction, carga.IdFederalCarga, carga.Estado, "RECHAZADO_ADMIN", idUsuarioRechazo, motivoLimpio);
            await transaction.CommitAsync();
            return Respuesta(true, codigoReferencia, "RECHAZADO_ADMIN", "La actualización federal fue rechazada por el administrador.");
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<FederalActualizacionConfirmacionInfo?> ObtenerActualizacionConfirmacionAsync(SqlConnection connection, SqlTransaction transaction, string codigoReferencia, int idUsuario)
    {
        const string sql = """
            SELECT c.id_federal_carga AS IdFederalCarga, c.estado AS Estado, c.fecha_expiracion AS FechaExpiracion, c.id_usuario_carga AS IdUsuarioCarga,
                   CONVERT(bit, CASE WHEN r.rol = N'SUPER_USUARIO' THEN 1 ELSE 0 END) AS EsSuperUsuario,
                   CONVERT(bit, ISNULL(um.habilita_modificacion, 0)) AS HabilitaModificacion
            FROM dbo.federal_carga c WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN dbo.usuario u ON u.id_usuario = @IdUsuario AND u.activo = 1
            INNER JOIN dbo.roles r ON r.id_rol = u.id_rol AND r.activo = 1
            INNER JOIN dbo.catalogo_modulo m ON m.clave = N'FEDERAL' AND m.activo = 1
            INNER JOIN dbo.usuario_modulo um ON um.id_usuario = u.id_usuario AND um.id_modulo = m.id_modulo AND um.habilitado = 1 AND um.activo = 1
            WHERE c.codigo_referencia = @CodigoReferencia AND c.tipo_carga = N'ACTUALIZACION' AND c.activo = 1;
            """;

        return await connection.QueryFirstOrDefaultAsync<FederalActualizacionConfirmacionInfo>(sql, new { CodigoReferencia = codigoReferencia, IdUsuario = idUsuario }, transaction);
    }

    private static async Task CambiarEstadoAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, string estado, int? idUsuarioConfirmacion, string? mensaje, bool rechazoVisto = true)
    {
        const string sql = """
            UPDATE dbo.federal_carga
            SET estado = @Estado,
                fecha_confirmacion = CASE WHEN @Estado IN (N'PENDIENTE_APROBACION', N'EXPIRADO') THEN fecha_confirmacion ELSE SYSDATETIME() END,
                fecha_expiracion = CASE WHEN @Estado = N'PENDIENTE_APROBACION' THEN NULL ELSE fecha_expiracion END,
                id_usuario_confirmacion = @IdUsuarioConfirmacion,
                mensaje_error = @Mensaje,
                rechazo_visto = @RechazoVisto,
                fecha_rechazo_visto = CASE WHEN @RechazoVisto = 0 THEN NULL ELSE fecha_rechazo_visto END
            WHERE id_federal_carga = @IdFederalCarga;

            UPDATE dbo.federal_carga_tmp_carpeta SET estado = @Estado, fecha_procesamiento = CASE WHEN @Estado = N'PENDIENTE_APROBACION' THEN NULL ELSE SYSDATETIME() END WHERE id_federal_carga = @IdFederalCarga;
            UPDATE dbo.federal_carga_tmp_delito SET estado = @Estado, fecha_procesamiento = CASE WHEN @Estado = N'PENDIENTE_APROBACION' THEN NULL ELSE SYSDATETIME() END WHERE id_federal_carga = @IdFederalCarga;
            UPDATE dbo.federal_carga_tmp_victima SET estado = @Estado, fecha_procesamiento = CASE WHEN @Estado = N'PENDIENTE_APROBACION' THEN NULL ELSE SYSDATETIME() END WHERE id_federal_carga = @IdFederalCarga;
            """;

        await connection.ExecuteAsync(sql, new { IdFederalCarga = idFederalCarga, Estado = estado, IdUsuarioConfirmacion = idUsuarioConfirmacion, Mensaje = mensaje, RechazoVisto = rechazoVisto }, transaction);
    }

    private static async Task AplicarActualizacionAsync(SqlConnection connection, SqlTransaction transaction, long idFederalCarga, int idUsuarioRegistro, int idUsuarioConfirmacion)
    {
        const string sql = """
        SET NOCOUNT ON;
        DECLARE @MesCorte TINYINT, @AnioCorte SMALLINT;
        SELECT @MesCorte = mes_corte, @AnioCorte = anio_corte FROM dbo.federal_carga WHERE id_federal_carga = @IdFederalCarga;

        SELECT c.id_ci, c.ntra_ci,
               COALESCE(TRY_CONVERT(datetime2, CONCAT(c.fha_de_ini, N' ', NULLIF(LTRIM(RTRIM(c.hra_de_ini)), N'')), 103), TRY_CONVERT(datetime2, CONCAT(c.fha_de_ini, N' ', NULLIF(LTRIM(RTRIM(c.hra_de_ini)), N''))), CASE WHEN TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), N''), N',', N'.')) >= 0 AND TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), N''), N',', N'.')) < 1 THEN DATEADD(SECOND, CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(LTRIM(RTRIM(c.hra_de_ini)), N''), N',', N'.')) * 86400, 0)), COALESCE(TRY_CONVERT(datetime2, c.fha_de_ini, 103), TRY_CONVERT(datetime2, c.fha_de_ini))) END, TRY_CONVERT(datetime2, c.fha_de_ini, 103), TRY_CONVERT(datetime2, c.fha_de_ini)) AS fecha_inicio,
               c.rmen_de_hchos
        INTO #carpetas_tmp
        FROM dbo.federal_carga_tmp_carpeta c WHERE c.id_federal_carga = @IdFederalCarga AND c.activo = 1;

        SELECT d.id_ci, d.id_delito, d.dto, d.moda_dto, fa.id_forma_accion,
               COALESCE(TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, N' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), N'')), 103), TRY_CONVERT(datetime2, CONCAT(d.fha_de_hchos, N' ', NULLIF(LTRIM(RTRIM(d.hra_de_hchos)), N''))), CASE WHEN TRY_CONVERT(float, REPLACE(NULLIF(d.hra_de_hchos, N''), N',', N'.')) >= 0 AND TRY_CONVERT(float, REPLACE(NULLIF(d.hra_de_hchos, N''), N',', N'.')) < 1 THEN DATEADD(SECOND, CONVERT(int, ROUND(TRY_CONVERT(float, REPLACE(NULLIF(d.hra_de_hchos, N''), N',', N'.')) * 86400, 0)), COALESCE(TRY_CONVERT(datetime2, d.fha_de_hchos, 103), TRY_CONVERT(datetime2, d.fha_de_hchos))) END, TRY_CONVERT(datetime2, d.fha_de_hchos, 103), TRY_CONVERT(datetime2, d.fha_de_hchos)) AS fecha_hechos,
               ic.id_instrumento_comision, gc.id_grado_consumacion, md.id_modalidad_delito, ef.id_entidad_federativa, mun.id_municipio,
               d.id_loc_hchos, d.nom_loc_hchos, d.id_col_hchos, d.nom_col_hchos, cp.id_codigo_postal, NULLIF(LTRIM(RTRIM(d.cp)), N'') AS codigo_postal_fiscalia,
               TRY_CONVERT(decimal(10,6), NULLIF(REPLACE(d.coord_x, N',', N'.'), N'')) AS coordenada_x, TRY_CONVERT(decimal(10,6), NULLIF(REPLACE(d.coord_y, N',', N'.'), N'')) AS coordenada_y, d.dom_hchos
        INTO #delitos_tmp
        FROM dbo.federal_carga_tmp_delito d
        INNER JOIN dbo.federal_catalogo_modalidad_delito md ON md.clave4 = d.clasf_de_dto AND md.activo = 1
        INNER JOIN dbo.catalogo_forma_accion fa ON fa.clave = TRY_CONVERT(tinyint, d.forma_acc) AND fa.activo = 1
        INNER JOIN dbo.catalogo_instrumento_comision ic ON ic.clave = TRY_CONVERT(tinyint, d.emto_com_dto) AND ic.activo = 1
        INNER JOIN dbo.catalogo_grado_consumacion gc ON gc.clave = TRY_CONVERT(tinyint, d.grdo_cons) AND gc.activo = 1
        INNER JOIN dbo.catalogo_entidad_federativa ef ON ef.id_entidad_federativa = TRY_CONVERT(tinyint, d.id_ent_hchos) AND ef.activo = 1
        INNER JOIN dbo.catalogo_municipio mun ON mun.id_entidad_federativa = ef.id_entidad_federativa AND TRY_CONVERT(int, mun.clave) = TRY_CONVERT(int, d.id_mun_hchos) AND mun.activo = 1
        OUTER APPLY (SELECT TOP (1) ccp.id_codigo_postal FROM dbo.catalogo_codigo_postal ccp WHERE ccp.codigo_postal = RIGHT(N'00000' + LTRIM(RTRIM(d.cp)), 5) AND ccp.id_municipio = mun.id_municipio AND ccp.activo = 1 ORDER BY ccp.id_codigo_postal) cp
        WHERE d.id_federal_carga = @IdFederalCarga AND d.activo = 1;

        SELECT v.id_ci, v.id_delito, v.id_vicf, tv.id_tipo_victima, tvm.id_tipo_victima_moral, sx.id_sexo, gen.id_genero, nac.id_nacionalidad, pob.id_pertenece_poblacion_indigena, disc.id_presenta_discapacidad,
               COALESCE(TRY_CONVERT(date, NULLIF(v.fha_nac, N''), 103), TRY_CONVERT(date, NULLIF(v.fha_nac, N''))) AS fecha_nacimiento, TRY_CONVERT(smallint, NULLIF(v.edad, N'')) AS edad
        INTO #victimas_tmp
        FROM dbo.federal_carga_tmp_victima v
        INNER JOIN dbo.catalogo_tipo_victima tv ON tv.clave = TRY_CONVERT(tinyint, v.id_tv) AND tv.activo = 1
        LEFT JOIN dbo.catalogo_tipo_victima_moral tvm ON tvm.clave = TRY_CONVERT(tinyint, NULLIF(v.id_tpm, N'')) AND tvm.activo = 1
        LEFT JOIN dbo.catalogo_sexo sx ON sx.clave = TRY_CONVERT(tinyint, NULLIF(v.sexo, N'')) AND sx.activo = 1
        LEFT JOIN dbo.catalogo_genero gen ON gen.clave = TRY_CONVERT(tinyint, NULLIF(v.genero, N'')) AND gen.activo = 1
        LEFT JOIN dbo.catalogo_nacionalidad nac ON TRY_CONVERT(int, nac.clave) = TRY_CONVERT(int, NULLIF(v.nacional, N'')) AND nac.activo = 1
        LEFT JOIN dbo.catalogo_pertenece_poblacion_indigena pob ON pob.clave = TRY_CONVERT(tinyint, NULLIF(v.pob, N'')) AND pob.activo = 1
        LEFT JOIN dbo.catalogo_presenta_discapacidad disc ON disc.clave = TRY_CONVERT(tinyint, NULLIF(v.disc, N'')) AND disc.activo = 1
        WHERE v.id_federal_carga = @IdFederalCarga AND v.activo = 1;

        INSERT INTO dbo.federal_victima_historico (id_federal_victima, id_federal_delito, identificador_victima_fiscalia, id_tipo_victima, id_tipo_victima_moral, id_sexo, id_genero, id_nacionalidad, id_pertenece_poblacion_indigena, id_presenta_discapacidad, fecha_nacimiento, edad, id_usuario_registro, fecha_registro, id_federal_carga, id_usuario_modificacion, id_federal_carga_nueva, tipo_movimiento, fecha_modificacion, activo)
        SELECT v.id_federal_victima, v.id_federal_delito, v.identificador_victima_fiscalia, v.id_tipo_victima, v.id_tipo_victima_moral, v.id_sexo, v.id_genero, v.id_nacionalidad, v.id_pertenece_poblacion_indigena, v.id_presenta_discapacidad, v.fecha_nacimiento, v.edad, v.id_usuario_registro, v.fecha_registro, v.id_federal_carga, @IdUsuarioConfirmacion, @IdFederalCarga,
               CASE WHEN vt.id_vicf IS NULL THEN N'ELIMINADO' ELSE N'MODIFICADO' END, SYSDATETIME(), 1
        FROM dbo.federal_victima v
        INNER JOIN dbo.federal_delito d ON d.id_federal_delito = v.id_federal_delito AND d.activo = 1
        INNER JOIN dbo.federal_carpeta_investigacion ci ON ci.id_federal_carpeta_investigacion = d.id_federal_carpeta_investigacion AND ci.activo = 1
        INNER JOIN dbo.federal_carga c ON c.id_federal_carga = v.id_federal_carga
        LEFT JOIN #victimas_tmp vt ON vt.id_ci = ci.identificador_carpeta_fiscalia AND vt.id_delito = d.identificador_delito_fiscalia AND vt.id_vicf = v.identificador_victima_fiscalia
        WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND v.activo = 1
          AND (vt.id_vicf IS NULL OR ISNULL(v.id_tipo_victima, 0) <> ISNULL(vt.id_tipo_victima, 0) OR ISNULL(v.id_tipo_victima_moral, 0) <> ISNULL(vt.id_tipo_victima_moral, 0) OR ISNULL(v.id_sexo, 0) <> ISNULL(vt.id_sexo, 0) OR ISNULL(v.id_genero, 0) <> ISNULL(vt.id_genero, 0) OR ISNULL(v.id_nacionalidad, 0) <> ISNULL(vt.id_nacionalidad, 0) OR ISNULL(v.id_pertenece_poblacion_indigena, 0) <> ISNULL(vt.id_pertenece_poblacion_indigena, 0) OR ISNULL(v.id_presenta_discapacidad, 0) <> ISNULL(vt.id_presenta_discapacidad, 0) OR ISNULL(CONVERT(varchar(10), v.fecha_nacimiento, 120), N'') <> ISNULL(CONVERT(varchar(10), vt.fecha_nacimiento, 120), N'') OR ISNULL(v.edad, 0) <> ISNULL(vt.edad, 0));

        INSERT INTO dbo.federal_delito_historico (id_federal_delito, id_federal_carpeta_investigacion, identificador_delito_fiscalia, delito_fiscalia, modalidad_delito_fiscalia, id_forma_accion, fecha_hechos, id_instrumento_comision, id_grado_consumacion, id_modalidad_delito, id_entidad_federativa, id_municipio, id_localidad_fiscalia, localidad_fiscalia_nombre, id_colonia_fiscalia, colonia_fiscalia_nombre, id_codigo_postal, codigo_postal_fiscalia, coordenada_x, coordenada_y, domicilio_hechos, id_usuario_registro, fecha_registro, id_federal_carga, id_usuario_modificacion, id_federal_carga_nueva, tipo_movimiento, fecha_modificacion, activo)
        SELECT d.id_federal_delito, d.id_federal_carpeta_investigacion, d.identificador_delito_fiscalia, d.delito_fiscalia, d.modalidad_delito_fiscalia, d.id_forma_accion, d.fecha_hechos, d.id_instrumento_comision, d.id_grado_consumacion, d.id_modalidad_delito, d.id_entidad_federativa, d.id_municipio, d.id_localidad_fiscalia, d.localidad_fiscalia_nombre, d.id_colonia_fiscalia, d.colonia_fiscalia_nombre, d.id_codigo_postal, d.codigo_postal_fiscalia, d.coordenada_x, d.coordenada_y, d.domicilio_hechos, d.id_usuario_registro, d.fecha_registro, d.id_federal_carga, @IdUsuarioConfirmacion, @IdFederalCarga,
               CASE WHEN dt.id_delito IS NULL THEN N'ELIMINADO' ELSE N'MODIFICADO' END, SYSDATETIME(), 1
        FROM dbo.federal_delito d
        INNER JOIN dbo.federal_carpeta_investigacion ci ON ci.id_federal_carpeta_investigacion = d.id_federal_carpeta_investigacion AND ci.activo = 1
        INNER JOIN dbo.federal_carga c ON c.id_federal_carga = d.id_federal_carga
        LEFT JOIN #delitos_tmp dt ON dt.id_ci = ci.identificador_carpeta_fiscalia AND dt.id_delito = d.identificador_delito_fiscalia
        WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND d.activo = 1
          AND (dt.id_delito IS NULL OR ISNULL(d.delito_fiscalia, N'') <> ISNULL(dt.dto, N'') OR ISNULL(d.modalidad_delito_fiscalia, N'') <> ISNULL(dt.moda_dto, N'') OR ISNULL(d.id_forma_accion, 0) <> ISNULL(dt.id_forma_accion, 0) OR ISNULL(CONVERT(varchar(19), d.fecha_hechos, 120), N'') <> ISNULL(CONVERT(varchar(19), dt.fecha_hechos, 120), N'') OR ISNULL(d.id_instrumento_comision, 0) <> ISNULL(dt.id_instrumento_comision, 0) OR ISNULL(d.id_grado_consumacion, 0) <> ISNULL(dt.id_grado_consumacion, 0) OR ISNULL(d.id_modalidad_delito, 0) <> ISNULL(dt.id_modalidad_delito, 0) OR ISNULL(d.id_entidad_federativa, 0) <> ISNULL(dt.id_entidad_federativa, 0) OR ISNULL(d.id_municipio, 0) <> ISNULL(dt.id_municipio, 0) OR ISNULL(d.id_localidad_fiscalia, N'') <> ISNULL(dt.id_loc_hchos, N'') OR ISNULL(d.localidad_fiscalia_nombre, N'') <> ISNULL(dt.nom_loc_hchos, N'') OR ISNULL(d.id_colonia_fiscalia, N'') <> ISNULL(dt.id_col_hchos, N'') OR ISNULL(d.colonia_fiscalia_nombre, N'') <> ISNULL(dt.nom_col_hchos, N'') OR ISNULL(d.id_codigo_postal, 0) <> ISNULL(dt.id_codigo_postal, 0) OR ISNULL(d.codigo_postal_fiscalia, N'') <> ISNULL(dt.codigo_postal_fiscalia, N'') OR ISNULL(d.coordenada_x, 0) <> ISNULL(dt.coordenada_x, 0) OR ISNULL(d.coordenada_y, 0) <> ISNULL(dt.coordenada_y, 0) OR ISNULL(d.domicilio_hechos, N'') <> ISNULL(dt.dom_hchos, N''));

        INSERT INTO dbo.federal_carpeta_investigacion_historico (id_federal_carpeta_investigacion, identificador_carpeta_fiscalia, nomenclatura_carpeta_fiscalia, fecha_inicio, resumen_hechos, id_usuario_registro, fecha_registro, id_federal_carga, id_usuario_modificacion, id_federal_carga_nueva, tipo_movimiento, fecha_modificacion, activo)
        SELECT ci.id_federal_carpeta_investigacion, ci.identificador_carpeta_fiscalia, ci.nomenclatura_carpeta_fiscalia, ci.fecha_inicio, ci.resumen_hechos, ci.id_usuario_registro, ci.fecha_registro, ci.id_federal_carga, @IdUsuarioConfirmacion, @IdFederalCarga,
               CASE WHEN ct.id_ci IS NULL THEN N'ELIMINADO' ELSE N'MODIFICADO' END, SYSDATETIME(), 1
        FROM dbo.federal_carpeta_investigacion ci
        INNER JOIN dbo.federal_carga c ON c.id_federal_carga = ci.id_federal_carga
        LEFT JOIN #carpetas_tmp ct ON ct.id_ci = ci.identificador_carpeta_fiscalia
        WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND ci.activo = 1
          AND (ct.id_ci IS NULL OR ISNULL(ci.nomenclatura_carpeta_fiscalia, N'') <> ISNULL(ct.ntra_ci, N'') OR ISNULL(CONVERT(varchar(19), ci.fecha_inicio, 120), N'') <> ISNULL(CONVERT(varchar(19), ct.fecha_inicio, 120), N'') OR ISNULL(ci.resumen_hechos, N'') <> ISNULL(ct.rmen_de_hchos, N''));

        UPDATE v SET v.id_tipo_victima = vt.id_tipo_victima, v.id_tipo_victima_moral = vt.id_tipo_victima_moral, v.id_sexo = vt.id_sexo, v.id_genero = vt.id_genero, v.id_nacionalidad = vt.id_nacionalidad, v.id_pertenece_poblacion_indigena = vt.id_pertenece_poblacion_indigena, v.id_presenta_discapacidad = vt.id_presenta_discapacidad, v.fecha_nacimiento = vt.fecha_nacimiento, v.edad = vt.edad, v.id_federal_carga = @IdFederalCarga
        FROM dbo.federal_victima v INNER JOIN dbo.federal_delito d ON d.id_federal_delito = v.id_federal_delito AND d.activo = 1 INNER JOIN dbo.federal_carpeta_investigacion ci ON ci.id_federal_carpeta_investigacion = d.id_federal_carpeta_investigacion AND ci.activo = 1 INNER JOIN dbo.federal_carga c ON c.id_federal_carga = v.id_federal_carga INNER JOIN #victimas_tmp vt ON vt.id_ci = ci.identificador_carpeta_fiscalia AND vt.id_delito = d.identificador_delito_fiscalia AND vt.id_vicf = v.identificador_victima_fiscalia
        WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND v.activo = 1;

        UPDATE d SET d.delito_fiscalia = dt.dto, d.modalidad_delito_fiscalia = dt.moda_dto, d.id_forma_accion = dt.id_forma_accion, d.fecha_hechos = dt.fecha_hechos, d.id_instrumento_comision = dt.id_instrumento_comision, d.id_grado_consumacion = dt.id_grado_consumacion, d.id_modalidad_delito = dt.id_modalidad_delito, d.id_entidad_federativa = dt.id_entidad_federativa, d.id_municipio = dt.id_municipio, d.id_localidad_fiscalia = dt.id_loc_hchos, d.localidad_fiscalia_nombre = dt.nom_loc_hchos, d.id_colonia_fiscalia = dt.id_col_hchos, d.colonia_fiscalia_nombre = dt.nom_col_hchos, d.id_codigo_postal = dt.id_codigo_postal, d.codigo_postal_fiscalia = dt.codigo_postal_fiscalia, d.coordenada_x = dt.coordenada_x, d.coordenada_y = dt.coordenada_y, d.domicilio_hechos = dt.dom_hchos, d.id_federal_carga = @IdFederalCarga
        FROM dbo.federal_delito d INNER JOIN dbo.federal_carpeta_investigacion ci ON ci.id_federal_carpeta_investigacion = d.id_federal_carpeta_investigacion AND ci.activo = 1 INNER JOIN dbo.federal_carga c ON c.id_federal_carga = d.id_federal_carga INNER JOIN #delitos_tmp dt ON dt.id_ci = ci.identificador_carpeta_fiscalia AND dt.id_delito = d.identificador_delito_fiscalia
        WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND d.activo = 1;

        UPDATE ci SET ci.nomenclatura_carpeta_fiscalia = ct.ntra_ci, ci.fecha_inicio = ct.fecha_inicio, ci.resumen_hechos = ct.rmen_de_hchos, ci.id_federal_carga = @IdFederalCarga
        FROM dbo.federal_carpeta_investigacion ci INNER JOIN dbo.federal_carga c ON c.id_federal_carga = ci.id_federal_carga INNER JOIN #carpetas_tmp ct ON ct.id_ci = ci.identificador_carpeta_fiscalia
        WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND ci.activo = 1;

        INSERT INTO dbo.federal_carpeta_investigacion (identificador_carpeta_fiscalia, nomenclatura_carpeta_fiscalia, fecha_inicio, resumen_hechos, id_usuario_registro, fecha_registro, id_federal_carga, activo)
        SELECT ct.id_ci, ct.ntra_ci, ct.fecha_inicio, ct.rmen_de_hchos, @IdUsuarioRegistro, SYSDATETIME(), @IdFederalCarga, 1 FROM #carpetas_tmp ct
        WHERE NOT EXISTS (SELECT 1 FROM dbo.federal_carpeta_investigacion ci INNER JOIN dbo.federal_carga c ON c.id_federal_carga = ci.id_federal_carga WHERE ci.identificador_carpeta_fiscalia = ct.id_ci AND ci.activo = 1 AND c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1);

        INSERT INTO dbo.federal_delito (id_federal_carpeta_investigacion, identificador_delito_fiscalia, delito_fiscalia, modalidad_delito_fiscalia, id_forma_accion, fecha_hechos, id_instrumento_comision, id_grado_consumacion, id_modalidad_delito, id_entidad_federativa, id_municipio, id_localidad_fiscalia, localidad_fiscalia_nombre, id_colonia_fiscalia, colonia_fiscalia_nombre, id_codigo_postal, codigo_postal_fiscalia, coordenada_x, coordenada_y, domicilio_hechos, id_usuario_registro, fecha_registro, id_federal_carga, activo)
        SELECT ci.id_federal_carpeta_investigacion, dt.id_delito, dt.dto, dt.moda_dto, dt.id_forma_accion, dt.fecha_hechos, dt.id_instrumento_comision, dt.id_grado_consumacion, dt.id_modalidad_delito, dt.id_entidad_federativa, dt.id_municipio, dt.id_loc_hchos, dt.nom_loc_hchos, dt.id_col_hchos, dt.nom_col_hchos, dt.id_codigo_postal, dt.codigo_postal_fiscalia, dt.coordenada_x, dt.coordenada_y, dt.dom_hchos, @IdUsuarioRegistro, SYSDATETIME(), @IdFederalCarga, 1
        FROM #delitos_tmp dt INNER JOIN dbo.federal_carpeta_investigacion ci ON ci.identificador_carpeta_fiscalia = dt.id_ci AND ci.activo = 1 INNER JOIN dbo.federal_carga cc ON cc.id_federal_carga = ci.id_federal_carga AND cc.activo = 1 AND (cc.id_federal_carga = @IdFederalCarga OR (cc.mes_corte = @MesCorte AND cc.anio_corte = @AnioCorte AND cc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION')))
        WHERE NOT EXISTS (SELECT 1 FROM dbo.federal_delito d INNER JOIN dbo.federal_carga c ON c.id_federal_carga = d.id_federal_carga WHERE d.id_federal_carpeta_investigacion = ci.id_federal_carpeta_investigacion AND d.identificador_delito_fiscalia = dt.id_delito AND d.activo = 1 AND c.activo = 1 AND (c.id_federal_carga = @IdFederalCarga OR (c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION'))));

        INSERT INTO dbo.federal_victima (id_federal_delito, identificador_victima_fiscalia, id_tipo_victima, id_tipo_victima_moral, id_sexo, id_genero, id_nacionalidad, id_pertenece_poblacion_indigena, id_presenta_discapacidad, fecha_nacimiento, edad, id_usuario_registro, fecha_registro, id_federal_carga, activo)
        SELECT d.id_federal_delito, vt.id_vicf, vt.id_tipo_victima, vt.id_tipo_victima_moral, vt.id_sexo, vt.id_genero, vt.id_nacionalidad, vt.id_pertenece_poblacion_indigena, vt.id_presenta_discapacidad, vt.fecha_nacimiento, vt.edad, @IdUsuarioRegistro, SYSDATETIME(), @IdFederalCarga, 1
        FROM #victimas_tmp vt INNER JOIN dbo.federal_carpeta_investigacion ci ON ci.identificador_carpeta_fiscalia = vt.id_ci AND ci.activo = 1 INNER JOIN dbo.federal_carga cc ON cc.id_federal_carga = ci.id_federal_carga AND cc.activo = 1 AND (cc.id_federal_carga = @IdFederalCarga OR (cc.mes_corte = @MesCorte AND cc.anio_corte = @AnioCorte AND cc.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION'))) INNER JOIN dbo.federal_delito d ON d.id_federal_carpeta_investigacion = ci.id_federal_carpeta_investigacion AND d.identificador_delito_fiscalia = vt.id_delito AND d.activo = 1
        WHERE NOT EXISTS (SELECT 1 FROM dbo.federal_victima v INNER JOIN dbo.federal_carga c ON c.id_federal_carga = v.id_federal_carga WHERE v.id_federal_delito = d.id_federal_delito AND v.identificador_victima_fiscalia = vt.id_vicf AND v.activo = 1 AND c.activo = 1 AND (c.id_federal_carga = @IdFederalCarga OR (c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION'))));

        UPDATE v SET activo = 0 FROM dbo.federal_victima v INNER JOIN dbo.federal_delito d ON d.id_federal_delito = v.id_federal_delito AND d.activo = 1 INNER JOIN dbo.federal_carpeta_investigacion ci ON ci.id_federal_carpeta_investigacion = d.id_federal_carpeta_investigacion AND ci.activo = 1 INNER JOIN dbo.federal_carga c ON c.id_federal_carga = v.id_federal_carga LEFT JOIN #victimas_tmp vt ON vt.id_ci = ci.identificador_carpeta_fiscalia AND vt.id_delito = d.identificador_delito_fiscalia AND vt.id_vicf = v.identificador_victima_fiscalia WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND v.activo = 1 AND vt.id_vicf IS NULL;
        UPDATE d SET activo = 0 FROM dbo.federal_delito d INNER JOIN dbo.federal_carpeta_investigacion ci ON ci.id_federal_carpeta_investigacion = d.id_federal_carpeta_investigacion AND ci.activo = 1 INNER JOIN dbo.federal_carga c ON c.id_federal_carga = d.id_federal_carga LEFT JOIN #delitos_tmp dt ON dt.id_ci = ci.identificador_carpeta_fiscalia AND dt.id_delito = d.identificador_delito_fiscalia WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND d.activo = 1 AND dt.id_delito IS NULL;
        UPDATE ci SET activo = 0 FROM dbo.federal_carpeta_investigacion ci INNER JOIN dbo.federal_carga c ON c.id_federal_carga = ci.id_federal_carga LEFT JOIN #carpetas_tmp ct ON ct.id_ci = ci.identificador_carpeta_fiscalia WHERE c.mes_corte = @MesCorte AND c.anio_corte = @AnioCorte AND c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND ci.activo = 1 AND ct.id_ci IS NULL;

        UPDATE dbo.federal_carga SET estado = N'CONFIRMADO_ACTUALIZACION', fecha_confirmacion = SYSDATETIME(), fecha_expiracion = NULL, id_usuario_confirmacion = @IdUsuarioConfirmacion, mensaje_error = NULL WHERE id_federal_carga = @IdFederalCarga;
        UPDATE dbo.federal_carga_tmp_carpeta SET estado = N'PROCESADO', fecha_procesamiento = SYSDATETIME() WHERE id_federal_carga = @IdFederalCarga;
        UPDATE dbo.federal_carga_tmp_delito SET estado = N'PROCESADO', fecha_procesamiento = SYSDATETIME() WHERE id_federal_carga = @IdFederalCarga;
        UPDATE dbo.federal_carga_tmp_victima SET estado = N'PROCESADO', fecha_procesamiento = SYSDATETIME() WHERE id_federal_carga = @IdFederalCarga;
        """;

        await connection.ExecuteAsync(sql, new { IdFederalCarga = idFederalCarga, IdUsuarioRegistro = idUsuarioRegistro, IdUsuarioConfirmacion = idUsuarioConfirmacion }, transaction, commandTimeout: 600);
    }

    private static async Task<ConfirmarCargaResponse> CancelarAsync(SqlTransaction transaction, ConfirmarCargaResponse response)
    {
        await transaction.RollbackAsync();
        return response;
    }
}
