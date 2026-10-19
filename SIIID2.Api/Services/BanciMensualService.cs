using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;
// Comparte validadores con mensual y mantiene staging separado de los registros aprobados.
public sealed partial class BanciMensualService(IDbConnectionFactory factory,
    IBanciUsuarioRepository usuarios,
    SistemaConfiguracionService configuracion,
    CarpetasValidator carpetasValidator,
    DelitosValidator delitosValidator,
    VictimasValidator victimasValidator,
    CatalogosValidator catalogosValidator,
    CargaIntegridadValidator integridad,
    ICatalogoReglasRepository reglas,
    IWebHostEnvironment ambiente,
    IRenapoCurpService renapo)
{
    public static readonly string[] Complementos = ["fub", "pro_apellido", "sdo_apellido", "nomb", "entidad_nacimiento", "estado_migratorio", "curp", "rfc", "localizado_o_no_localizado", "con_o_sin_vida", "fecha_localizacion", "constitutiva_delito", "motivo_desaparicion", "acciones_busqueda", "obs"];
    public static readonly string[] Personales = ["sexo", "genero", "pob", "disc", "fha_nac", "edad", "nacional"];
    public static string[] Editables(bool actualizacion) => actualizacion ? [.. Personales, .. Complementos] : Complementos;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Serializar(object valor) => JsonSerializer.Serialize(valor, Json);
    private static string Valor(ArchivoFila fila, string campo) => fila.Columnas.GetValueOrDefault(campo)?.Trim() ?? "";
    private static object? Dato(ArchivoFila fila,
        string campo) => string.IsNullOrWhiteSpace(Valor(fila, campo)) ? null : Valor(fila, campo);
    private static string Llave(ArchivoFila fila,
        int nivel) => Serializar(new[]
    {
        Valor(fila, "id_ci"),
        Valor(fila, "id_delito"),
        Valor(fila, "id_vicf")
    }.Take(nivel));
    private static List<ArchivoFila> Filas(IEnumerable<dynamic> filas) => filas.Select((fila,
        i) => new ArchivoFila
        {
            NumeroFila = i + 2,
            Columnas = ((IDictionary<string, object>)fila).ToDictionary(p => p.Key, p => p.Value is null or DBNull ? null : Convert.ToString(p.Value, CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase)
        }).ToList();
    private static string Huella(BanciLibro libro) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serializar(new[]
    {
        libro.Carpetas,
        libro.Delitos,
        libro.Victimas
    }.Select(filas => filas.Select(f => f.Columnas.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray()).OrderBy(x => Serializar(x), StringComparer.Ordinal))))));
    private static bool AlcanceNacional(BanciUsuarioDetalle acceso) => acceso.Rol == "SUPER_USUARIO" || acceso.Rol == "CONSULTA" && !acceso.IdEntidadFederativa.HasValue;
    private async Task<BanciUsuarioDetalle> AccesoAsync(int usuario, int? entidad = null, string? permiso = null)
    {
        var acceso = await usuarios.ObtenerDetalleAsync(usuario);
        if(acceso is null || !acceso.Activo || !acceso.HabilitaBanci)
            throw new UnauthorizedAccessException("No tiene acceso activo a BANCI.");
        if(entidad.HasValue && !AlcanceNacional(acceso) && acceso.IdEntidadFederativa != entidad)
            throw new UnauthorizedAccessException("La entidad no corresponde a su usuario.");
        if(permiso == "ADMIN" && acceso.Rol != "SUPER_USUARIO")
            throw new UnauthorizedAccessException("Esta operación requiere un superusuario.");
        if(permiso == "CARGA" && !acceso.HabilitaCarga || permiso == "ACTUALIZACION" && !acceso.HabilitaModificacion)
            throw new UnauthorizedAccessException("No tiene habilitado el permiso para esta operación.");
        return acceso;
    }

    private async Task PeriodoAsync(int entidad, int mes, int anio, bool actualizacion)
    {
        await configuracion.CargarAsync();
        var correspondiente = FechaRangoValidator.Hoy().AddMonths(-1);
        if(mes < 1 || mes > 12 || anio < correspondiente.Year - FechaRangoValidator.AntiguedadMaxima || anio > correspondiente.Year)
            throw new InvalidOperationException("Corte mensual inválido.");
        var fecha = new DateTime(anio, mes, 1);
        if(fecha > new DateTime(correspondiente.Year, correspondiente.Month, 1))
            throw new InvalidOperationException("No se pueden reportar cortes futuros.");
        if(!actualizacion
            && fecha < new DateTime(correspondiente.Year, correspondiente.Month, 1)
            && !configuracion.Activa("BANCI", "PERMITIR_CORTES_ANTERIORES"))
            throw new InvalidOperationException("La carga de cortes anteriores está desactivada.");
        using var db = factory.CrearConexion();
        if(!await db.ExecuteScalarAsync<bool>("SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.catalogo_entidad_federativa WHERE id_entidad_federativa=@entidad AND activo=1 AND es_entidad_territorial=1) THEN 1 ELSE 0 END);", new
        {
            entidad
        }))
            throw new InvalidOperationException("Seleccione una entidad territorial activa.");
    }

    public async Task<object> OpcionesAsync(int usuario)
    {
        var acceso = await AccesoAsync(usuario);
        await configuracion.CargarAsync();
        using var db = factory.CrearConexion();
        var entidades = await db.QueryAsync("SELECT id_entidad_federativa AS id,nombre FROM dbo.catalogo_entidad_federativa WHERE activo=1 AND es_entidad_territorial=1 AND(@admin=1 OR id_entidad_federativa=@entidad) ORDER BY nombre;", new
        {
            admin = AlcanceNacional(acceso),
            entidad = acceso.IdEntidadFederativa
        });
        var catalogos = new Dictionary<string, object>();
        foreach(var tabla in new[]
        {
            "banci_catalogo_localizacion",
            "banci_catalogo_condicion_vida",
            "banci_catalogo_constitutiva_delito"
        }

        )
            catalogos[tabla.Replace("banci_catalogo_", "")] = await db.QueryAsync($"SELECT clave,descripcion,codigo_regla AS codigo FROM dbo.{tabla} WHERE activo=1 ORDER BY clave;");
        foreach(var (campo, tabla) in new[]
        {
            ("id_tv", "catalogo_tipo_victima"),
            ("id_tpm", "catalogo_tipo_victima_moral"),
            ("sexo", "catalogo_sexo"),
            ("genero", "catalogo_genero"),
            ("pob", "catalogo_pertenece_poblacion_indigena"),
            ("disc", "catalogo_presenta_discapacidad"),
            ("nacional", "catalogo_nacionalidad"),
            ("forma_acc", "catalogo_forma_accion"),
            ("emto_com_dto", "catalogo_instrumento_comision"),
            ("grdo_cons", "catalogo_grado_consumacion")
        }

        )
            catalogos[campo] = await db.QueryAsync($"SELECT clave,descripcion FROM dbo.{tabla} WHERE activo=1 ORDER BY descripcion;");
        catalogos["clasf_de_dto"] = await db.QueryAsync("SELECT clave,descripcion FROM dbo.banci_catalogo_clasificacion_delito WHERE activo=1 ORDER BY clave;");
        catalogos["id_ent_hchos"] = await db.QueryAsync("SELECT id_entidad_federativa AS clave,nombre AS descripcion FROM dbo.catalogo_entidad_federativa WHERE activo=1 ORDER BY nombre;");
        catalogos["id_mun_hchos"] = await db.QueryAsync("SELECT clave,nombre AS descripcion,id_entidad_federativa AS condicion FROM dbo.catalogo_municipio WHERE activo=1 ORDER BY nombre;");
        catalogos["motivos"] = await db.QueryAsync(BanciMensualSql.Motivos + " ORDER BY descripcion;");
        var corte = FechaRangoValidator.Hoy().AddMonths(-1);
        return new
        {
            entidades,
            catalogos,
            mes = corte.Month,
            anio = corte.Year,
            cortesAnteriores = configuracion.Activa("BANCI", "PERMITIR_CORTES_ANTERIORES"),
            version = configuracion.Version,
            opciones = configuracion.Opciones.Where(o => o.Modulo == "BANCI"),
            complementos = Complementos,
            personales = Personales,
            columnas = new
            {
                carpetas = carpetasValidator.ColumnasObligatorias,
                delitos = delitosValidator.ColumnasObligatorias,
                victimas = victimasValidator.ColumnasObligatorias.Concat(Complementos)
            }
        };
    }

    private async Task<BanciLibro> OrigenAsync(IDbConnection db,
        int entidad,
        int mes,
        int anio,
        bool actualizacion,
        IDbTransaction? tx = null)
    {
        var libro = new BanciLibro
        {
            Entidad = entidad,
            Mes = mes,
            Anio = anio,
            Actualizacion = actualizacion
        };
        if(!actualizacion)
        {
            using var consulta = await db.QueryMultipleAsync(BanciMensualSql.Origen, new
            {
                Entidad = entidad,
                Mes = mes,
                Anio = anio
            }, tx);
            libro.Carpetas = Filas(await consulta.ReadAsync());
            libro.Delitos = Filas(await consulta.ReadAsync());
            libro.Victimas = Filas(await consulta.ReadAsync());
        }
        else
        {
            libro.Carpetas = Filas(await db.QueryAsync("SELECT id_ci,ntra_ci,CONVERT(varchar(10),fha_de_ini,23) AS fha_de_ini,CONVERT(varchar(8),hra_de_ini,108) AS hra_de_ini,rmen_de_hchos,no_banci FROM dbo.banci_carpeta_investigacion WHERE id_entidad_federativa=@entidad AND mes_corte=@mes AND anio_corte=@anio AND activo=1 ORDER BY id_ci;", new
            {
                entidad,
                mes,
                anio
            }, tx));
            var filtro = " FROM dbo.banci_carpeta_investigacion ci JOIN dbo.banci_delito d ON d.id_banci_carpeta_investigacion=ci.id_banci_carpeta_investigacion AND d.activo=1 ";
            var condicion = " WHERE ci.id_entidad_federativa=@entidad AND ci.mes_corte=@mes AND ci.anio_corte=@anio AND ci.activo=1 ";
            libro.Delitos = Filas(await db.QueryAsync("SELECT ci.id_ci,d.*" + filtro + condicion + " ORDER BY ci.id_ci,d.id_delito;", new
            {
                entidad,
                mes,
                anio
            }, tx));
            libro.Victimas = Filas(await db.QueryAsync("SELECT ci.id_ci,d.id_delito,v.*" + filtro + " JOIN dbo.banci_victima v ON v.id_banci_delito=d.id_banci_delito AND v.activo=1 " + condicion + " ORDER BY ci.id_ci,d.id_delito,v.id_vicf;", new
            {
                entidad,
                mes,
                anio
            }, tx));
            foreach(var fila in libro.Delitos.Concat(libro.Victimas))
            {
                foreach(var campo in fila.Columnas.Keys.Where(c => c.StartsWith("id_banci_") || c.EndsWith("_consolidado") || c == "activo").ToArray())
                    fila.Columnas.Remove(campo);
                foreach(var campo in fila.Columnas.Keys.ToArray())
                    if((campo.StartsWith("fha_")
                        || campo == "fecha_localizacion")
                        && DateTime.TryParse(fila.Columnas[campo], CultureInfo.InvariantCulture, out var fecha))
                        fila.Columnas[campo] = fecha.ToString("yyyy-MM-dd");
                if(fila.Columnas.Remove("primer_apellido_vicfem", out var primero))
                    fila.Columnas["1apellido_vicfem"] = primero;
                if(fila.Columnas.Remove("segundo_apellido_vicfem", out var segundo))
                    fila.Columnas["2apellido_vicfem"] = segundo;
            }
        }

        return libro;
    }

    public async Task<BanciLibro> PrecargaAsync(int usuario, int entidad, int mes, int anio, bool actualizacion, bool manual = false)
    {
        await AccesoAsync(usuario, entidad, actualizacion ? "ACTUALIZACION" : "CARGA");
        await PeriodoAsync(entidad, mes, anio, actualizacion);
        using var db = factory.CrearConexion();
        var libro = await OrigenAsync(db, entidad, mes, anio, actualizacion);
        if(actualizacion && libro.Carpetas.Count == 0)
            throw new InvalidOperationException("El corte no tiene carga BANCI aprobada.");
        var tieneOrigenConsolidado = libro.Carpetas.Count > 0;
        if(!actualizacion)
            await ExcluirCarpetasRegistradasAsync(db, libro, incluirEnRevision: true);
        var historica = new DateTime(anio, mes, 1) < new DateTime(FechaRangoValidator.Hoy().AddMonths(-1).Year, FechaRangoValidator.Hoy().AddMonths(-1).Month, 1);
        if(!actualizacion && libro.Carpetas.Count == 0 && (!historica || tieneOrigenConsolidado))
            throw new InvalidOperationException("Ya no hay carpetas pendientes de las reportadas en el SIIID mensual para esta entidad y periodo. Revise Consulta de envíos para conocer el estado de lo enviado; utilice Actualización para modificar registros aprobados.");
        libro.Manual = manual;
        await PrepararComplementosAsync(libro);

        return libro;
    }

    // Ambas modalidades incorporan únicamente carpetas pendientes.
    private static async Task ExcluirCarpetasRegistradasAsync(IDbConnection db, BanciLibro libro, IDbTransaction? tx = null, bool incluirEnRevision = false, long? excluirCarga = null)
    {
        var registradas = (await db.QueryAsync<string>("SELECT id_ci FROM dbo.banci_carpeta_investigacion WHERE id_entidad_federativa=@Entidad AND mes_corte=@Mes AND anio_corte=@Anio;", libro, tx)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(incluirEnRevision)
            registradas.UnionWith(await db.QueryAsync<string>("""
                SELECT JSON_VALUE(t.datos_propuestos,'$.columnas.id_ci')
                FROM dbo.banci_carga_tmp_carpeta t JOIN dbo.banci_carga c ON c.id_banci_carga=t.id_banci_carga
                WHERE c.id_entidad_federativa=@Entidad AND c.mes_corte=@Mes AND c.anio_corte=@Anio
                  AND c.tipo_carga='CARGA' AND c.estado='PENDIENTE_APROBACION'
                  AND (@excluirCarga IS NULL OR c.id_banci_carga<>@excluirCarga);
                """, new { libro.Entidad, libro.Mes, libro.Anio, excluirCarga }, tx));
        FiltrarNuevas(libro, registradas);
    }

    public static void FiltrarNuevas(BanciLibro libro, ISet<string> registradas)
    {
        libro.Carpetas.RemoveAll(f => registradas.Contains(Valor(f, "id_ci")));
        var nuevas = libro.Carpetas.Select(f => Valor(f, "id_ci")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        libro.Delitos.RemoveAll(f => !nuevas.Contains(Valor(f, "id_ci")));
        libro.Victimas.RemoveAll(f => !nuevas.Contains(Valor(f, "id_ci")));
    }

    private async Task PrepararComplementosAsync(BanciLibro libro)
    {
        await reglas.CargarAsync();
        foreach(var fila in libro.Victimas)
        {
            PrecargarIdentidad(fila);
            foreach(var campo in CamposFeminicidio) fila.Columnas.Remove(campo);
            foreach(var campo in Complementos)
                fila.Columnas.TryAdd(campo, null);
            if(Valor(fila, "localizado_o_no_localizado") == "")
                fila.Columnas["localizado_o_no_localizado"] = reglas.Clave("banci_catalogo_localizacion", "NO_LOCALIZADO");
        }
    }

    public static void NormalizarLocalizacion(BanciLibro libro, string noLocalizado)
    {
        foreach(var fila in libro.Victimas.Where(f => Valor(f, "localizado_o_no_localizado") == noLocalizado))
            foreach(var campo in new[] { "con_o_sin_vida", "fecha_localizacion", "constitutiva_delito", "motivo_desaparicion" })
                fila.Columnas[campo] = null;
    }

    // Una carga manual toma una carpeta completa; una actualización toma solo las víctimas enviadas.
    public static BanciLibro SeleccionarOrigen(BanciLibro origen, BanciLibro propuesta)
    {
        if(!propuesta.Manual)
            return origen;
        var carpetas = propuesta.Carpetas.Select(f => Llave(f, 1)).ToHashSet();
        var delitos = propuesta.Delitos.Select(f => Llave(f, 2)).ToHashSet();
        var victimas = propuesta.Victimas.Select(f => Llave(f, 3)).ToHashSet();
        return new BanciLibro
        {
            Entidad = origen.Entidad,
            Mes = origen.Mes,
            Anio = origen.Anio,
            Actualizacion = origen.Actualizacion,
            Manual = true,
            Carpetas = origen.Carpetas.Where(f => carpetas.Contains(Llave(f, 1))).ToList(),
            Delitos = origen.Delitos.Where(f => propuesta.Actualizacion ? delitos.Contains(Llave(f, 2)) : carpetas.Contains(Llave(f, 1))).ToList(),
            Victimas = origen.Victimas.Where(f => propuesta.Actualizacion ? victimas.Contains(Llave(f, 3)) : carpetas.Contains(Llave(f, 1))).ToList()
        };
    }

    public static bool SeSuperponen(BanciLibro primero, BanciLibro segundo)
    {
        // Cada envío reserva sus carpetas; las actualizaciones reservan las víctimas modificadas.
        var nivel = primero.Actualizacion && segundo.Actualizacion ? 3 : 1;
        var claves = (nivel == 3 ? primero.Victimas : primero.Carpetas).Select(f => Llave(f, nivel)).ToHashSet();
        return (nivel == 3 ? segundo.Victimas : segundo.Carpetas).Any(f => claves.Contains(Llave(f, nivel)));
    }

    public static void ConservarCambios(BanciLibro origen, BanciLibro propuesta)
    {
        var anteriores = origen.Victimas.ToDictionary(f => Llave(f, 3));
        propuesta.Victimas.RemoveAll(f => anteriores.TryGetValue(Llave(f, 3), out var previa)
            && Editables(true).All(c => Valor(f, c) == Valor(previa, c)));
        var delitos = propuesta.Victimas.Select(f => Llave(f, 2)).ToHashSet();
        var carpetas = propuesta.Victimas.Select(f => Llave(f, 1)).ToHashSet();
        propuesta.Delitos.RemoveAll(f => !delitos.Contains(Llave(f, 2)));
        propuesta.Carpetas.RemoveAll(f => !carpetas.Contains(Llave(f, 1)));
    }

    // Se conserva el origen protegido y se muestra una sola captura editable de cada dato personal.
    private static readonly string[] CamposFeminicidio = ["nombre_vicfem", "1apellido_vicfem", "2apellido_vicfem", "curp_vicfem", "primer_apellido_vicfem", "segundo_apellido_vicfem"];

    public static void PrecargarIdentidad(ArchivoFila fila)
    {
        foreach(var (editable, origen) in new[]
        {
            ("nomb", "nombre_vicfem"),
            ("pro_apellido", "1apellido_vicfem"),
            ("sdo_apellido", "2apellido_vicfem"),
            ("curp", "curp_vicfem")
        })
            if(Valor(fila, editable) == "" && Valor(fila, origen) != "")
                fila.Columnas[editable] = Valor(fila, origen);
    }

    // La protección del archivo es orientativa. La igualdad se comprueba nuevamente en servidor.
    public byte[] Excel(BanciLibro libro)
    {
        using var wb = new XLWorkbook();
        var grupos = new[]
        {
            ("Carpetas", libro.Carpetas, carpetasValidator.ColumnasObligatorias),
            ("Delitos", libro.Delitos, delitosValidator.ColumnasObligatorias),
            ("Victimas", libro.Victimas, (IReadOnlyCollection<string>)victimasValidator.ColumnasObligatorias.Concat(Complementos).ToArray())
        };
        foreach(var (nombre, filas, baseColumnas) in grupos)
        {
            var hoja = wb.AddWorksheet(nombre);
            var columnas = baseColumnas.Concat(filas.SelectMany(f => f.Columnas.Keys)).Where(c => !CamposFeminicidio.Contains(c, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            for(var c = 0; c < columnas.Length; c++)
            {
                hoja.Cell(1, c + 1).SetValue(columnas[c]);
                hoja.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#6f2c91");
                hoja.Cell(1, c + 1).Style.Font.FontColor = XLColor.White;
                for(var f = 0; f < filas.Count; f++)
                {
                    var celda = hoja.Cell(f + 2, c + 1);
                    celda.Style.NumberFormat.Format = "@";
                    celda.SetValue(Valor(filas[f], columnas[c]));
                    celda.Style.Fill.BackgroundColor = Editables(libro.Actualizacion).Contains(columnas[c]) ? XLColor.LightYellow : XLColor.WhiteSmoke;
                }
            }

            hoja.SheetView.FreezeRows(1);
            hoja.Columns().Width = 24;
            hoja.Row(1).Style.Font.Bold = true;
            if(filas.Count > 0)
                hoja.Range(1, 1, filas.Count + 1, columnas.Length).SetAutoFilter();
        }

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<BanciLibro> LeerExcelAsync(IFormFile archivo, int entidad, int mes, int anio, bool actualizacion)
    {
        if(!Path.GetExtension(archivo.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || archivo.Length > 50 * 1024 * 1024)
            throw new InvalidOperationException("Seleccione un libro .xlsx de hasta 50 MB.");
        await using var stream = archivo.OpenReadStream();
        using var wb = new XLWorkbook(stream);
        if(wb.Worksheets.Count != 3 || new[]
        {
            "Carpetas",
            "Delitos",
            "Victimas"
        }.Any(n => !wb.TryGetWorksheet(n, out _)))
            throw new InvalidOperationException("El libro debe contener las pestañas Carpetas, Delitos y Victimas.");
        var libro = new BanciLibro
        {
            Entidad = entidad,
            Mes = mes,
            Anio = anio,
            Actualizacion = actualizacion
        };
        foreach(var (nombre, destino) in new[]
        {
            ("Carpetas", libro.Carpetas),
            ("Delitos", libro.Delitos),
            ("Victimas", libro.Victimas)
        }

        )
        {
            var h = wb.Worksheet(nombre);
            var ultima = h.LastRowUsed()?.RowNumber() ?? 1;
            var cols = h.LastColumnUsed()?.ColumnNumber() ?? 0;
            if(ultima > 100001 || cols > 100)
                throw new InvalidOperationException("La hoja supera el límite de registros o columnas.");
            var nombres = Enumerable.Range(1, cols).Select(c => h.Cell(1, c).GetString().Trim().ToLowerInvariant()).ToArray();
            if(nombres.Any(string.IsNullOrWhiteSpace) || nombres.Distinct().Count() != nombres.Length)
                throw new InvalidOperationException($"Hay encabezados vacíos o duplicados en {nombre}.");
            for(var r = 2; r <= ultima; r++)
            {
                if(h.Row(r).CellsUsed().All(c => c.IsEmpty()))
                    continue;
                var fila = new ArchivoFila
                {
                    NumeroFila = r
                };
                for(var c = 0; c < cols; c++)
                {
                    var celda = h.Cell(r, c + 1);
                    fila.Columnas[nombres[c]] = celda.DataType == XLDataType.DateTime ? celda.GetDateTime().ToString(nombres[c].StartsWith("hra_") ? "HH:mm:ss" : "yyyy-MM-dd") : celda.GetString().Trim();
                }

                destino.Add(fila);
            }
        }

        return libro;
    }

    public static List<CargaValidacionError> CompararOrigen(BanciLibro origen, BanciLibro propuesta)
    {
        var errores = new List<CargaValidacionError>();
        foreach(var (nombre, antes, despues, nivel) in new[]
        {
            ("carpetas", origen.Carpetas, propuesta.Carpetas, 1),
            ("delitos", origen.Delitos, propuesta.Delitos, 2),
            ("victimas", origen.Victimas, propuesta.Victimas, 3)
        }

        )
        {
            var indice = despues.GroupBy(f => Llave(f, nivel)).ToDictionary(g => g.Key, g => g.ToList());
            foreach(var fila in antes)
            {
                var key = Llave(fila, nivel);
                if(!indice.TryGetValue(key, out var coincidencias) || coincidencias.Count != 1)
                {
                    errores.Add(Error(nombre, fila, "identificadores", "Falta el registro de origen o se repitieron sus identificadores."));
                    continue;
                }

                foreach(var par in fila.Columnas.Where(p => nombre != "victimas" || !Editables(propuesta.Actualizacion).Contains(p.Key)))
                    if((par.Value ?? "").Trim() != Valor(coincidencias[0], par.Key))
                        errores.Add(Error(nombre, coincidencias[0], par.Key, $"El dato protegido debe coincidir con el origen: {par.Value}."));
            }

            var claves = antes.Select(f => Llave(f, nivel)).ToHashSet();
            foreach(var fila in despues.Where(f => !claves.Contains(Llave(f, nivel))))
                errores.Add(Error(nombre, fila, "identificadores", "El registro no pertenece al origen de este corte."));
        }

        return errores;
    }

    private static CargaValidacionError Error(string hoja, ArchivoFila fila, string campo, string mensaje) => new()
    {
        Archivo = hoja,
        Fila = fila.NumeroFila,
        Campo = campo,
        Columna = campo,
        Codigo = "BANCI_VALIDACION",
        Mensaje = mensaje,
        DescripcionResumen = mensaje
    };
}
