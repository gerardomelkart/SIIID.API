using System.Reflection;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;
using SIIID2.Api.Services;

var pruebas = 0;
void Verificar(bool condicion, string caso)
{
    if(!condicion)
        throw new Exception(caso);
    pruebas++;
}

var reglas = new ReglasSimuladas();
var fisica = new ArchivoFila
{
    NumeroFila = 1,
    Columnas = new()
    {
        ["id_ci"] = "ci",
        ["id_delito"] = "del",
        ["id_vicf"] = "vic",
        ["id_tv"] = "16",
        ["id_tpm"] = "26",
        ["sexo"] = "9",
        ["genero"] = "9",
        ["pob"] = "9",
        ["disc"] = "9",
        ["fha_nac"] = null,
        ["edad"] = null,
        ["nacional"] = "9"
    }
};
Verificar((await new VictimasValidator(reglas).ValidarAsync([fisica])).Count == 0 && fisica.Columnas["edad"] == "999", "Persona física con claves de tipo y No identificado distintas");
var moral = new ArchivoFila
{
    NumeroFila = 2,
    Columnas = new(fisica.Columnas)
    {
        ["id_tv"] = "18",
        ["id_tpm"] = "9",
        ["sexo"] = null,
        ["genero"] = null,
        ["pob"] = "24",
        ["disc"] = "34",
        ["edad"] = null,
        ["nacional"] = null
    }
};
Verificar((await new VictimasValidator(reglas).ValidarAsync([moral])).Count == 0, "Persona moral usa No aplica de ambos catálogos");
moral.Columnas["pob"] = "4";
Verificar((await new VictimasValidator(reglas).ValidarAsync([moral])).Count > 0, "No aceptar la clave anterior de No aplica");
// Las reglas locales de feminicidio mantienen su alcance con códigos distintos en ambos módulos.
var victimaFem = new ArchivoFila
{
    NumeroFila = 1,
    Columnas = new()
    {
        ["id_ci"] = "ci",
        ["id_delito"] = "del",
        ["nacional"] = "173",
        ["nombre_vicfem"] = "",
        ["1apellido_vicfem"] = "",
        ["2apellido_vicfem"] = "",
        ["curp_vicfem"] = ""
    }
};
foreach(var (modulo, clasificacion) in new[]
{
    ("MENSUAL", "fem-local"),
    ("FEDERAL", "fem-federal")
}

)
{
    var delitoFem = new ArchivoFila
    {
        Columnas = new()
        {
            ["id_ci"] = "ci",
            ["id_delito"] = "del",
            ["clasf_de_dto"] = clasificacion,
            ["grdo_cons"] = "21"
        }
    };
    var validacionFem = await new FeminicidioVictimaValidator(reglas).ValidarAsync([delitoFem], [victimaFem], modulo);
    Verificar(validacionFem.Errores.Count > 0 || validacionFem.Advertencias.Count > 0, "Regla local de feminicidio con claves nuevas: " + modulo);
    delitoFem.Columnas["grdo_cons"] = "22";
    validacionFem = await new FeminicidioVictimaValidator(reglas).ValidarAsync([delitoFem], [victimaFem], modulo);
    Verificar(validacionFem.Errores.Count == 0 && validacionFem.Advertencias.Count == 0, "Tentativa conserva alcance opcional: " + modulo);
}

// Ejecutar las reglas reales de semanal con claves diferentes de las semillas actuales.
var semanal = new SemanalCargaService(null!, null!, null!, null!, null!, reglas, null!, null!, null!, null!, null!, null!, null!);
void ReglaSemanal(string nombre,
    params object[] argumentos) => typeof(SemanalCargaService).GetMethod(nombre, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(semanal, argumentos);
var filaRegla = new ArchivoFila
{
    NumeroFila = 1,
    Columnas = new()
    {
        ["id_ci"] = "ci",
        ["id_delito"] = "del",
        ["clasf_de_dto"] = "robo-test",
        ["forma_acc"] = "91"
    }
};
var filasRegla = new List<ArchivoFila>
{
    filaRegla
};
var erroresRegla = new List<CargaValidacionError>();
ReglaSemanal("ValidarRoboVehiculoFormaAccion", filasRegla, erroresRegla);
Verificar(erroresRegla.Count == 1 && erroresRegla[0].Valor == "91", "Semanal: forma desconocida usa el catálogo");
filaRegla.Columnas["forma_acc"] = "92";
erroresRegla.Clear();
ReglaSemanal("ValidarRoboVehiculoFormaAccion", filasRegla, erroresRegla);
Verificar(erroresRegla.Count == 0, "Semanal: forma identificada conserva aceptación");
filaRegla.Columnas["clasf_de_dto"] = "les-test";
filaRegla.Columnas["emto_com_dto"] = "77";
ReglaSemanal("ValidarLesionesDolosasElementoComision", filasRegla, erroresRegla);
Verificar(erroresRegla.Count == 1 && erroresRegla[0].Valor!.Contains("77"), "Semanal: advertencia de instrumento usa claves vigentes");
filaRegla.Columnas["clasf_de_dto"] = "hom.01";
filaRegla.Columnas["grdo_cons"] = "22";
erroresRegla.Clear();
ReglaSemanal("ValidarHomicidioTentativa", filasRegla, erroresRegla);
Verificar(erroresRegla.Count == 1, "Semanal: tentativa usa significado vigente");
filaRegla.Columnas["clasf_de_dto"] = "hom-doloso";
filaRegla.Columnas["grdo_cons"] = "31";
erroresRegla.Clear();
ReglaSemanal("ValidarHomicidioDolosoGradoConsumacion", filasRegla, erroresRegla);
Verificar(erroresRegla.Count == 1, "Semanal: grado desconocido usa significado vigente");
var victimaRegla = new ArchivoFila
{
    Columnas = new()
    {
        ["id_ci"] = "ci",
        ["id_delito"] = "del",
        ["id_tv"] = "16"
    }
};
erroresRegla.Clear();
ReglaSemanal("ValidarHomicidioDolosoTipoVictima", filasRegla, new List<ArchivoFila> { victimaRegla }, erroresRegla);
Verificar(erroresRegla.Count == 0, "Semanal: permite persona física vigente");
victimaRegla.Columnas["id_tv"] = "18";
ReglaSemanal("ValidarHomicidioDolosoTipoVictima", filasRegla, new List<ArchivoFila> { victimaRegla }, erroresRegla);
Verificar(erroresRegla.Count == 1, "Semanal: rechaza persona moral vigente para homicidio");
filaRegla.Columnas["clasf_de_dto"] = "ext-test";
filaRegla.Columnas["grdo_cons"] = null;
ReglaSemanal("NormalizarGradoConsumacionExtorsion", filasRegla);
Verificar(filaRegla.Columnas["grdo_cons"] == "31", "Semanal: grado omitido en extorsión usa significado vigente");
// Contratos antiguos retirados: tampoco deben volver por registro de DI o controladores.
var assembly = typeof(SIIID2.Api.Controllers.BanciMensualController).Assembly;
foreach(var nombre in new[]
{
    "BanciCruceService",
    "BanciCruceController",
    "BanciCargasController",
    "BanciActualizacionesController",
    "BanciAltaNormalizer",
    "BanciArchivoReader",
    "BanciActualizacionRepository"
}

)
    Verificar(!assembly.GetTypes().Any(t => t.Name == nombre), "Contrato retirado: " + nombre);
Verificar(assembly.GetTypes().Any(t => t.Name == "BanciUsuariosController"), "Se conserva administración de usuarios BANCI");
Verificar(!CurpFormatoValidator.EsFormatoValido("GODE020230HDFRRN00"), "Rechazar fecha imposible de CURP sin RENAPO");
Verificar(!CurpFormatoValidator.EsFormatoValido("dato invalido"), "Conservar validación local de CURP");
var curpBase = "GODE561231HDFRRN0";
const string alfabeto = "0123456789ABCDEFGHIJKLMNÑOPQRSTUVWXYZ";
var suma = Enumerable.Range(0, 17).Sum(i => alfabeto.IndexOf(curpBase[i]) * (18 - i));
var curpValida = curpBase + ((10 - suma % 10) % 10);
Verificar(CurpFormatoValidator.EsFormatoValido(curpValida), "Aceptar CURP válida sin consulta externa");
Verificar(!CurpFormatoValidator.EsFormatoValido(curpBase + ((curpValida[17] - '0' + 1) % 10)), "Rechazar dígito verificador incorrecto");
foreach(var modulo in new[]
{
    "MENSUAL",
    "FEDERAL"
}

)
{
    var configuracion = new SistemaConfiguracionService(null!);
    typeof(SistemaConfiguracionService).GetProperty("Version")!.SetValue(configuracion, 1L);
    configuracion.Modulos.Add(new SistemaModulo { Clave = modulo, Activo = true });
    var feminicidio = new SistemaOpcion
    {
        Modulo = modulo,
        Clave = "FEMINICIDIO_DATOS_ADICIONALES",
        Habilitado = false,
        Disponible = true
    };
    var renapo = new SistemaOpcion
    {
        Modulo = modulo,
        Clave = "RENAPO",
        Habilitado = true,
        Disponible = true
    };
    configuracion.Opciones.AddRange([feminicidio, renapo]);
    Verificar(!configuracion.Activa(modulo, "RENAPO"), "Apagar feminicidio apaga RENAPO: " + modulo);
    feminicidio.Habilitado = true;
    renapo.Habilitado = false;
    Verificar(configuracion.Activa(modulo, "FEMINICIDIO_DATOS_ADICIONALES") && !configuracion.Activa(modulo, "RENAPO"), "Apagar RENAPO conserva reglas locales: " + modulo);
}

BanciLibro LibroPrueba() => new()
{
    Entidad = 9,
    Mes = 9,
    Anio = 2026,
    Carpetas = [new ArchivoFila
    {
        Columnas = new()
        {
            ["id_ci"] = "CI-1",
            ["ntra_ci"] = "PRUEBA-1",
            ["fha_de_ini"] = "2026-09-01"
        }
    }

    ],
    Delitos = [new ArchivoFila
    {
        Columnas = new()
        {
            ["id_ci"] = "CI-1",
            ["id_delito"] = "D-1",
            ["dto"] = "Dato protegido"
        }
    }

    ],
    Victimas = [new ArchivoFila
    {
        Columnas = new()
        {
            ["id_ci"] = "CI-1",
            ["id_delito"] = "D-1",
            ["id_vicf"] = "V-1",
            ["id_tv"] = "18",
            ["nomb"] = "Antes"
        }
    }

    ]
};
var origenBanci = LibroPrueba();
var propuestaBanci = LibroPrueba();
propuestaBanci.Victimas[0].Columnas["nomb"] = "Después";
Verificar(BanciMensualService.CompararOrigen(origenBanci, propuestaBanci).Count == 0, "Permitir editar complementos sin modificar el consolidado");
propuestaBanci.Carpetas[0].Columnas["ntra_ci"] = "ALTERADA";
Verificar(BanciMensualService.CompararOrigen(origenBanci, propuestaBanci).Any(e => e.Campo == "ntra_ci"), "Bloquear nomenclatura modificada");
propuestaBanci = LibroPrueba();
propuestaBanci.Victimas.Clear();
Verificar(BanciMensualService.CompararOrigen(origenBanci, propuestaBanci).Any(e => e.Archivo == "victimas"), "Bloquear omisión de víctima");
propuestaBanci = LibroPrueba();
propuestaBanci.Victimas[0].Columnas["id_vicf"] = "OTRA";
Verificar(BanciMensualService.CompararOrigen(origenBanci, propuestaBanci).Count == 2, "Detectar cambio de identidad como faltante y extra");
propuestaBanci = LibroPrueba();
propuestaBanci.Delitos.Add(propuestaBanci.Delitos[0]);
Verificar(BanciMensualService.CompararOrigen(origenBanci, propuestaBanci).Any(e => e.Archivo == "delitos"), "Bloquear duplicados aunque se conserven todos los originales");
propuestaBanci = LibroPrueba();
propuestaBanci.Victimas[0].Columnas["id_tv"] = "16";
Verificar(BanciMensualService.CompararOrigen(origenBanci, propuestaBanci).Any(e => e.Campo == "id_tv"), "Conservar el tipo de víctima del consolidado");
// Las cargas adicionales excluyen lo aprobado sin romper las relaciones del libro.
var adicionales = new BanciLibro
{
    Carpetas = [new ArchivoFila { Columnas = new() { ["id_ci"] = "CI-ANTERIOR" } }, new ArchivoFila { Columnas = new() { ["id_ci"] = "CI-NUEVA" } }],
    Delitos = [new ArchivoFila { Columnas = new() { ["id_ci"] = "CI-ANTERIOR" } }, new ArchivoFila { Columnas = new() { ["id_ci"] = "CI-NUEVA" } }],
    Victimas = [new ArchivoFila { Columnas = new() { ["id_ci"] = "CI-ANTERIOR" } }, new ArchivoFila { Columnas = new() { ["id_ci"] = "CI-NUEVA" } }]
};
BanciMensualService.FiltrarNuevas(adicionales, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ci-anterior" });
Verificar(adicionales.Carpetas.Count == 1 && adicionales.Delitos.Count == 1 && adicionales.Victimas.Count == 1 && adicionales.Victimas[0].Columnas["id_ci"] == "CI-NUEVA", "La carga adicional incluye solo nuevas carpetas y sus hijos");
var identidad = new ArchivoFila { Columnas = new() { ["nomb"] = "Nombre BANCI", ["nombre_vicfem"] = "Origen", ["1apellido_vicfem"] = "Apellido", ["curp_vicfem"] = "CURP-DE-ORIGEN" } };
BanciMensualService.PrecargarIdentidad(identidad);
Verificar(identidad.Columnas["nomb"] == "Nombre BANCI" && identidad.Columnas["pro_apellido"] == "Apellido" && identidad.Columnas["curp"] == "CURP-DE-ORIGEN", "Precargar identidad sin sobrescribir datos BANCI existentes");
var sinCambios = new BanciLibro { Victimas = [identidad] };
var cambioEditable = new BanciLibro { Victimas = [new ArchivoFila { Columnas = new(identidad.Columnas) }] };
Verificar(!BanciMensualService.HayCambiosEditables(sinCambios, cambioEditable), "No enviar actualización sin cambios");
cambioEditable.Victimas[0].Columnas["nomb"] = "Nombre actualizado";
Verificar(BanciMensualService.HayCambiosEditables(sinCambios, cambioEditable), "Detectar modificación de identidad editable");
var mensualBanci = new BanciMensualService(null!, null!, null!, new CarpetasValidator(), new DelitosValidator(), new VictimasValidator(reglas), null!, null!, reglas, null!, null!);
var libroExcel = LibroPrueba();
libroExcel.Victimas[0].Columnas["curp"] = "TEXTO18CARACTERES";
libroExcel.Carpetas[0].Columnas["id_ci"] = "00000000000000000001";
var bytesExcel = mensualBanci.Excel(libroExcel);
using(var streamExcel = new MemoryStream(bytesExcel))
{
    var archivoExcel = new Microsoft.AspNetCore.Http.FormFile(streamExcel, 0, streamExcel.Length, "archivo", "BANCI.xlsx");
    var leido = await mensualBanci.LeerExcelAsync(archivoExcel, 9, 9, 2026, false);
    Verificar(leido.Carpetas[0].Columnas["id_ci"] == "00000000000000000001", "Excel conserva IDs largos y ceros iniciales como texto");
    Verificar(leido.Victimas[0].Columnas["curp"] == "TEXTO18CARACTERES", "Excel conserva complementos en la tercera pestaña");
    Verificar(leido.Carpetas.Count == 1 && leido.Delitos.Count == 1 && leido.Victimas.Count == 1, "Excel de tres pestañas conserva las cantidades");
}

var origenActualizacion = LibroPrueba();
var propuestaActualizacion = LibroPrueba();
origenActualizacion.Victimas[0].Columnas["sexo"] = "10";
propuestaActualizacion.Victimas[0].Columnas["sexo"] = "11";
Verificar(BanciMensualService.CompararOrigen(origenActualizacion, propuestaActualizacion).Count > 0, "Los datos personales del origen están protegidos en carga inicial");
propuestaActualizacion.Actualizacion = true;
Verificar(BanciMensualService.CompararOrigen(origenActualizacion, propuestaActualizacion).Count == 0, "Una actualización permite modificar datos personales de BANCI");
// Manual es un subconjunto autorizado; masiva sigue exigiendo la totalidad del mensual.
var total = LibroPrueba();
var segunda = LibroPrueba();
foreach(var fila in segunda.Carpetas.Concat(segunda.Delitos).Concat(segunda.Victimas)) fila.Columnas["id_ci"] = "CI-2";
total.Carpetas.AddRange(segunda.Carpetas);
total.Delitos.AddRange(segunda.Delitos);
total.Victimas.AddRange(segunda.Victimas);
var parcial = LibroPrueba();
Verificar(BanciMensualService.CompararOrigen(total, parcial).Count == 3, "Masiva rechaza una carpeta faltante y sus hijos");
parcial.Manual = true;
var seleccionado = BanciMensualService.SeleccionarOrigen(total, parcial);
Verificar(BanciMensualService.CompararOrigen(seleccionado, parcial).Count == 0, "Manual permite una carpeta de la lista con sus hijos");
parcial.Victimas.Clear();
Verificar(BanciMensualService.CompararOrigen(BanciMensualService.SeleccionarOrigen(total, parcial), parcial).Count > 0, "Alta manual no permite omitir víctimas de la carpeta elegida");
parcial = LibroPrueba();
parcial.Manual = true;
parcial.Actualizacion = true;
parcial.Victimas[0].Columnas["nomb"] = "Modificado";
seleccionado = BanciMensualService.SeleccionarOrigen(total, parcial);
Verificar(BanciMensualService.CompararOrigen(seleccionado, parcial).Count == 0, "Actualización manual permite modificar una víctima de varias");
var conSinCambios = LibroPrueba();
conSinCambios.Actualizacion = true;
conSinCambios.Carpetas.AddRange(segunda.Carpetas);
conSinCambios.Delitos.AddRange(segunda.Delitos);
conSinCambios.Victimas.AddRange(segunda.Victimas);
conSinCambios.Victimas[0].Columnas["nomb"] = "Modificado";
BanciMensualService.ConservarCambios(total, conSinCambios);
Verificar(conSinCambios.Victimas.Count == 1 && conSinCambios.Carpetas.Count == 1 && conSinCambios.Delitos.Count == 1, "Se excluyen registros sin cambios y sus padres no afectados");
parcial = LibroPrueba();
parcial.Manual = true;
parcial.Carpetas[0].Columnas["id_ci"] = "FUERA-DE-LISTA";
Verificar(BanciMensualService.CompararOrigen(BanciMensualService.SeleccionarOrigen(total, parcial), parcial).Count > 0, "Manual no acepta carpetas fuera del mensual");
var noLocalizada = LibroPrueba();
noLocalizada.Victimas[0].Columnas["localizado_o_no_localizado"] = "49";
noLocalizada.Victimas[0].Columnas["con_o_sin_vida"] = "valor inválido ignorado";
noLocalizada.Victimas[0].Columnas["fecha_localizacion"] = "fecha inválida ignorada";
BanciMensualService.NormalizarLocalizacion(noLocalizada, "49");
Verificar(noLocalizada.Victimas[0].Columnas["con_o_sin_vida"] == null && noLocalizada.Victimas[0].Columnas["fecha_localizacion"] == null, "No localizada ignora condición de vida y datos residuales antes de validar/persistir");
noLocalizada.Victimas[0].Columnas["localizado_o_no_localizado"] = "45";
noLocalizada.Victimas[0].Columnas["con_o_sin_vida"] = "1";
BanciMensualService.NormalizarLocalizacion(noLocalizada, "49");
Verificar(noLocalizada.Victimas[0].Columnas["con_o_sin_vida"] == "1", "Localizada conserva condición de vida para su validación");
var envioUno = LibroPrueba();
envioUno.Manual = true;
var envioDos = segunda;
envioDos.Manual = true;
Verificar(!BanciMensualService.SeSuperponen(envioUno, envioDos), "Permitir envíos manuales pendientes de carpetas distintas");
Verificar(BanciMensualService.SeSuperponen(envioUno, envioUno), "Bloquear dos envíos pendientes del mismo registro");
envioDos.Manual = false;
Verificar(BanciMensualService.SeSuperponen(envioUno, envioDos), "Una masiva pendiente bloquea otros envíos del corte");
var acuseLibro = LibroPrueba();
acuseLibro.Carpetas[0].Columnas["no_banci"] = "BANCI/09/2026/000001";
var operacionAcuse = new BanciOperacion { Referencia = Guid.NewGuid(), EntidadNombre = "Ciudad de México", Mes = 9, Anio = 2026, Estado = "CONFIRMADO", Tipo = "CARGA", NombreUsuario = "Prueba" };
var catalogosAcuse = new Dictionary<string, Dictionary<string, string>>();
var pdfAcuse = BanciMensualService.GenerarAcuse(operacionAcuse, acuseLibro, new DateTime(2026, 10, 15, 22, 0, 0), catalogosAcuse);
Verificar(System.Text.Encoding.ASCII.GetString(pdfAcuse, 0, 5) == "%PDF-", "Se genera el acuse PDF confirmado con datos y folios BANCI");
// La ruta opcional permite revisar texto y paginación durante la verificación local.
var salidaAcuse = Environment.GetEnvironmentVariable("BANCI_ACUSE_PRUEBA");
if(!string.IsNullOrEmpty(salidaAcuse)) File.WriteAllBytes(salidaAcuse, pdfAcuse);
operacionAcuse.Estado = "PENDIENTE_APROBACION";
Verificar(BanciMensualService.GenerarAcuse(operacionAcuse, acuseLibro, DateTime.UtcNow, catalogosAcuse).Length > 1000, "El acuse pendiente se distingue del confirmado");
Console.WriteLine($"{pruebas} verificaciones aprobadas.");
sealed class ReglasSimuladas : ICatalogoReglasRepository
{
    public Task CargarAsync() => Task.CompletedTask;
    public string Clave(string catalogo, string codigo) => (catalogo, codigo) switch
    {
        ("catalogo_tipo_victima", "PERSONA_FISICA") => "16",
        ("catalogo_tipo_victima", "PERSONA_MORAL") => "18",
        ("catalogo_tipo_victima", "OTRO") => "19",
        ("catalogo_tipo_victima", "NO_IDENTIFICADO") => "20",
        ("catalogo_tipo_victima_moral", "NO_IDENTIFICADO") => "26",
        ("catalogo_tipo_victima_moral", "OTRO") => "25",
        ("catalogo_pertenece_poblacion_indigena", "NO_APLICA") => "24",
        ("catalogo_presenta_discapacidad", "NO_APLICA") => "34",
        ("catalogo_modalidad_delito", "FEMINICIDIO") => "fem-local",
        ("federal_catalogo_modalidad_delito", "FEMINICIDIO") => "fem-federal",
        ("catalogo_nacionalidad", "MEXICANA") => "173",
        ("catalogo_grado_consumacion", "CONSUMADO") => "21",
        ("catalogo_delito", "HOMICIDIO") => "hom",
        ("catalogo_forma_accion", "NO_IDENTIFICADO") => "91",
        ("catalogo_grado_consumacion", "TENTATIVA") => "22",
        ("catalogo_grado_consumacion", "NO_IDENTIFICADO") => "31",
        ("catalogo_instrumento_comision", "OTRO") => "66",
        ("catalogo_instrumento_comision", "NO_IDENTIFICADO") => "77",
        ("catalogo_instrumento_comision", "NO_APLICA") => "88",
        _ => throw new NotSupportedException()
    };
    public int ClaveNumerica(string catalogo, string codigo) => int.Parse(Clave(catalogo, codigo));
    public bool Contiene(string catalogo, string codigo, string? clave) => (catalogo, codigo, clave) switch
    {
        ("catalogo_modalidad_delito", "ROBO_VEHICULO", "robo-test") => true,
        ("catalogo_modalidad_delito", "EXTORSION", "ext-test") => true,
        ("catalogo_subtipo_delito", "LESIONES_DOLOSAS", "les-test") => true,
        ("catalogo_subtipo_delito", "HOMICIDIO_DOLOSO", "hom-doloso") => true,
        _ => false
    };
}
