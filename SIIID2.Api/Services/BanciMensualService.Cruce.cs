using System.Data;
using System.Text.Json;
using System.Security.Cryptography;
using Dapper;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public sealed partial class BanciMensualService
{
    private async Task<bool> CruceActivoAsync()
    {
        await configuracion.CargarAsync();
        return configuracion.Modulos.Any(m => m.Clave == "BANCI" && m.Activo)
            && configuracion.Activa("MENSUAL", "CRUCE_ACTUALIZACION_CONSOLIDADO");
    }

    public async Task<BanciCrucePreparado> PrepararCruceAsync(int usuario, BanciLibro mensual, IFormFile? complementos)
    {
        if(!await CruceActivoAsync())
            return new();
        using var db = factory.CrearConexion();
        var origen = await OrigenAsync(db, mensual.Entidad, mensual.Mes, mensual.Anio, true);
        // El cruce corresponde solo a cortes previamente reportados en BANCI.
        if(origen.Carpetas.Count == 0)
            return new();
        await PrepararComplementosAsync(origen);
        if(await db.ExecuteScalarAsync<bool>("SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.banci_carga b WHERE b.id_entidad_federativa=@Entidad AND b.mes_corte=@Mes AND b.anio_corte=@Anio AND b.estado IN('VALIDADO','PENDIENTE_APROBACION') AND (b.estado<>'VALIDADO' OR b.id_usuario_carga<>@usuario OR EXISTS(SELECT 1 FROM dbo.banci_operacion_vinculada ownLink WHERE ownLink.id_banci_carga=b.id_banci_carga)) AND NOT EXISTS(SELECT 1 FROM dbo.banci_operacion_vinculada ov JOIN dbo.carga c ON c.id_carga=ov.id_carga_consolidado WHERE ov.id_banci_carga=b.id_banci_carga AND (c.estado NOT IN('VALIDADO_PENDIENTE_ACTUALIZACION','PENDIENTE_APROBACION') OR (c.estado='VALIDADO_PENDIENTE_ACTUALIZACION' AND c.fecha_expiracion<SYSDATETIME())))) THEN 1 ELSE 0 END);", new
        {
            origen.Entidad,
            origen.Mes,
            origen.Anio,
            usuario
        }))
            return new()
            {
                Errores = [Error("general", new(), "banci", "Existe un envío BANCI validado o pendiente de aprobación para este corte. Resuélvalo antes de actualizar el Consolidado.")]
            };
        var condiciones = (await db.QueryAsync<(string Codigo, string Clave)>("SELECT codigo_regla AS Codigo,CONVERT(varchar(20),clave) AS Clave FROM dbo.banci_catalogo_constitutiva_delito WHERE activo=1;")).ToDictionary(x => x.Codigo, x => x.Clave);
        var clases = (await db.QueryAsync<string>("SELECT clave FROM dbo.banci_catalogo_clasificacion_delito WHERE activo=1;")).ToHashSet();
        var desapariciones = (await db.QueryAsync<string>(ClavesDesaparicionSql)).ToHashSet();
        var delitosPadre = (await db.QueryAsync<(string Modalidad, string Delito)>("SELECT md.clave4 AS Modalidad,cd.clave2 AS Delito FROM dbo.catalogo_modalidad_delito md JOIN dbo.catalogo_subtipo_delito sd ON sd.id_subtipo_delito=md.id_subtipo_delito JOIN dbo.catalogo_delito cd ON cd.id_delito=sd.id_delito;")).ToDictionary(x => x.Modalidad, x => x.Delito);
        var resultado = CompararCruce(origen, mensual, clases, condiciones["NO_DELITO"], condiciones["DELITO"], desapariciones, delitosPadre);
        if(resultado.Errores.Count > 0)
            return resultado;
        // Permiso BANCI se exige únicamente para capturar complementos, no para consultar el cruce obligatorio.
        if(resultado.Nuevas!.Victimas.Count > 0)
        {
            await AccesoAsync(usuario, mensual.Entidad, "CARGA");
            await PrepararComplementosAsync(resultado.Nuevas);
            if(complementos == null)
            {
                resultado.RequiereComplementos = true;
                return resultado;
            }
            var nuevas = await LeerExcelAsync(complementos, mensual.Entidad, mensual.Mes, mensual.Anio, false);
            resultado.Errores.AddRange(CompararOrigen(resultado.Nuevas, nuevas));
            foreach(var fila in nuevas.Victimas)
            {
                foreach(var campo in new[] { "curp", "rfc" })
                    if(Valor(fila, campo) != "")
                        fila.Columnas[campo] = Valor(fila, campo).ToUpperInvariant();
                if(Valor(fila, "localizado_o_no_localizado") == "")
                    fila.Columnas["localizado_o_no_localizado"] = reglas.Clave("banci_catalogo_localizacion", "NO_LOCALIZADO");
            }
            NormalizarLocalizacion(nuevas, reglas.Clave("banci_catalogo_localizacion", "NO_LOCALIZADO"));
            resultado.Errores.AddRange(await ValidarPersistenciaAsync(nuevas));
            resultado.Errores.AddRange(integridad.Validar(nuevas.Carpetas, nuevas.Delitos, nuevas.Victimas));
            resultado.Errores.AddRange(await ValidarClasificacionReclasificacionAsync(nuevas));
            if(configuracion.Activa("BANCI", "VALIDACIONES_CAMPOS"))
            {
                resultado.Errores.AddRange(await ValidarComplementosAsync(nuevas));
                resultado.Errores.AddRange(await victimasValidator.ValidarAsync(nuevas.Victimas));
                resultado.Errores.AddRange(await catalogosValidator.ValidarAsync(nuevas.Carpetas, nuevas.Delitos, nuevas.Victimas));
                await ValidarRenapoBanciAsync(nuevas, resultado.Errores, resultado.Advertencias);
            }
            if(resultado.Errores.Count > 0)
                return resultado;
            await using(var memoria = new MemoryStream())
            {
                await complementos.CopyToAsync(memoria);
                resultado.Archivo = memoria.ToArray();
            }
            resultado.NombreArchivo = NombreOriginal(complementos.FileName);
            var porClave = nuevas.Victimas.ToDictionary(v => Llave(v, 3));
            foreach(var victima in resultado.Propuesta!.Victimas)
                if(porClave.TryGetValue(Llave(victima, 3), out var capturada))
                    foreach(var campo in Complementos)
                        victima.Columnas[campo] = capturada.Columnas.GetValueOrDefault(campo);
        }
        return resultado;
    }

    // No empareja víctimas por nombre ni adivina IDs; conserva la clave compuesta de origen.
    public static BanciCrucePreparado CompararCruce(BanciLibro banci, BanciLibro mensual, ISet<string> clases,
        string noDelito, string delito, ISet<string> desapariciones, IReadOnlyDictionary<string, string>? delitosPadre = null)
    {
        BanciLibro Copia(BanciLibro libro) => JsonSerializer.Deserialize<BanciLibro>(Serializar(libro), Json)!;
        var propuesta = Copia(banci);
        var nuevas = new BanciLibro { Entidad = mensual.Entidad, Mes = mensual.Mes, Anio = mensual.Anio };
        var resultado = new BanciCrucePreparado { Origen = banci, Propuesta = propuesta, Nuevas = nuevas };
        var delitos = mensual.Delitos.GroupBy(d => Llave(d, 2)).ToDictionary(g => g.Key, g => g.First());
        var victimas = mensual.Victimas.GroupBy(v => Llave(v, 3)).ToDictionary(g => g.Key, g => g.First());
        var previas = banci.Victimas.Select(v => Llave(v, 3)).ToHashSet();
        var destinos = propuesta.Victimas.ToDictionary(v => Llave(v, 3));
        static string Identidad(ArchivoFila v) => Serializar(new[] { Valor(v, "id_ci"), Valor(v, "id_vicf") });
        var candidatasPorIdentidad = mensual.Victimas.GroupBy(Identidad).ToDictionary(g => g.Key, g => g.ToArray());
        var conteoPrevias = banci.Victimas.GroupBy(Identidad).ToDictionary(g => g.Key, g => g.Count());
        foreach(var previa in banci.Victimas)
        {
            var key = Llave(previa, 3);
            delitos.TryGetValue(Llave(previa, 2), out var actualDelito);
            victimas.TryGetValue(key, out var actualVictima);
            var reclasificada = EsReclasificada(previa, noDelito, delito, desapariciones);
            // Solo una reclasificación aprobada permite trasladar la víctima a otro delito del mismo CI.
            // La identidad debe ser inequívoca en ambos conjuntos; nunca se empareja por nombre.
            if(reclasificada && actualVictima == null)
            {
                var candidatas = candidatasPorIdentidad.GetValueOrDefault(Identidad(previa)) ?? [];
                var previasIguales = conteoPrevias[Identidad(previa)];
                if(candidatas.Length > 0)
                {
                    if(candidatas.Length != 1 || previasIguales != 1)
                    {
                        resultado.Errores.Add(Error("victimas", previa, "identificadores", "No se puede identificar inequívocamente la víctima reclasificada dentro de su carpeta. Conserve sus identificadores de origen."));
                        continue;
                    }
                    actualVictima = candidatas[0];
                    delitos.TryGetValue(Llave(actualVictima, 2), out actualDelito);
                }
            }
            var sigue = actualVictima != null && actualDelito != null && clases.Contains(Valor(actualDelito, "clasf_de_dto"));
            string? mensaje = null;
            if(!reclasificada && !sigue)
                mensaje = "La víctima sigue como desaparición en BANCI. Reporte y apruebe primero su reclasificación en BANCI; no puede omitirla ni cambiar sus identificadores.";
            if(reclasificada && sigue)
                mensaje = "La víctima tiene una reclasificación aprobada en BANCI. Refleje esa reclasificación en el Consolidado o reviértala y apruébela primero en BANCI.";
            if(reclasificada && actualVictima != null && actualDelito != null)
            {
                var motivo = Valor(previa, "motivo_desaparicion");
                var clasificacion = Valor(actualDelito, "clasf_de_dto");
                if(Valor(previa, "constitutiva_delito") == noDelito)
                    mensaje = "BANCI reporta No delito: esta víctima no debe seguir vinculada a un delito en el Consolidado.";
                else if(!clasificacion.Equals(motivo, StringComparison.Ordinal) && !(delitosPadre != null && delitosPadre.TryGetValue(clasificacion, out var padre) && padre == motivo))
                    mensaje = "El delito reportado en el Consolidado no coincide con la reclasificación aprobada en BANCI.";
            }
            if(mensaje != null)
                resultado.Errores.Add(Error("victimas", actualVictima ?? previa, "reclasificacion", mensaje));
            if(sigue && !reclasificada)
            {
                var destino = destinos[key];
                foreach(var par in actualVictima!.Columnas.Where(p => !Complementos.Contains(p.Key) && !CamposFeminicidio.Contains(p.Key)))
                    destino.Columnas[par.Key] = par.Value;
            }
        }
        foreach(var victima in mensual.Victimas)
        {
            if(!delitos.TryGetValue(Llave(victima, 2), out var d) || !clases.Contains(Valor(d, "clasf_de_dto")) || previas.Contains(Llave(victima, 3)))
                continue;
            var fila = CopiaFila(victima);
            PrecargarIdentidad(fila);
            foreach(var campo in CamposFeminicidio)
                fila.Columnas.Remove(campo);
            nuevas.Victimas.Add(fila);
            propuesta.Victimas.Add(CopiaFila(fila));
        }
        var delitosNuevos = nuevas.Victimas.Select(v => Llave(v, 2)).ToHashSet();
        var carpetasNuevas = nuevas.Victimas.Select(v => Llave(v, 1)).ToHashSet();
        var vigentes = propuesta.Victimas.Where(v => !EsReclasificada(v, noDelito, delito, desapariciones)).ToArray();
        var delitosVigentes = vigentes.Select(v => Llave(v, 2)).ToHashSet();
        var carpetasVigentes = vigentes.Select(v => Llave(v, 1)).ToHashSet();
        var propuestosDelitos = propuesta.Delitos.ToDictionary(d => Llave(d, 2));
        var propuestasCarpetas = propuesta.Carpetas.ToDictionary(c => Llave(c, 1));
        foreach(var d in mensual.Delitos)
        {
            if(!clases.Contains(Valor(d, "clasf_de_dto")))
                continue;
            if(delitosNuevos.Contains(Llave(d, 2)))
                nuevas.Delitos.Add(CopiaFila(d));
            if(delitosVigentes.Contains(Llave(d, 2)))
                propuestosDelitos[Llave(d, 2)] = CopiaFila(d);
        }
        foreach(var c in mensual.Carpetas)
        {
            if(carpetasNuevas.Contains(Llave(c, 1)))
                nuevas.Carpetas.Add(CopiaFila(c));
            if(carpetasVigentes.Contains(Llave(c, 1)))
            {
                var folio = propuestasCarpetas.GetValueOrDefault(Llave(c, 1))?.Columnas.GetValueOrDefault("no_banci");
                var copia = CopiaFila(c);
                if(folio != null)
                    copia.Columnas["no_banci"] = folio;
                propuestasCarpetas[Llave(c, 1)] = copia;
            }
        }
        propuesta.Delitos = propuestosDelitos.Values.ToList();
        propuesta.Carpetas = propuestasCarpetas.Values.ToList();
        foreach(var (_, filas) in Grupos(propuesta))
            for(var i = 0; i < filas.Count; i++)
                filas[i].NumeroFila = i + 2;
        foreach(var (_, filas) in Grupos(nuevas))
            for(var i = 0; i < filas.Count; i++)
                filas[i].NumeroFila = i + 2;
        return resultado;
    }
    private static ArchivoFila CopiaFila(ArchivoFila fila) => new() { NumeroFila = fila.NumeroFila, Columnas = new(fila.Columnas, StringComparer.OrdinalIgnoreCase) };

    // Se invoca dentro de la transacción que guarda el staging del Consolidado.
    public async Task GuardarCruceAsync(IDbConnection db, IDbTransaction tx, long carga, int usuario, BanciCrucePreparado? cruce)
    {
        if(cruce?.Origen == null || cruce.Propuesta == null)
            return;
        if(cruce.RequiereComplementos || cruce.Errores.Count > 0)
            throw new InvalidOperationException("Complete el cruce BANCI antes de validar.");
        await BloquearCorteAsync(db, tx, cruce.Origen);
        var actual = await OrigenAsync(db, cruce.Origen.Entidad, cruce.Origen.Mes, cruce.Origen.Anio, true, tx);
        await PrepararComplementosAsync(actual);
        if(Huella(actual) != Huella(cruce.Origen))
            throw new InvalidOperationException("BANCI cambió durante la validación. Vuelva a validar los archivos.");
        await LiberarCrucesVencidosAsync(db, tx, actual);
        await DescartarBorradoresPropiosAsync(db, tx, actual, usuario);
        await VerificarPendientesCruceAsync(db, tx, actual);
        var referencia = cruce.Referencia;
        var id = await db.ExecuteScalarAsync<long>("""
            INSERT dbo.banci_carga(codigo_referencia,id_entidad_federativa,mes_corte,anio_corte,tipo_carga,estado,id_usuario_carga,snapshot_origen,huella_origen,id_carga_consolidado)
            VALUES(@referencia,@Entidad,@Mes,@Anio,'ACTUALIZACION','VALIDADO',@usuario,@snapshot,@huella,@carga);
            SELECT CONVERT(bigint,SCOPE_IDENTITY());
            """, new
        {
            referencia,
            actual.Entidad,
            actual.Mes,
            actual.Anio,
            usuario,
            snapshot = Serializar(actual),
            huella = Huella(actual),
            carga
        }, tx);
        foreach(var (tabla, filas) in Grupos(cruce.Propuesta))
            foreach(var fila in filas)
                await db.ExecuteAsync($"INSERT dbo.banci_carga_tmp_{tabla}(id_banci_carga,numero_fila,datos_propuestos) VALUES(@id,@numero,@json);", new
                {
                    id,
                    numero = fila.NumeroFila,
                    json = Serializar(fila)
                }, tx);
        await db.ExecuteAsync("INSERT dbo.banci_operacion_vinculada(id_carga_consolidado,id_banci_carga,huella_origen) VALUES(@carga,@id,@huella);", new
        {
            carga,
            id,
            huella = Huella(actual)
        }, tx);
        if(cruce.Archivo != null)
        {
            var ruta = RutaOriginal(referencia, cruce.NombreArchivo);
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            await File.WriteAllBytesAsync(ruta, cruce.Archivo);
            await db.ExecuteAsync("INSERT dbo.banci_carga_archivo(id_banci_carga,nombre_original,ruta_relativa,huella_sha256,longitud_bytes) VALUES(@id,@nombre,@ruta,@huella,@longitud);",
                new
                {
                    id,
                    nombre = cruce.NombreArchivo,
                    ruta = RutaRelativaOriginal(referencia, cruce.NombreArchivo),
                    huella = Convert.ToHexString(SHA256.HashData(cruce.Archivo)),
                    longitud = cruce.Archivo.LongLength
                }, tx);
        }
        await BitacoraAsync(db, tx, id, usuario, null, "VALIDADO", "Operación vinculada a actualización del Consolidado.");
    }
    public void EliminarOriginalCruce(BanciCrucePreparado? cruce)
    {
        if(cruce?.Archivo != null)
            File.Delete(RutaOriginal(cruce.Referencia, cruce.NombreArchivo));
    }
    private static async Task BloquearCorteAsync(IDbConnection db, IDbTransaction tx, BanciLibro libro)
    {
        var bloqueo = await db.ExecuteScalarAsync<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@recurso,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @r;", new
        {
            recurso = $"BANCI:{libro.Entidad}:{libro.Anio}:{libro.Mes}"
        }, tx);
        if(bloqueo < 0)
            throw new InvalidOperationException("El corte está siendo procesado. Reintente.");
    }
    private static Task DescartarBorradoresPropiosAsync(IDbConnection db, IDbTransaction tx, BanciLibro libro, int usuario) => db.ExecuteAsync("""
        DECLARE @borradores TABLE(id BIGINT);
        UPDATE b SET estado='RECHAZADO_USUARIO',motivo_resolucion=N'Sustituido por la actualización vinculada del Consolidado.'
        OUTPUT INSERTED.id_banci_carga INTO @borradores
        FROM dbo.banci_carga b WHERE b.id_entidad_federativa=@Entidad AND b.mes_corte=@Mes AND b.anio_corte=@Anio
          AND b.estado='VALIDADO' AND b.id_usuario_carga=@usuario
          AND NOT EXISTS(SELECT 1 FROM dbo.banci_operacion_vinculada ov WHERE ov.id_banci_carga=b.id_banci_carga);
        INSERT dbo.banci_carga_bitacora_estado(id_banci_carga,estado_anterior,estado_nuevo,id_usuario,comentario)
        SELECT id,'VALIDADO','RECHAZADO_USUARIO',@usuario,N'Sustituido por la actualización vinculada del Consolidado.' FROM @borradores;
        """, new { libro.Entidad, libro.Mes, libro.Anio, usuario }, tx);

    private static Task LiberarCrucesVencidosAsync(IDbConnection db, IDbTransaction tx, BanciLibro libro) => db.ExecuteAsync("""
        DECLARE @vencidos TABLE(id BIGINT,usuario INT);
        UPDATE b SET estado='RECHAZADO_USUARIO',motivo_resolucion=N'La actualización vinculada expiró o fue descartada.'
        OUTPUT INSERTED.id_banci_carga,INSERTED.id_usuario_carga INTO @vencidos
        FROM dbo.banci_carga b JOIN dbo.banci_operacion_vinculada ov ON ov.id_banci_carga=b.id_banci_carga
        JOIN dbo.carga c ON c.id_carga=ov.id_carga_consolidado
        WHERE b.id_entidad_federativa=@Entidad AND b.mes_corte=@Mes AND b.anio_corte=@Anio AND b.estado='VALIDADO'
          AND (c.estado NOT IN('VALIDADO_PENDIENTE_ACTUALIZACION','PENDIENTE_APROBACION') OR (c.estado='VALIDADO_PENDIENTE_ACTUALIZACION' AND c.fecha_expiracion<SYSDATETIME()));
        INSERT dbo.banci_carga_bitacora_estado(id_banci_carga,estado_anterior,estado_nuevo,id_usuario,comentario)
        SELECT id,'VALIDADO','RECHAZADO_USUARIO',usuario,N'La actualización vinculada expiró o fue descartada.' FROM @vencidos;
        """, libro, tx);

    private static async Task VerificarPendientesCruceAsync(IDbConnection db, IDbTransaction tx, BanciLibro libro, long? excluir = null)
    {
        if(await db.ExecuteScalarAsync<bool>("SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.banci_carga WHERE id_entidad_federativa=@Entidad AND mes_corte=@Mes AND anio_corte=@Anio AND estado IN('VALIDADO','PENDIENTE_APROBACION') AND NOT EXISTS(SELECT 1 FROM dbo.banci_operacion_vinculada ov JOIN dbo.carga c ON c.id_carga=ov.id_carga_consolidado WHERE ov.id_banci_carga=dbo.banci_carga.id_banci_carga AND (c.estado NOT IN('VALIDADO_PENDIENTE_ACTUALIZACION','PENDIENTE_APROBACION') OR (c.estado='VALIDADO_PENDIENTE_ACTUALIZACION' AND c.fecha_expiracion<SYSDATETIME()))) AND (@excluir IS NULL OR id_banci_carga<>@excluir)) THEN 1 ELSE 0 END);", new
        {
            libro.Entidad,
            libro.Mes,
            libro.Anio,
            excluir
        }, tx))
            throw new InvalidOperationException("Hay un envío BANCI en validación o pendiente de aprobación para este corte. Resuélvalo antes de continuar con el cruce.");
    }

    // Estado y definitivos se confirman o revierten con la transacción del Consolidado.
    public async Task ResolverCruceAsync(IDbConnection db, IDbTransaction tx, long carga, int usuario, string estado, string motivo = "")
    {
        var id = await db.QuerySingleOrDefaultAsync<long?>("SELECT id_banci_carga FROM dbo.banci_operacion_vinculada WHERE id_carga_consolidado=@carga;", new
        {
            carga
        }, tx);
        if(id == null)
        {
            if(estado is "CONFIRMADO_ACTUALIZACION" or "PENDIENTE_APROBACION" && await CruceActivoAsync())
            {
                var existe = await db.ExecuteScalarAsync<bool>("SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.carga c JOIN dbo.banci_carpeta_investigacion ci ON ci.id_entidad_federativa=c.id_entidad_federativa AND ci.mes_corte=c.mes_corte AND ci.anio_corte=c.anio_corte WHERE c.id_carga=@carga AND ci.activo=1) THEN 1 ELSE 0 END);", new
                {
                    carga
                }, tx);
                if(existe)
                    throw new InvalidOperationException("Esta actualización se validó sin el cruce BANCI. Vuelva a validar los archivos.");
            }
            return;
        }
        var op = await db.QuerySingleAsync<BanciOperacion>(BanciMensualSql.Operaciones + " WHERE b.id_banci_carga=@id;", new
        {
            id
        }, tx);
        if(estado == "EXPIRADO_ACTUALIZACION" && op.Estado == "RECHAZADO_USUARIO")
            return;
        if(op.Estado is not ("VALIDADO" or "PENDIENTE_APROBACION"))
            throw new InvalidOperationException("La operación BANCI vinculada ya fue resuelta.");
        var libro = new BanciLibro { Entidad = op.Entidad, Mes = op.Mes, Anio = op.Anio };
        await BloquearCorteAsync(db, tx, libro);
        if(estado is "CONFIRMADO_ACTUALIZACION" or "PENDIENTE_APROBACION")
        {
            await VerificarPendientesCruceAsync(db, tx, libro, op.Id);
            var actual = await OrigenAsync(db, op.Entidad, op.Mes, op.Anio, true, tx);
            await PrepararComplementosAsync(actual);
            if(Huella(actual) != op.Huella)
                throw new InvalidOperationException("BANCI cambió después de validar. Rechace este envío y vuelva a validar los archivos del Consolidado.");
            if(estado == "CONFIRMADO_ACTUALIZACION")
            {
                var propuesta = await PropuestaAsync(db, op, tx);
                await IntegrarCargaAsync(db, tx, op, propuesta, usuario, cruce: true);
                await RegistrarReclasificacionesAsync(db, tx, op, propuesta, usuario);
            }
        }
        var destino = estado == "EXPIRADO_ACTUALIZACION" ? "RECHAZADO_USUARIO" : estado;
        await db.ExecuteAsync("UPDATE dbo.banci_carga SET estado=@destino,motivo_resolucion=@motivo,fecha_confirmacion=COALESCE(fecha_confirmacion,SYSUTCDATETIME()),fecha_aprobacion=CASE WHEN @destino IN('CONFIRMADO_ACTUALIZACION','RECHAZADO_ADMIN') THEN SYSUTCDATETIME() ELSE NULL END,id_usuario_aprobacion=CASE WHEN @destino IN('CONFIRMADO_ACTUALIZACION','RECHAZADO_ADMIN') THEN @usuario ELSE NULL END WHERE id_banci_carga=@Id;", new
        {
            destino,
            motivo,
            usuario,
            op.Id
        }, tx);
        await BitacoraAsync(db, tx, op.Id, usuario, op.Estado, destino, "Resolución conjunta del Consolidado. " + motivo);
    }
}
