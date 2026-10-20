using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;

public sealed partial class BanciMensualService
{
    public async Task<object> ValidarAsync(int usuario, BanciLibro libro, byte[] original, string nombre, bool manual = false)
    {
        await AccesoAsync(usuario, libro.Entidad, libro.Actualizacion ? "ACTUALIZACION" : "CARGA");
        libro.Manual = manual;
        using(var consultaPendientes = factory.CrearConexion())
            foreach(var pendiente in await PropuestasEnRevisionAsync(consultaPendientes, libro))
                if(libro.Actualizacion || SeSuperponen(libro, pendiente)) throw new BanciEnvioPendienteException();
        var origen = await PrecargaAsync(usuario, libro.Entidad, libro.Mes, libro.Anio, libro.Actualizacion, manual);
        var tieneOrigen = origen.Carpetas.Count > 0;
        origen = SeleccionarOrigen(origen, libro);
        await reglas.CargarAsync();
        var errores = new List<CargaValidacionError>();
        var advertencias = new List<CargaValidacionError>();
        foreach(var (_, filas) in Grupos(libro))
        {
            if(filas.Select(f => f.NumeroFila).Distinct().Count() != filas.Count || filas.Any(f => f.NumeroFila is null or < 2))
                for(var i = 0; i < filas.Count; i++)
                    filas[i].NumeroFila = i + 2;
        }

        if(libro.Manual && !libro.Actualizacion && libro.Carpetas.Count != 1)
            errores.Add(Error("carpetas", new ArchivoFila(), "registros", "La captura manual debe enviar una carpeta de la lista a la vez, con sus delitos y víctimas."));
        if(!libro.Actualizacion)
            errores.AddRange(await ValidarCarpetasNuevasAsync(libro));
        var protegido = tieneOrigen;
        if(protegido)
            errores.AddRange(CompararOrigen(origen, libro));
        if(libro.Actualizacion && CompararOrigen(origen, libro).Count == 0 && !HayCambiosEditables(origen, libro))
            errores.Add(Error("victimas", new ArchivoFila(), "actualizacion", "Modifique al menos un dato personal o de localización antes de enviar la actualización."));
        if(!protegido || libro.Actualizacion)
        {
            foreach(var fila in libro.Victimas)
            {
                var fisica = Valor(fila, "id_tv") == reglas.Clave("catalogo_tipo_victima", "PERSONA_FISICA");
                foreach(var (campo, tabla) in new[]
                {
                    ("sexo", "catalogo_sexo"),
                    ("genero", "catalogo_genero"),
                    ("pob", "catalogo_pertenece_poblacion_indigena"),
                    ("disc", "catalogo_presenta_discapacidad"),
                    ("nacional", "catalogo_nacionalidad")
                }

                )
                    if(Valor(fila, campo) is "" or "0")
                        fila.Columnas[campo] = fisica ? reglas.Clave(tabla, "NO_IDENTIFICADO") : campo is "pob" or "disc" ? reglas.Clave(tabla, "NO_APLICA") : null;
                if(!libro.Actualizacion && Valor(fila, "id_tpm") == "0")
                    fila.Columnas["id_tpm"] = null;
            }
        }

        foreach(var fila in libro.Victimas)
        {
            foreach(var campo in new[]
            {
                "curp",
                "rfc"
            }

            )
                if(Valor(fila, campo) != "")
                    fila.Columnas[campo] = Valor(fila, campo).ToUpperInvariant();
            if(Valor(fila, "localizado_o_no_localizado") == "")
                fila.Columnas["localizado_o_no_localizado"] = reglas.Clave("banci_catalogo_localizacion", "NO_LOCALIZADO");
        }

        NormalizarLocalizacion(libro, reglas.Clave("banci_catalogo_localizacion", "NO_LOCALIZADO"));
        errores.AddRange(await ValidarPersistenciaAsync(libro));
        errores.AddRange(integridad.Validar(libro.Carpetas, libro.Delitos, libro.Victimas));
        if(libro.Carpetas.Count == 0 || libro.Delitos.Count == 0 || libro.Victimas.Count == 0)
            errores.Add(Error("general", new ArchivoFila(), "registros", "Debe incluir las carpetas, delitos y víctimas del corte."));
        // La desactivación de contenido nunca permite duplicados, romper relaciones ni alterar el origen.
        if(configuracion.Activa("BANCI", "VALIDACIONES_CAMPOS"))
        {
            errores.AddRange(carpetasValidator.Validar(libro.Carpetas, false));
            foreach(var fila in libro.Carpetas)
                if(DateTime.TryParseExact(Valor(fila, "fha_de_ini"), ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var inicio)
                    && (inicio.Month != libro.Mes
                    || inicio.Year != libro.Anio))
                    errores.Add(Error("carpetas", fila, "fha_de_ini", "La fecha de inicio debe pertenecer al corte seleccionado."));
            errores.AddRange(delitosValidator.Validar(libro.Delitos));
            errores.AddRange(await victimasValidator.ValidarAsync(libro.Victimas));
            errores.AddRange(await catalogosValidator.ValidarAsync(libro.Carpetas, libro.Delitos, libro.Victimas));
            errores.AddRange(await ValidarComplementosAsync(libro));
            var coordenadas = await catalogosValidator.ValidarCoordenadasConfiguradasAsync(libro.Delitos, "BANCI", configuracion);
            errores.AddRange(coordenadas.Errores);
            if(coordenadas.Advertencia != null)
                advertencias.Add(coordenadas.Advertencia);
            advertencias.AddRange(delitosValidator.ValidarAdvertencias(libro.Delitos, configuracion.Activa("BANCI", "COORDENADAS_FORMATO_RANGO"), configuracion.Activa("BANCI", "COORDENADAS_SIN_INFORMACION"), configuracion.Activa("BANCI", "COORDENADAS_CONCENTRACION")));
            if(errores.Count == 0 && configuracion.Activa("BANCI", "RENAPO"))
            {
                var grupos = libro.Victimas.Where(f => Valor(f, "curp") != "").GroupBy(f => Valor(f, "curp")).ToArray();
                foreach(var lote in grupos.Chunk(4))
                {
                    var resultados = await Task.WhenAll(lote.Select(async g => new { Grupo = g, Resultado = await renapo.ConsultarAsync(g.Key) }));
                    foreach(var consulta in resultados)
                    {
                        if(consulta.Resultado.Estado == EstadoConsultaRenapo.Encontrada)
                            continue;
                        if(consulta.Resultado.Estado == EstadoConsultaRenapo.NoDisponible)
                            advertencias.Add(Error("victimas", consulta.Grupo.First(), "curp", "RENAPO no está disponible. La CURP solo pudo validarse localmente."));
                        else
                            errores.Add(Error("victimas", consulta.Grupo.First(), "curp", "RENAPO no validó la CURP. Revise el dato y la disponibilidad del servicio."));
                    }

                    if(advertencias.Count > 0)
                        break;
                }
            }
        }

        using var db = factory.CrearConexion();
        var clases = (await db.QueryAsync<string>("SELECT clave FROM dbo.banci_catalogo_clasificacion_delito WHERE activo=1;")).ToHashSet();
        foreach(var fila in libro.Delitos.Where(f => !clases.Contains(Valor(f, "clasf_de_dto"))))
            errores.Add(Error("delitos", fila, "clasf_de_dto", "El delito no pertenece al catálogo de desaparición de BANCI."));
        if(errores.Count > 0)
            return new
            {
                esValido = false,
                errores,
                advertencias
            };
        if(libro.Actualizacion)
        {
            ConservarCambios(origen, libro);
            origen = SeleccionarOrigen(origen, libro);
            if(libro.Victimas.Count == 0)
                return new
                {
                    esValido = false,
                    errores = new[] { Error("victimas", new ArchivoFila(), "actualizacion", "No hay registros modificados para enviar.") },
                    advertencias
                };
        }
        if(original.Length > 50 * 1024 * 1024)
            throw new InvalidOperationException("El archivo supera 50 MB.");
        var referencia = Guid.NewGuid();
        var ruta = RutaOriginal(referencia, nombre);
        if(!manual) Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        db.Open();
        using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            if(!manual) await File.WriteAllBytesAsync(ruta, original);
            var bloqueo = await db.ExecuteScalarAsync<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@recurso,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @r;", new
            {
                recurso = $"BANCI:{libro.Entidad}:{libro.Anio}:{libro.Mes}"
            }, tx);
            if(bloqueo < 0)
                throw new InvalidOperationException("El corte está siendo procesado. Intente nuevamente.");
            var pendientes = (await db.QueryAsync<BanciOperacion>(BanciMensualSql.Operaciones + " WHERE b.id_entidad_federativa=@Entidad AND b.mes_corte=@Mes AND b.anio_corte=@Anio AND b.estado IN('VALIDADO','PENDIENTE_APROBACION');", libro, tx)).ToList();
            ValidarDisponibilidadActualizacion(libro.Actualizacion, pendientes.Any(p => p.Estado == "PENDIENTE_APROBACION"));
            foreach(var pendiente in pendientes)
            {
                var contenido = await PropuestaAsync(db, pendiente, tx);
                contenido.Manual = (JsonSerializer.Deserialize<BanciLibro>(pendiente.Snapshot ?? "{}", Json) ?? new BanciLibro()).Manual;
                if(!SeSuperponen(libro, contenido))
                    continue;
                if(pendiente.Estado == "PENDIENTE_APROBACION" || pendiente.Usuario != usuario)
                    throw new BanciEnvioPendienteException();
                await db.ExecuteAsync("UPDATE dbo.banci_carga SET estado='RECHAZADO_USUARIO',motivo_resolucion=N'Sustituido por una nueva validación del remitente.' WHERE id_banci_carga=@Id;", pendiente, tx);
                await BitacoraAsync(db, tx, pendiente.Id, usuario, pendiente.Estado, "RECHAZADO_USUARIO", "Sustituido por una nueva validación del remitente.");
            }

            var id = await db.ExecuteScalarAsync<long>("""
                INSERT dbo.banci_carga(codigo_referencia,id_entidad_federativa,mes_corte,anio_corte,tipo_carga,estado,
                    id_usuario_carga,es_historica,snapshot_origen,huella_origen,id_carga_consolidado)
                VALUES(@referencia,@Entidad,@Mes,@Anio,@tipo,'VALIDADO',@usuario,@historica,@snapshot,@huella,
                    (SELECT MAX(id_carga) FROM dbo.carga WHERE id_entidad_federativa=@Entidad AND mes_corte=@Mes AND anio_corte=@Anio AND activo=1 AND estado IN('CONFIRMADO','CONFIRMADO_ACTUALIZACION')));
                SELECT CONVERT(bigint,SCOPE_IDENTITY());
                """, new
            {
                referencia,
                libro.Entidad,
                libro.Mes,
                libro.Anio,
                tipo = libro.Actualizacion ? "ACTUALIZACION" : "CARGA",
                usuario,
                historica = origen.Carpetas.Count == 0,
                snapshot = Serializar(origen),
                huella = Huella(origen)
            }, tx);
            foreach(var (tabla, filas) in Grupos(libro))
                foreach(var fila in filas)
                    await db.ExecuteAsync($"INSERT dbo.banci_carga_tmp_{tabla}(id_banci_carga,numero_fila,datos_propuestos) VALUES(@id,@fila,@json);", new
                    {
                        id,
                        fila = fila.NumeroFila,
                        json = Serializar(fila)
                    }, tx);
            if(!manual) await db.ExecuteAsync("INSERT dbo.banci_carga_archivo(id_banci_carga,nombre_original,ruta_relativa,huella_sha256,longitud_bytes) VALUES(@id,@nombre,@ruta,@huella,@longitud);", new
            {
                id,
                nombre = NombreOriginal(nombre),
                ruta = RutaRelativaOriginal(referencia, nombre),
                huella = Convert.ToHexString(SHA256.HashData(original)),
                longitud = original.LongLength
            }, tx);
            await BitacoraAsync(db, tx, id, usuario, null, "VALIDADO", "Validación de carga BANCI.");
            tx.Commit();
            return new
            {
                esValido = true,
                referencia,
                errores,
                advertencias,
                carpetas = libro.Carpetas.Count,
                delitos = libro.Delitos.Count,
                victimas = libro.Victimas.Count
            };
        }
        catch
        {
            tx.Rollback();
            if(!manual) File.Delete(ruta);
            throw;
        }
    }

    private async Task<List<CargaValidacionError>> ValidarCarpetasNuevasAsync(BanciLibro libro)
    {
        using var db = factory.CrearConexion();
        var errores = new List<CargaValidacionError>();
        foreach(var lote in libro.Carpetas.Chunk(500))
        {
            var registradas = (await db.QueryAsync<(string Id, string Nomenclatura, int Mes, int Anio)>("""
                SELECT id_ci AS Id,ntra_ci AS Nomenclatura,mes_corte AS Mes,anio_corte AS Anio
                FROM dbo.banci_carpeta_investigacion
                WHERE id_entidad_federativa=@Entidad
                  AND ((mes_corte=@Mes AND anio_corte=@Anio AND id_ci IN @ids) OR ntra_ci IN @nomenclaturas);
                """, new
            {
                libro.Entidad,
                libro.Mes,
                libro.Anio,
                ids = lote.Select(f => Valor(f, "id_ci")).ToArray(),
                nomenclaturas = lote.Select(f => Valor(f, "ntra_ci")).ToArray()
            })).ToArray();
            foreach(var fila in lote)
            {
                var repetida = registradas.Any(r =>
                    (r.Mes == libro.Mes && r.Anio == libro.Anio
                        && string.Equals(r.Id, Valor(fila, "id_ci"), StringComparison.OrdinalIgnoreCase))
                    || string.Equals(r.Nomenclatura, Valor(fila, "ntra_ci"), StringComparison.OrdinalIgnoreCase));
                if(repetida)
                    errores.Add(Error("carpetas", fila, "id_ci", "La carpeta ya está registrada en BANCI con este identificador o nomenclatura. Descargue una nueva plantilla de las carpetas pendientes o utilice Actualización."));
            }
        }
        return errores;
    }

    public static bool HayCambiosEditables(BanciLibro origen, BanciLibro propuesta)
    {
        var anteriores = origen.Victimas.GroupBy(f => Llave(f, 3)).ToDictionary(g => g.Key, g => g.First());
        return propuesta.Victimas.Any(f => !anteriores.TryGetValue(Llave(f, 3), out var anterior) || Editables(true).Any(c => Valor(f, c) != Valor(anterior, c)));
    }

    private async Task<List<CargaValidacionError>> ValidarComplementosAsync(BanciLibro libro)
    {
        var errores = new List<CargaValidacionError>();
        using var db = factory.CrearConexion();
        var motivos = (await db.QueryAsync<(byte Condicion, string Clave)>(BanciMensualSql.Motivos)).ToHashSet();
        var localizacion = reglas.Clave("banci_catalogo_localizacion", "LOCALIZADO");
        foreach(var fila in libro.Victimas)
        {
            foreach(var campo in Complementos)
            {
                var limite = campo is "acciones_busqueda" or "obs" ? 50000 : campo == "motivo_desaparicion" ? 1000 : campo == "curp" ? 18 : campo == "rfc" ? 13 : campo == "nomb" || campo == "estado_migratorio" ? 500 : 250;
                if(Valor(fila, campo).Length > limite)
                    errores.Add(Error("victimas", fila, campo, $"Máximo {limite} caracteres."));
            }

            if(Valor(fila, "curp") != "" && !CurpFormatoValidator.EsFormatoValido(Valor(fila, "curp")))
                errores.Add(Error("victimas", fila, "curp", "La estructura, fecha o dígito de la CURP no es válido."));
            foreach(var (campo, tabla) in new[]
            {
                ("localizado_o_no_localizado", "banci_catalogo_localizacion"),
                ("con_o_sin_vida", "banci_catalogo_condicion_vida"),
                ("constitutiva_delito", "banci_catalogo_constitutiva_delito")
            }

            )
                if(Valor(fila, campo) != ""
                    && !await db.ExecuteScalarAsync<bool>($"SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.{tabla} WHERE CONVERT(nvarchar(20),clave)=@valor AND activo=1) THEN 1 ELSE 0 END);", new
                    {
                        valor = Valor(fila, campo)
                    }))
                    errores.Add(Error("victimas", fila, campo, "Seleccione una opción vigente del catálogo."));
            if(Valor(fila, "localizado_o_no_localizado") == localizacion)
            {
                if(!DateTime.TryParseExact(Valor(fila, "fecha_localizacion"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
                    || !FechaRangoValidator.EsValida(fecha, FechaRangoValidator.Hoy()))
                    errores.Add(Error("victimas", fila, "fecha_localizacion", "Indique una fecha de localización válida dentro de los últimos 120 años, sin fechas futuras."));
                if(fecha != default)
                {
                    var carpeta = libro.Carpetas.FirstOrDefault(c => Valor(c, "id_ci") == Valor(fila, "id_ci"));
                    var delito = libro.Delitos.FirstOrDefault(d => Llave(d, 2) == Llave(fila, 2));
                    if(carpeta != null
                        && DateTime.TryParseExact(Valor(carpeta, "fha_de_ini"), ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var inicio)
                        && fecha.Date < inicio.Date)
                        errores.Add(Error("victimas", fila, "fecha_localizacion", "La localización no puede ser anterior al inicio de la carpeta."));
                    if(delito != null
                        && DateTime.TryParseExact(Valor(delito, "fha_de_hchos"), ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var hechos)
                        && fecha.Date < hechos.Date)
                        errores.Add(Error("victimas", fila, "fecha_localizacion", "La localización no puede ser anterior a los hechos."));
                }

                if(Valor(fila, "con_o_sin_vida") == "")
                    errores.Add(Error("victimas", fila, "con_o_sin_vida", "Indique la condición de vida."));
                if(!byte.TryParse(Valor(fila, "constitutiva_delito"), out var condicion)
                    || !motivos.Contains((condicion, Valor(fila, "motivo_desaparicion"))))
                    errores.Add(Error("victimas", fila, "motivo_desaparicion", "Seleccione la condición y su motivo en el catálogo correspondiente."));
            }
            else if(Valor(fila, "fecha_localizacion") != ""
                || Valor(fila, "con_o_sin_vida") != ""
                || Valor(fila, "constitutiva_delito") != ""
                || Valor(fila, "motivo_desaparicion") != "")
                errores.Add(Error("victimas", fila, "localizado_o_no_localizado", "Los datos de localización requieren seleccionar Persona localizada."));
        }

        return errores;
    }

    private static string NombreOriginal(string nombre) => Path.GetFileName(nombre.Replace('\\', '/'));
    public static string RutaRelativaOriginal(Guid referencia, string nombre) => Path.Combine(referencia.ToString("N"), NombreOriginal(nombre));
    private string RutaOriginal(Guid referencia, string nombre) => Path.Combine(ambiente.ContentRootPath, "ArchivosOriginalesBanci", RutaRelativaOriginal(referencia, nombre));
    private string RutaOriginal(Guid referencia) => Path.Combine(ambiente.ContentRootPath, "ArchivosOriginalesBanci", referencia.ToString("N") + ".xlsx");
    private static (string Tabla, List<ArchivoFila> Filas)[] Grupos(BanciLibro libro) => [("carpeta", libro.Carpetas), ("delito", libro.Delitos), ("victima", libro.Victimas)];
    private static Task BitacoraAsync(IDbConnection db,
        IDbTransaction tx,
        long id,
        int usuario,
        string? anterior,
        string estado,
        string motivo) => db.ExecuteAsync("INSERT dbo.banci_carga_bitacora_estado(id_banci_carga,estado_anterior,estado_nuevo,id_usuario,comentario) VALUES(@id,@anterior,@estado,@usuario,@motivo);", new
        {
            id,
            anterior,
            estado,
            usuario,
            motivo
        }, tx);
    private static async Task<BanciLibro> PropuestaAsync(IDbConnection db, BanciOperacion op, IDbTransaction? tx = null)
    {
        var libro = new BanciLibro
        {
            Entidad = op.Entidad,
            Mes = op.Mes,
            Anio = op.Anio,
            Actualizacion = op.Tipo == "ACTUALIZACION"
        };
        foreach(var (tabla, filas) in Grupos(libro))
            foreach(var dato in await db.QueryAsync<string>($"SELECT datos_propuestos FROM dbo.banci_carga_tmp_{tabla} WHERE id_banci_carga=@Id ORDER BY numero_fila;", new
            {
                op.Id
            }, tx))
                filas.Add(JsonSerializer.Deserialize<ArchivoFila>(dato, Json)!);
        return libro;
    }

    public async Task<object> ListarAsync(int usuario, int anio, string reporte)
    {
        var acceso = await AccesoAsync(usuario, null, reporte is "cargas" or "originales" or "pendientes" ? "ADMIN" : null);
        if(anio < 1900 || anio > FechaRangoValidator.Hoy().Year)
            throw new InvalidOperationException("Año inválido.");
        using var db = factory.CrearConexion();
        var filas = (await db.QueryAsync<BanciOperacion>("SELECT Id,Referencia,Entidad,EntidadNombre,Mes,Anio,Tipo,Estado,Usuario,NombreUsuario,Fecha,Motivo,Manual,TieneOriginal,FechaEnvio FROM (" + BanciMensualSql.Operaciones + ") b WHERE b.Anio=@anio AND(@admin=1 OR b.Entidad=@entidad) AND(@pendientes=0 OR b.Estado='PENDIENTE_APROBACION') AND(@originales=0 OR (b.Manual=0 AND b.TieneOriginal=1)) AND(@envios=0 OR b.Estado IN('PENDIENTE_APROBACION','CONFIRMADO','CONFIRMADO_ACTUALIZACION','RECHAZADO_ADMIN')) ORDER BY COALESCE(b.FechaEnvio,b.Fecha) DESC;", new
        {
            anio,
            admin = AlcanceNacional(acceso),
            entidad = acceso.IdEntidadFederativa,
            originales = reporte == "originales",
            envios = reporte == "envios",
            pendientes = reporte == "pendientes"
        })).ToList();
        return filas.Select(f => new { f.Id, f.Referencia, f.Entidad, f.EntidadNombre, f.Mes, f.Anio, f.Tipo, f.Estado, f.NombreUsuario, Fecha = DateTime.SpecifyKind(f.FechaEnvio ?? f.Fecha, DateTimeKind.Utc), f.Motivo, f.Manual, TieneOriginal = !f.Manual && f.TieneOriginal });
    }

    public async Task<BanciLibro> DetalleAsync(int usuario, Guid referencia)
    {
        using var db = factory.CrearConexion();
        var op = await ObtenerAsync(db, referencia);
        await AccesoAsync(usuario, op.Entidad);
        var propuesta = await PropuestaAsync(db, op);
        if(op.Estado.StartsWith("CONFIRMADO", StringComparison.Ordinal) || op.Estado == "PENDIENTE_APROBACION")
        {
            var folios = (await db.QueryAsync<(string Ci, string Ntra, string No)>("SELECT id_ci,ntra_ci,no_banci FROM dbo.banci_carpeta_investigacion WHERE id_entidad_federativa=@Entidad AND mes_corte=@Mes AND anio_corte=@Anio;", op)).ToList();
            foreach(var carpeta in propuesta.Carpetas)
            {
                var folio = folios.FirstOrDefault(f => f.Ci == Valor(carpeta, "id_ci") && f.Ntra == Valor(carpeta, "ntra_ci"));
                if(folio.No != null)
                    carpeta.Columnas["no_banci"] = folio.No;
            }
        }

        var origen = JsonSerializer.Deserialize<BanciLibro>(op.Snapshot ?? "{}", Json) ?? new BanciLibro();
        var previas = origen.Victimas.GroupBy(v => Llave(v, 3)).ToDictionary(g => g.Key, g => g.First());
        foreach(var fila in propuesta.Victimas)
        {
            previas.TryGetValue(Llave(fila, 3), out var previa);
            foreach(var campo in Editables(propuesta.Actualizacion))
            {
                var anterior = previa == null ? "" : Valor(previa, campo);
                var nuevo = Valor(fila, campo);
                if(anterior != nuevo)
                    propuesta.Cambios.Add(new BanciCambioVista(Valor(fila, "id_ci"), Valor(fila, "id_delito"), Valor(fila, "id_vicf"), campo, anterior, nuevo));
            }
        }

        return propuesta;
    }

    private static async Task<BanciOperacion> ObtenerAsync(IDbConnection db,
        Guid referencia,
        IDbTransaction? tx = null) => await db.QuerySingleOrDefaultAsync<BanciOperacion>(BanciMensualSql.Operaciones + " WHERE b.codigo_referencia=@referencia;", new
        {
            referencia
        }, tx) ?? throw new KeyNotFoundException("El envío no existe.");
    public async Task<(byte[] Bytes, string Nombre)> OriginalAsync(int usuario, Guid referencia)
    {
        using var db = factory.CrearConexion();
        var op = await ObtenerAsync(db, referencia);
        await AccesoAsync(usuario, op.Entidad);
        if(op.Manual || !op.TieneOriginal)
            throw new InvalidOperationException("Los archivos originales están disponibles únicamente para cargas y actualizaciones masivas.");
        var nombre = await db.QuerySingleAsync<string>("SELECT nombre_original FROM dbo.banci_carga_archivo WHERE id_banci_carga=@Id;", new
        {
            op.Id
        });
        var ruta = RutaOriginal(referencia, nombre);
        if(!File.Exists(ruta)) ruta = RutaOriginal(referencia);
        if(!File.Exists(ruta))
            throw new KeyNotFoundException("El archivo original no está disponible en este ambiente.");
        return (await File.ReadAllBytesAsync(ruta), nombre);
    }

    public async Task<object> ResolverAsync(int usuario, Guid referencia, BanciDecision decision, bool administrativa)
    {
        using var db = factory.CrearConexion();
        db.Open();
        using var tx = db.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            var op = await ObtenerAsync(db, referencia, tx);
            var acceso = await AccesoAsync(usuario, op.Entidad, administrativa ? "ADMIN" : op.Tipo);
            if(!administrativa && op.Usuario != usuario)
                throw new UnauthorizedAccessException("Solo el remitente puede confirmar este envío.");
            var esperado = administrativa ? "PENDIENTE_APROBACION" : "VALIDADO";
            if(op.Estado != esperado)
                throw new InvalidOperationException("El envío cambió de estado. Actualice la consulta.");
            if(!decision.Aceptar && string.IsNullOrWhiteSpace(decision.Motivo))
                throw new InvalidOperationException("Indique el motivo del rechazo.");
            if(decision.Motivo.Length > 2000)
                throw new InvalidOperationException("El motivo supera 2000 caracteres.");
            if(!administrativa && decision.Aceptar && op.Tipo == "ACTUALIZACION")
            {
                var bloqueoCorte = await db.ExecuteScalarAsync<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@recurso,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @r;", new { recurso = $"BANCI:{op.Entidad}:{op.Anio}:{op.Mes}" }, tx);
                if(bloqueoCorte < 0) throw new InvalidOperationException("El corte está siendo procesado. Intente nuevamente.");
                var otroPendiente = await db.ExecuteScalarAsync<bool>("SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.banci_carga WHERE id_entidad_federativa=@Entidad AND mes_corte=@Mes AND anio_corte=@Anio AND estado='PENDIENTE_APROBACION' AND id_banci_carga<>@Id) THEN 1 ELSE 0 END);", op, tx);
                ValidarDisponibilidadActualizacion(true, otroPendiente);
            }
            var estado = decision.Aceptar ? "PENDIENTE_APROBACION" : administrativa ? "RECHAZADO_ADMIN" : "RECHAZADO_USUARIO";
            // Igual que mensual: el enlace envía a revisión; el superusuario integra directamente.
            if(decision.Aceptar && (administrativa || acceso.Rol == "SUPER_USUARIO"))
            {
                var bloqueo = await db.ExecuteScalarAsync<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@recurso,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @r;", new
                {
                    recurso = $"BANCI:{op.Entidad}:{op.Anio}:{op.Mes}"
                }, tx);
                if(bloqueo < 0)
                    throw new InvalidOperationException("El corte está siendo procesado. Intente nuevamente.");
                var snapshot = JsonSerializer.Deserialize<BanciLibro>(op.Snapshot ?? "{}", Json) ?? new BanciLibro();
                using var documento = JsonDocument.Parse(op.Snapshot ?? "{}");
                var manual = documento.RootElement.TryGetProperty("manual", out _) ? snapshot.Manual : op.Tipo == "CARGA";
                var propuesta = await PropuestaAsync(db, op, tx);
                propuesta.Manual = manual;
                var actual = await OrigenAsync(db, op.Entidad, op.Mes, op.Anio, op.Tipo == "ACTUALIZACION", tx);
                if(op.Tipo == "CARGA")
                    await ExcluirCarpetasRegistradasAsync(db, actual, tx, incluirEnRevision: true, excluirCarga: op.Id);
                await PrepararComplementosAsync(actual);
                actual = SeleccionarOrigen(actual, propuesta);

                if(Huella(actual) != op.Huella)
                    throw new InvalidOperationException("Los datos de origen cambiaron después de validar. Genere una nueva plantilla y envíe nuevamente.");
                if(op.Tipo == "CARGA")
                    await IntegrarCargaAsync(db, tx, op, propuesta, usuario);
                else
                    await IntegrarActualizacionAsync(db, tx, op, propuesta, usuario);
                estado = op.Tipo == "CARGA" ? "CONFIRMADO" : "CONFIRMADO_ACTUALIZACION";
            }

            await db.ExecuteAsync("UPDATE dbo.banci_carga SET estado=@estado,motivo_resolucion=@motivo,fecha_confirmacion=COALESCE(fecha_confirmacion,SYSUTCDATETIME()),id_usuario_aprobacion=CASE WHEN @resuelto=1 THEN @usuario ELSE NULL END,fecha_aprobacion=CASE WHEN @resuelto=1 THEN SYSUTCDATETIME() ELSE NULL END WHERE id_banci_carga=@Id;", new
            {
                estado,
                motivo = decision.Motivo,
                usuario,
                resuelto = administrativa || estado.StartsWith("CONFIRMADO"),
                op.Id
            }, tx);
            await BitacoraAsync(db, tx, op.Id, usuario, op.Estado, estado, decision.Motivo);
            tx.Commit();
            return new
            {
                esValido = true,
                estado
            };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static async Task IntegrarCargaAsync(IDbConnection db, IDbTransaction tx, BanciOperacion op, BanciLibro libro, int usuario)
    {
        foreach(var carpeta in libro.Carpetas)
        {
            var existentes = (await db.QueryAsync<(long Id, int Mes, int Anio, string Ci, string Ntra)>("""
                SELECT id_banci_carpeta_investigacion AS Id,mes_corte AS Mes,anio_corte AS Anio,id_ci AS Ci,ntra_ci AS Ntra
                FROM dbo.banci_carpeta_investigacion WITH(UPDLOCK,HOLDLOCK)
                WHERE id_entidad_federativa=@Entidad AND ((mes_corte=@Mes AND anio_corte=@Anio AND id_ci=@ci) OR ntra_ci=@ntra);
                """, new
            {
                op.Entidad,
                op.Mes,
                op.Anio,
                ci = Valor(carpeta, "id_ci"),
                ntra = Valor(carpeta, "ntra_ci")
            }, tx)).ToArray();
            if(existentes.Length > 0 && (libro.Manual || existentes.Length != 1
                || existentes[0].Mes != op.Mes || existentes[0].Anio != op.Anio
                || !string.Equals(existentes[0].Ci, Valor(carpeta, "id_ci"), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(existentes[0].Ntra, Valor(carpeta, "ntra_ci"), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("La carpeta ya está registrada en BANCI con otro identificador o periodo. Actualice la plantilla.");
            long id;
            if(existentes.Length == 1)
            {
                id = existentes[0].Id;
                await ActualizarRegistroAsync(db, tx, op.Id, usuario, "banci_carpeta_investigacion", "id_banci_carpeta_investigacion", id,
                    carpeta, ["id_ci", "ntra_ci", "fha_de_ini", "hra_de_ini", "rmen_de_hchos"], "CARPETA");
            }
            else
            {
                var numero = await db.ExecuteScalarAsync<long>("""
                    IF NOT EXISTS(SELECT 1 FROM dbo.banci_consecutivo_carpeta WITH(UPDLOCK,HOLDLOCK) WHERE id_entidad_federativa=@Entidad AND anio=@Anio)
                        INSERT dbo.banci_consecutivo_carpeta(id_entidad_federativa,anio,ultimo_consecutivo) VALUES(@Entidad,@Anio,0);
                    UPDATE dbo.banci_consecutivo_carpeta SET ultimo_consecutivo=ultimo_consecutivo+1 OUTPUT inserted.ultimo_consecutivo WHERE id_entidad_federativa=@Entidad AND anio=@Anio;
                    """, op, tx);
                var no = $"BANCI/{op.Entidad:D2}/{op.Anio}/{numero:D6}";
                id = await db.ExecuteScalarAsync<long>("""
                    INSERT dbo.banci_carpeta_investigacion(id_banci_carga,id_entidad_federativa,mes_corte,anio_corte,id_ci,ntra_ci,no_banci,fha_de_ini,hra_de_ini,rmen_de_hchos)
                    VALUES(@Id,@Entidad,@Mes,@Anio,@ci,@ntra,@no,@fecha,@hora,@resumen); SELECT CONVERT(bigint,SCOPE_IDENTITY());
                    """, new
                {
                    op.Id,
                    op.Entidad,
                    op.Mes,
                    op.Anio,
                    ci = Valor(carpeta, "id_ci"),
                    ntra = Valor(carpeta, "ntra_ci"),
                    no,
                    fecha = Fecha(carpeta, "fha_de_ini"),
                    hora = Hora(carpeta, "hra_de_ini"),
                    resumen = Dato(carpeta, "rmen_de_hchos")
                }, tx);
            }
            foreach(var delito in libro.Delitos.Where(d => Valor(d, "id_ci") == Valor(carpeta, "id_ci")))
            {
                var idDelito = await db.QuerySingleOrDefaultAsync<long?>("SELECT id_banci_delito FROM dbo.banci_delito WHERE id_banci_carpeta_investigacion=@id AND id_delito=@delito;", new
                {
                    id,
                    delito = Valor(delito, "id_delito")
                }, tx);
                if(idDelito.HasValue)
                    await ActualizarRegistroAsync(db, tx, op.Id, usuario, "banci_delito", "id_banci_delito", idDelito.Value, delito, ColumnasDelito, "DELITO");
                else
                {
                    var datos = Parametros(delito, ColumnasDelito);
                    datos.Add("padre", id);
                    datos.Add("carga", op.Id);
                    idDelito = await InsertarAsync(db, tx, "banci_delito", "id_banci_carpeta_investigacion", ColumnasDelito, datos);
                }
                foreach(var victima in libro.Victimas.Where(v => Llave(v, 2) == Llave(delito, 2)))
                {
                    var idVictima = await db.QuerySingleOrDefaultAsync<long?>("SELECT id_banci_victima FROM dbo.banci_victima WHERE id_banci_delito=@idDelito AND id_vicf=@victima;", new
                    {
                        idDelito,
                        victima = Valor(victima, "id_vicf")
                    }, tx);
                    if(idVictima.HasValue)
                        await ActualizarRegistroAsync(db, tx, op.Id, usuario, "banci_victima", "id_banci_victima", idVictima.Value, victima, ColumnasVictima, "VICTIMA");
                    else
                    {
                        var datos = Parametros(victima, ColumnasVictima);
                        datos.Add("padre", idDelito);
                        datos.Add("carga", op.Id);
                        await InsertarAsync(db, tx, "banci_victima", "id_banci_delito", ColumnasVictima, datos);
                    }
                }
            }
        }
    }

    // Solo se escriben campos que cambiaron; los folios y el historial sobreviven a una carga masiva completa.
    private static async Task ActualizarRegistroAsync(IDbConnection db, IDbTransaction tx, long carga, int usuario,
        string tabla, string llave, long id, ArchivoFila fila, string[] columnas, string tipo)
    {
        var anterior = (IDictionary<string, object>)await db.QuerySingleAsync($"SELECT * FROM dbo.{tabla} WHERE {llave}=@id;", new
        {
            id
        }, tx);
        var parametros = Parametros(fila, columnas);
        static string Normalizar(object? dato) => dato is DateTime fecha ? fecha.ToString("yyyy-MM-dd") : Convert.ToString(dato, CultureInfo.InvariantCulture)?.Trim() ?? "";
        var cambios = columnas.Where(c => Normalizar(anterior[c]) != Normalizar(parametros.Get<object?>(c))).ToList();
        if(!(bool)anterior["activo"])
        {
            cambios.Add("activo");
            parametros.Add("activo", true);
        }
        if(cambios.Count == 0)
            return;
        foreach(var campo in cambios)
            await db.ExecuteAsync("""
                INSERT dbo.banci_historial_cambio(id_banci_carga,tipo_registro,identificador_registro,campo,valor_anterior,valor_nuevo,id_usuario)
                VALUES(@carga,@tipo,@id,@campo,@antes,@despues,@usuario);
                """, new
            {
                carga,
                tipo,
                id,
                campo,
                antes = Normalizar(anterior[campo]),
                despues = Normalizar(parametros.Get<object?>(campo)),
                usuario
            }, tx);
        parametros.Add("id", id);
        await db.ExecuteAsync($"UPDATE dbo.{tabla} SET {string.Join(',', cambios.Select(c => c + "=@" + c))} WHERE {llave}=@id;", parametros, tx);
    }

    // Las columnas SQL salen de listas del modelo, nunca de encabezados del usuario.
    private static readonly string[] ColumnasDelito = ["id_delito", "dto", "moda_dto", "forma_acc", "fha_de_hchos", "hra_de_hchos", "emto_com_dto", "grdo_cons", "clasf_de_dto", "nom_ent_hchos", "id_ent_hchos", "nom_mun_hchos", "id_mun_hchos", "nom_loc_hchos", "id_loc_hchos", "nom_col_hchos", "id_col_hchos", "cp", "coord_x", "coord_y", "dom_hchos"];
    private static readonly string[] ColumnasVictima = ["id_vicf", "id_tv", "id_tpm", "sexo", "genero", "pob", "disc", "fha_nac", "edad", "nacional", .. Complementos];
    private static DateTime? Fecha(ArchivoFila fila, string campo)
    {
        var valor = Valor(fila, campo);
        if(valor == "")
            return null;
        if(DateTime.TryParseExact(valor, ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
            return fecha;
        throw new InvalidOperationException($"Fecha no interpretable: {campo}, fila {fila.NumeroFila}.");
    }

    private static TimeSpan? Hora(ArchivoFila fila, string campo)
    {
        var valor = Valor(fila, campo);
        if(valor == "")
            return null;
        if(TimeSpan.TryParse(valor, CultureInfo.InvariantCulture, out var hora) && hora >= TimeSpan.Zero && hora < TimeSpan.FromDays(1))
            return hora;
        throw new InvalidOperationException($"Hora no interpretable: {campo}, fila {fila.NumeroFila}.");
    }

    private static DynamicParameters Parametros(ArchivoFila fila, IEnumerable<string> columnas)
    {
        var parametros = new DynamicParameters();
        foreach(var campo in columnas)
        {
            var origen = campo == "primer_apellido_vicfem" ? "1apellido_vicfem" : campo == "segundo_apellido_vicfem" ? "2apellido_vicfem" : campo;
            object? valor = Dato(fila, origen);
            if(campo.StartsWith("fha_") || campo == "fecha_localizacion")
                valor = Fecha(fila, origen);
            if(campo.StartsWith("hra_"))
                valor = Hora(fila, origen);
            if(campo is "coord_x" or "coord_y" && valor != null)
                valor = decimal.Parse(Valor(fila, origen), NumberStyles.Number, CultureInfo.InvariantCulture);
            parametros.Add(campo, valor);
        }

        return parametros;
    }

    private static Task<long> InsertarAsync(IDbConnection db,
        IDbTransaction tx,
        string tabla,
        string padre,
        string[] columnas,
        DynamicParameters datos) => db.ExecuteScalarAsync<long>($"INSERT dbo.{tabla}(id_banci_carga,{padre},{string.Join(',', columnas)}) VALUES(@carga,@padre,{string.Join(',', columnas.Select(c => '@' + c))}); SELECT CONVERT(bigint,SCOPE_IDENTITY());", datos, tx);
    private static async Task IntegrarActualizacionAsync(IDbConnection db,
        IDbTransaction tx,
        BanciOperacion op,
        BanciLibro libro,
        int usuario)
    {
        foreach(var fila in libro.Victimas)
        {
            var anterior = await db.QuerySingleAsync("""
                SELECT v.* FROM dbo.banci_victima v JOIN dbo.banci_delito d ON d.id_banci_delito=v.id_banci_delito
                JOIN dbo.banci_carpeta_investigacion ci ON ci.id_banci_carpeta_investigacion=d.id_banci_carpeta_investigacion
                WHERE ci.id_entidad_federativa=@Entidad AND ci.mes_corte=@Mes AND ci.anio_corte=@Anio AND ci.id_ci=@ci AND d.id_delito=@delito AND v.id_vicf=@victima AND ci.activo=1 AND d.activo=1 AND v.activo=1;
                """, new
            {
                op.Entidad,
                op.Mes,
                op.Anio,
                ci = Valor(fila, "id_ci"),
                delito = Valor(fila, "id_delito"),
                victima = Valor(fila, "id_vicf")
            }, tx);
            var id = (long)anterior.id_banci_victima;
            var parametros = Parametros(fila, Editables(true));
            parametros.Add("victima", id);
            static string Normalizar(object? dato) => dato is DateTime fecha ? fecha.ToString("yyyy-MM-dd") : Convert.ToString(dato, CultureInfo.InvariantCulture)?.Trim() ?? "";
            var previos = (IDictionary<string, object>)anterior;
            var cambios = Editables(true).Where(c => Normalizar(previos[c]) != Normalizar(parametros.Get<object?>(c))).ToArray();
            if(cambios.Length == 0)
                continue;
            foreach(var campo in cambios)
                await db.ExecuteAsync("INSERT dbo.banci_historial_cambio(id_banci_carga,tipo_registro,identificador_registro,campo,valor_anterior,valor_nuevo,id_usuario) VALUES(@Id,'VICTIMA',@victima,@campo,@antes,@despues,@usuario);", new
                {
                    op.Id,
                    victima = id,
                    campo,
                    antes = Normalizar(previos[campo]),
                    despues = Normalizar(parametros.Get<object?>(campo)),
                    usuario
                }, tx);
            await db.ExecuteAsync($"UPDATE dbo.banci_victima SET {string.Join(',', cambios.Select(c => c + "=@" + c))} WHERE id_banci_victima=@victima;", parametros, tx);
        }
    }
}
