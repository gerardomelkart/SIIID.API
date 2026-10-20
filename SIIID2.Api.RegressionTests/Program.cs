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
// Consolidado y BANCI almacenan las coordenadas con escalas decimales distintas.
foreach(var actualizacion in new[] { false, true })
foreach(var manual in new[] { false, true })
{
    var origenCoordenadas = LibroPrueba();
    var propuestaCoordenadas = LibroPrueba();
    propuestaCoordenadas.Actualizacion = actualizacion;
    propuestaCoordenadas.Manual = manual;
    origenCoordenadas.Delitos[0].Columnas["coord_x"] = "-99.1360000000";
    origenCoordenadas.Delitos[0].Columnas["coord_y"] = "19.4340000000";
    propuestaCoordenadas.Delitos[0].Columnas["coord_x"] = "-99.136000";
    propuestaCoordenadas.Delitos[0].Columnas["coord_y"] = "19.434000";
    Verificar(BanciMensualService.CompararOrigen(origenCoordenadas, propuestaCoordenadas).Count == 0,
        $"Coordenadas equivalentes con seis y diez decimales: actualización={actualizacion}, manual={manual}");
    Verificar(BanciMensualService.CompararOrigen(propuestaCoordenadas, origenCoordenadas).Count == 0,
        "La equivalencia de coordenadas funciona en ambos sentidos");
    foreach(var diferente in new[] { "-99.1360000001", "-99.136000000000000000000000000001", "", "-99,136000", "no numérico" })
    {
        propuestaCoordenadas.Delitos[0].Columnas["coord_x"] = diferente;
        Verificar(BanciMensualService.CompararOrigen(origenCoordenadas, propuestaCoordenadas).Any(e => e.Campo == "coord_x"),
            "No se aceptan coordenadas distintas o inválidas, sin tolerancias ni redondeo");
    }
    propuestaCoordenadas.Delitos[0].Columnas["coord_x"] = "-99.136000";
    propuestaCoordenadas.Delitos[0].Columnas["coord_y"] = "19.4340000001";
    Verificar(BanciMensualService.CompararOrigen(origenCoordenadas, propuestaCoordenadas).Any(e => e.Campo == "coord_y"),
        "La latitud también permanece protegida ante cambios reales");
    propuestaCoordenadas.Delitos[0].Columnas["coord_y"] = "19.434000";
    origenCoordenadas.Delitos[0].Columnas["dto"] = "1.00";
    propuestaCoordenadas.Delitos[0].Columnas["dto"] = "1.0";
    Verificar(BanciMensualService.CompararOrigen(origenCoordenadas, propuestaCoordenadas).Any(e => e.Campo == "dto"),
        "La normalización numérica se limita a las dos coordenadas, no a otros datos protegidos");
}
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
libroExcel.Victimas[0].Columnas["nombre_vicfem"] = "Columna heredada que no se exporta";
var bytesExcel = mensualBanci.Excel(libroExcel);
using(var streamExcel = new MemoryStream(bytesExcel))
{
    var archivoExcel = new Microsoft.AspNetCore.Http.FormFile(streamExcel, 0, streamExcel.Length, "archivo", "BANCI.xlsx");
    var leido = await mensualBanci.LeerExcelAsync(archivoExcel, 9, 9, 2026, false);
    Verificar(leido.Carpetas[0].Columnas["id_ci"] == "00000000000000000001", "Excel conserva IDs largos y ceros iniciales como texto");
    Verificar(leido.Victimas[0].Columnas["curp"] == "TEXTO18CARACTERES", "Excel conserva complementos en la tercera pestaña");
    Verificar(!leido.Victimas[0].Columnas.Keys.Any(c => c.Contains("vicfem")), "La plantilla BANCI excluye todas las columnas heredadas de feminicidio");
    Verificar(leido.Carpetas.Count == 1 && leido.Delitos.Count == 1 && leido.Victimas.Count == 1, "Excel de tres pestañas conserva las cantidades");
}

var origenActualizacion = LibroPrueba();
var propuestaActualizacion = LibroPrueba();
origenActualizacion.Victimas[0].Columnas["sexo"] = "10";
propuestaActualizacion.Victimas[0].Columnas["sexo"] = "11";
Verificar(BanciMensualService.CompararOrigen(origenActualizacion, propuestaActualizacion).Count > 0, "Los datos personales del origen están protegidos en carga inicial");
propuestaActualizacion.Actualizacion = true;
Verificar(BanciMensualService.CompararOrigen(origenActualizacion, propuestaActualizacion).Count == 0, "Una actualización permite modificar datos personales de BANCI");
// Manual es un subconjunto autorizado; masiva exige la totalidad de las carpetas pendientes.
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
Verificar(!BanciMensualService.SeSuperponen(envioUno, envioDos), "Una masiva de pendientes permite completar otras carpetas ya capturadas manualmente");
Verificar(BanciMensualService.SeSuperponen(envioDos, envioDos), "La masiva sigue bloqueando envíos de los mismos registros");
var pendientesMasiva = new BanciLibro();
for(var i = 1; i <= 5; i++)
{
    var fila = new ArchivoFila { Columnas = new() { ["id_ci"] = $"CI-{i}" } };
    pendientesMasiva.Carpetas.Add(fila);
    pendientesMasiva.Delitos.Add(fila);
    pendientesMasiva.Victimas.Add(fila);
}
BanciMensualService.FiltrarNuevas(pendientesMasiva, new HashSet<string> { "CI-1", "CI-2" });
Verificar(pendientesMasiva.Carpetas.Count == 3 && pendientesMasiva.Delitos.Count == 3 && pendientesMasiva.Victimas.Count == 3,
    "Después de dos capturas manuales la masiva conserva las tres carpetas pendientes con sus hijos");
BanciMensualService.FiltrarNuevas(pendientesMasiva, new HashSet<string> { "CI-3", "CI-4", "CI-5" });
Verificar(pendientesMasiva.Carpetas.Count == 0 && pendientesMasiva.Delitos.Count == 0 && pendientesMasiva.Victimas.Count == 0,
    "Al completar el periodo ya no quedan registros para una nueva carga");
var acuseLibro = LibroPrueba();
acuseLibro.Carpetas[0].Columnas["no_banci"] = "BANCI/09/2026/000001";
var operacionAcuse = new BanciOperacion { Referencia = Guid.NewGuid(), EntidadNombre = "Ciudad de México", Mes = 9, Anio = 2026, Estado = "CONFIRMADO", Tipo = "CARGA", NombreUsuario = "Prueba" };
var catalogosAcuse = new Dictionary<string, Dictionary<string, string>>();
var webRootAcuse = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../SIIID2.Api/wwwroot"));
var pdfAcuse = BanciMensualService.GenerarAcuse(operacionAcuse, acuseLibro, new DateTime(2026, 10, 15, 22, 0, 0), catalogosAcuse, webRootAcuse);
Verificar(System.Text.Encoding.ASCII.GetString(pdfAcuse, 0, 5) == "%PDF-", "Se genera el acuse PDF confirmado con datos y folios BANCI");
// La ruta opcional permite revisar texto y paginación durante la verificación local.
var salidaAcuse = Environment.GetEnvironmentVariable("BANCI_ACUSE_PRUEBA");
if(!string.IsNullOrEmpty(salidaAcuse))
{
    File.WriteAllBytes(salidaAcuse, pdfAcuse);
    acuseLibro.Actualizacion = true;
    acuseLibro.Cambios.Add(new BanciCambioVista("CI-1", "D-1", "V-1", "obs", "Antes", "MARCADOR_CAMBIO_NO_VISIBLE"));
    File.WriteAllBytes(Path.ChangeExtension(salidaAcuse, "actualizacion.pdf"),
        BanciMensualService.GenerarAcuse(operacionAcuse, acuseLibro, DateTime.UtcNow, catalogosAcuse, webRootAcuse));
    acuseLibro.Actualizacion = false;
}
operacionAcuse.Estado = "PENDIENTE_APROBACION";
Verificar(BanciMensualService.GenerarAcuse(operacionAcuse, acuseLibro, DateTime.UtcNow, catalogosAcuse, webRootAcuse).Length > 1000, "El acuse pendiente se distingue del confirmado");
var acuseLargo = LibroPrueba();
acuseLargo.Carpetas[0].Columnas["no_banci"] = "BANCI/09/2026/000001";
acuseLargo.Victimas = Enumerable.Range(1, 80).Select(i => new ArchivoFila
{
    Columnas = new(acuseLibro.Victimas[0].Columnas) { ["id_vicf"] = i.ToString(), ["nomb"] = "Persona de prueba con nombre extenso para verificar paginación del acuse" }
}).ToList();
var pdfLargo = BanciMensualService.GenerarAcuse(operacionAcuse, acuseLargo, DateTime.UtcNow, catalogosAcuse, webRootAcuse);
Verificar(pdfLargo.Length > 1000, "El acuse institucional pagina el detalle de víctimas conservando encabezado y pie");
if(!string.IsNullOrEmpty(salidaAcuse)) File.WriteAllBytes(Path.ChangeExtension(salidaAcuse, "largo.pdf"), pdfLargo);
var directorioPruebasBanci = Environment.GetEnvironmentVariable("BANCI_ARCHIVOS_PRUEBA");
if(!string.IsNullOrWhiteSpace(directorioPruebasBanci))
{
    foreach(var actualizar in new[] { false, true })
    {
        var nombre = actualizar ? "BANCI_202609_ACTUALIZACION_PRUEBA.xlsx" : "BANCI_202609_CARGA_PRUEBA.xlsx";
        await using var archivo = File.OpenRead(Path.Combine(directorioPruebasBanci, nombre));
        var formulario = new Microsoft.AspNetCore.Http.FormFile(archivo, 0, archivo.Length, "archivo", nombre);
        var leido = await mensualBanci.LeerExcelAsync(formulario, 9, 9, 2026, actualizar);
        Verificar(leido.Carpetas.Count == 5 && leido.Delitos.Count == 5 && leido.Victimas.Count == 5, "El lector real de la API abre las tres pestañas del Excel de " + nombre);
        Verificar(leido.Carpetas.All(f => f.Columnas.ContainsKey("no_banci") == actualizar), "La carga omite folios y la actualización conserva los originales");
        Verificar(leido.Victimas.All(f => !f.Columnas.Keys.Any(c => c.Contains("vicfem"))), "Los Excel entregados no contienen columnas de feminicidio");
    }
}
// El archivo administrativo exporta el detalle completo y conserva los valores como texto.
var diferenciasExcel = new ActualizacionDiferenciasResponse
{
    EsValido = true,
    Victimas = Enumerable.Range(1, 151).Select(i => new ActualizacionDiferenciaRegistro
    {
        TipoMovimiento = "MODIFICADO", CampoIdentificador = "id_ci + id_delito + id_vicf", IdentificadorFiscalia = $"000{i}",
        CamposModificados = [new() { Campo = "nomb", ValorAnterior = "Anterior", ValorNuevo = "=2+2" }, new() { Campo = "sexo", ValorAnterior = "1", ValorNuevo = "2" }]
    }).ToList(),
    Carpetas = [new() { TipoMovimiento = "NUEVO", IdentificadorFiscalia = "001", CamposModificados = [new() { Campo = "rmen_de_hchos", ValorNuevo = new string('a', 60_001) }] }],
    Delitos = [new() { TipoMovimiento = "ELIMINADO", IdentificadorFiscalia = "002", CamposModificados = [new() { Campo = "dto", ValorAnterior = "Anterior", ValorNuevo = null }] }]
};
string archivoTemporal;
using(var exportado = DiferenciasExcel.Crear(diferenciasExcel))
{
    archivoTemporal = ((FileStream)exportado).Name;
    using(var documentoValidar = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(exportado, false))
    {
        var erroresFormato = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(documentoValidar).Take(5).ToList();
        Verificar(erroresFormato.Count == 0, "El Excel generado cumple el formato Open XML: " + string.Join("; ", erroresFormato.Select(e => e.Description + " " + e.Path?.XPath)));
    }
    exportado.Position = 0;
    using var excel = new ClosedXML.Excel.XLWorkbook(exportado);
    Verificar(excel.Worksheets.Count == 3, "El Excel incluye Carpetas, Delitos y Víctimas");
    var victimasExcel = excel.Worksheet("Victimas");
    Verificar(victimasExcel.LastRowUsed()!.RowNumber() == 303, "Exporta los 151 registros y todos sus campos, más allá de la muestra de 100");
    Verificar(victimasExcel.Cell(2, 6).GetString() == "=2+2" && !victimasExcel.Cell(2, 6).HasFormula, "Los valores se exportan como texto, nunca como fórmulas");
    Verificar(victimasExcel.Cell(2, 3).GetString() == "0001", "Se conservan los ceros iniciales de los identificadores");
    Verificar(excel.Worksheet("Delitos").Cell(2, 1).GetString() == "ELIMINADO", "Se incluyen también registros eliminados");
    var carpetasExcel = excel.Worksheet("Carpetas");
    Verificar(string.Concat(Enumerable.Range(2, 3).Select(f => carpetasExcel.Cell(f, 6).GetString())) == new string('a', 60_001), "Los textos que exceden el límite de celda se conservan completos en partes");
    Verificar(victimasExcel.AutoFilter.IsEnabled && victimasExcel.SheetView.SplitRow == 1, "El Excel permite filtrar y conserva visibles los encabezados");
}
Verificar(!File.Exists(archivoTemporal), "El archivo temporal se elimina al terminar la descarga");
diferenciasExcel.DetalleLimitado = true;
var rechazoParcial = false;
try { using var parcialExcel = DiferenciasExcel.Crear(diferenciasExcel); }
catch(InvalidOperationException) { rechazoParcial = true; }
Verificar(rechazoParcial, "Nunca entrega una muestra parcial como si fuera el Excel completo");
var controladorExportacion = typeof(SIIID2.Api.Controllers.DiferenciasAdministracionController);
Verificar(controladorExportacion.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Any(a => a.Roles == "SUPER_USUARIO"), "La descarga completa requiere aprobación administrativa de superusuario");
foreach(var (accion, politica) in new[] { ("Mensual", "MODULO_MENSUAL"), ("Federal", "MODULO_FEDERAL"), ("Semanal", "MODULO_SEMANAL"), ("Banci", "MODULO_BANCI") })
    Verificar(controladorExportacion.GetMethod(accion)!.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Any(a => a.Policy == politica), "La descarga exige también acceso al módulo " + accion);
// Plantillas vacías generadas y colores propios para cada módulo.
foreach(var (modulo, color) in new[] { ("mensual", "691C32"), ("federal", "1F4E79"), ("semanal", "235B4E") })
foreach(var (tipo, columnas) in new[] { ("carpetas", 5), ("delitos", 22), ("victimas", 16) })
{
    var plantilla = PlantillasExcel.Crear(modulo, tipo, new CarpetasValidator(), new DelitosValidator(), new VictimasValidator(reglas));
    using var contenido = new MemoryStream(plantilla);
    using var excelPlantilla = new ClosedXML.Excel.XLWorkbook(contenido);
    var hoja = excelPlantilla.Worksheet(1);
    Verificar(hoja.LastRowUsed()!.RowNumber() == 1 && hoja.LastColumnUsed()!.ColumnNumber() == columnas, "Plantilla " + modulo + "/" + tipo + " vacía, solo encabezados oficiales");
    Verificar(hoja.Cell(1, 1).Style.Fill.BackgroundColor.Color.ToArgb() == System.Drawing.Color.FromArgb(Convert.ToInt32("FF" + color, 16)).ToArgb(), "La plantilla usa el color del módulo " + modulo);
    Verificar(hoja.SheetView.SplitRow == 1 && hoja.Cell(2, 1).Style.NumberFormat.Format == "@", "Plantilla con encabezado inmovilizado y campos de texto sin perder ceros iniciales");
}
diferenciasExcel.DetalleLimitado = false;
foreach(var (modulo, color) in new[] { ("mensual", "691C32"), ("federal", "1F4E79"), ("semanal", "235B4E"), ("banci", "6F2C91") })
{
    using var archivoColor = DiferenciasExcel.Crear(diferenciasExcel, modulo);
    using var excelColor = new ClosedXML.Excel.XLWorkbook(archivoColor);
    var hoja = excelColor.Worksheet("Victimas");
    Verificar(hoja.LastColumnUsed()!.ColumnNumber() == 6 && !hoja.Row(1).CellsUsed().Any(c => c.GetString() == "Parte"), "El Excel de " + modulo + " elimina la columna Parte");
    Verificar(hoja.Cell(1, 1).Style.Fill.BackgroundColor.Color.ToArgb() == System.Drawing.Color.FromArgb(Convert.ToInt32("FF" + color, 16)).ToArgb(), "Las diferencias tienen el color del módulo " + modulo);
    Verificar(hoja.Cell(1, 1).Style.Font.Bold && hoja.Cell(1, 1).Style.Font.FontColor.Color.ToArgb() == System.Drawing.Color.White.ToArgb(), "Encabezado de diferencias legible, en blanco y negrita");
}
var salidaDiferencias = Environment.GetEnvironmentVariable("DIFERENCIAS_EXCEL_VOLUMEN");
if(!string.IsNullOrEmpty(salidaDiferencias))
{
    var volumen = new ActualizacionDiferenciasResponse { EsValido = true };
    volumen.Victimas = Enumerable.Range(1, 60_000).Select(i => new ActualizacionDiferenciaRegistro
    {
        TipoMovimiento = "MODIFICADO", IdentificadorFiscalia = i.ToString(), CampoIdentificador = "id_vicf",
        CamposModificados = Enumerable.Range(1, 20).Select(c => new ActualizacionCampoDiferencia { Campo = "campo_" + c, ValorAnterior = "Anterior", ValorNuevo = "Nuevo" }).ToList()
    }).ToList();
    using var archivo = DiferenciasExcel.Crear(volumen);
    using(var salida = File.Create(salidaDiferencias)) archivo.CopyTo(salida);
    archivo.Position = 0;
    using var documentoExcel = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(archivo, false);
    var hojasVictimas = documentoExcel.WorkbookPart!.Workbook.GetFirstChild<DocumentFormat.OpenXml.Spreadsheet.Sheets>()!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Where(h => h.Name!.Value!.StartsWith("Victimas")).ToList();
    var filasVolumen = 0;
    foreach(var hoja in hojasVictimas)
    {
        using var lector = DocumentFormat.OpenXml.OpenXmlReader.Create(documentoExcel.WorkbookPart.GetPartById(hoja.Id!));
        var filasHoja = 0;
        while(lector.Read()) if(lector.IsStartElement && lector.ElementType == typeof(DocumentFormat.OpenXml.Spreadsheet.Row)) filasHoja++;
        filasVolumen += filasHoja - 1;
    }
    Verificar(filasVolumen == 1_200_000 && hojasVictimas.Count == 2, "La exportación de 60 mil registros conserva 1.2 millones de cambios y continúa en otra hoja al alcanzar el límite de Excel");
}
var corteReporte = new DateTime(2026, 9, 1);
var cargasReporte = BanciMensualService.PrepararReporteCargas([
    new() { Entidad = 9, Estado = "SIN_CARGA", Ranking = 99 },
    new() { Entidad = 2, Intentos = 3, FechaCargaExitosa = corteReporte.AddHours(1), FechaCarga = corteReporte.AddHours(2) },
    new() { Entidad = 1, Intentos = 1, FechaCargaExitosa = corteReporte, FechaAprobacion = corteReporte.AddHours(3) }
]).ToList();
Verificar(cargasReporte.Select(f => f.Entidad).SequenceEqual([1, 2, 9]), "Ranking BANCI por primera carga exitosa y entidades sin carga al final");
Verificar(cargasReporte.Select(f => f.Ranking).SequenceEqual(new int?[] { 1, 2, null }), "Entidades sin carga exitosa no reciben ranking");
Verificar(cargasReporte[1].Intentos == 3 && cargasReporte[1].FechaCarga?.Kind == DateTimeKind.Utc && cargasReporte[0].FechaAprobacion?.Kind == DateTimeKind.Utc, "Reporte BANCI conserva intentos y entrega fechas UTC");
var rutaOriginalBanci = BanciMensualService.RutaRelativaOriginal(Guid.Empty, @"C:\fakepath\Mi archivo BANCI.xlsx");
Verificar(Path.GetFileName(rutaOriginalBanci) == "Mi archivo BANCI.xlsx" && Path.GetDirectoryName(rutaOriginalBanci) == Guid.Empty.ToString("N"), "Original BANCI conserva nombre recibido y separa cada envío en una carpeta");
var libroRevision = new BanciLibro
{
    Manual = true, Anio = 2025, Mes = 9,
    Victimas = Enumerable.Range(1, 150).Select(i => new ArchivoFila { NumeroFila = i }).ToList(),
    Cambios = Enumerable.Range(1, 250).Select(i => new BanciCambioVista("ci", "del", "vic", "campo" + i, "antes", "despues")).ToList()
};
var revisionBanci = BanciMensualService.PrepararRevisionAdministracion(libroRevision);
Verificar(revisionBanci.TotalVictimas == 150 && revisionBanci.TotalCambios == 250 && revisionBanci.VictimasConCambios == 1, "Revisión administrativa BANCI conserva los totales completos y cuenta víctimas distintas");
Verificar(revisionBanci.Libro.Victimas.Count == 100 && revisionBanci.Libro.Cambios.Count == 100 && revisionBanci.LimiteMuestra == 100, "Revisión BANCI limita registros y cambios enviados al navegador");
Verificar(libroRevision.Cambios.Count == 250 && libroRevision.Victimas.Count == 150 && revisionBanci.Libro.Manual && revisionBanci.Libro.Anio == 2025, "La muestra de revisión no altera datos originales ni modalidad o corte");
Verificar(!BanciMensualService.PrepararRecordatorio(0, 2026, 9).HayPendiente, "BANCI no avisa de carga pendiente cuando no quedan carpetas por remitir");
var avisoBanci = BanciMensualService.PrepararRecordatorio(2, 2026, 9);
Verificar(avisoBanci.HayPendiente && avisoBanci.Periodo == "septiembre de 2026" && avisoBanci.Mensaje.Contains("2 carpeta"), "BANCI recuerda el corte y las carpetas aún pendientes de carga");
ArchivoFila VictimaPendienteBanci(string victima) => new() { Columnas = new() { ["id_ci"] = "ci", ["id_delito"] = "del", ["id_vicf"] = victima } };
var disponiblesBanci = new BanciLibro { Actualizacion = true, Victimas = [VictimaPendienteBanci("uno"), VictimaPendienteBanci("dos")] };
BanciMensualService.ExcluirVictimasEnRevision(disponiblesBanci, [new() { Actualizacion = true, Victimas = [VictimaPendienteBanci("uno")] }]);
Verificar(disponiblesBanci.Victimas.Count == 1 && disponiblesBanci.Victimas[0].Columnas["id_vicf"] == "dos", "La precarga de actualización BANCI conserva únicamente víctimas sin envío pendiente");
BanciMensualService.ExcluirVictimasEnRevision(disponiblesBanci, [new() { Actualizacion = true, Victimas = [VictimaPendienteBanci("dos")] }]);
Verificar(disponiblesBanci.Victimas.Count == 0, "Si todos los registros están en revisión no se expone captura ni plantilla de actualización");
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
