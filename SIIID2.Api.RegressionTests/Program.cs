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
foreach (var(modulo, clasificacion)in new[]
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
var semanal = new SemanalCargaService(null !, null !, null !, null !, null !, reglas, null !, null !, null !, null !, null !, null !, null !);
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
var assembly=typeof(SIIID2.Api.Controllers.BanciModeloController).Assembly;
foreach(var nombre in new[] { "BanciCruceService", "BanciCruceController", "BanciCargasController", "BanciActualizacionesController", "BanciAltaNormalizer", "BanciArchivoReader", "BanciActualizacionRepository" })
    Verificar(!assembly.GetTypes().Any(t=>t.Name==nombre),"Contrato retirado: "+nombre);
Verificar(assembly.GetTypes().Any(t=>t.Name=="BanciUsuariosController"),"Se conserva administración de usuarios BANCI");
Verificar(!CurpFormatoValidator.EsFormatoValido("GODE020230HDFRRN00"),"Rechazar fecha imposible de CURP sin RENAPO");
Verificar(!CurpFormatoValidator.EsFormatoValido("dato invalido"),"Conservar validación local de CURP");
var curpBase="GODE561231HDFRRN0";
const string alfabeto="0123456789ABCDEFGHIJKLMNÑOPQRSTUVWXYZ";
var suma=Enumerable.Range(0,17).Sum(i=>alfabeto.IndexOf(curpBase[i])*(18-i));
var curpValida=curpBase+((10-suma%10)%10);
Verificar(CurpFormatoValidator.EsFormatoValido(curpValida),"Aceptar CURP válida sin consulta externa");
Verificar(!CurpFormatoValidator.EsFormatoValido(curpBase+((curpValida[17]-'0'+1)%10)),"Rechazar dígito verificador incorrecto");

foreach(var modulo in new[] { "MENSUAL", "FEDERAL" })
{
    var configuracion=new SistemaConfiguracionService(null!);
    typeof(SistemaConfiguracionService).GetProperty("Version")!.SetValue(configuracion,1L);
    configuracion.Modulos.Add(new SistemaModulo { Clave=modulo, Activo=true });
    var feminicidio=new SistemaOpcion { Modulo=modulo, Clave="FEMINICIDIO_DATOS_ADICIONALES", Habilitado=false, Disponible=true };
    var renapo=new SistemaOpcion { Modulo=modulo, Clave="RENAPO", Habilitado=true, Disponible=true };
    configuracion.Opciones.AddRange([feminicidio,renapo]);
    Verificar(!configuracion.Activa(modulo,"RENAPO"),"Apagar feminicidio apaga RENAPO: "+modulo);
    feminicidio.Habilitado=true;
    renapo.Habilitado=false;
    Verificar(configuracion.Activa(modulo,"FEMINICIDIO_DATOS_ADICIONALES")&&!configuracion.Activa(modulo,"RENAPO"),"Apagar RENAPO conserva reglas locales: "+modulo);
}
Console.WriteLine($"{pruebas} verificaciones aprobadas.");
sealed class ReglasSimuladas : ICatalogoReglasRepository
{
    public Task CargarAsync() => Task.CompletedTask;
    public string Clave(string catalogo, string codigo) => (catalogo, codigo) switch    {
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
        _ => throw new NotSupportedException()};
    public int ClaveNumerica(string catalogo, string codigo) => int.Parse(Clave(catalogo, codigo));
    public bool Contiene(string catalogo, string codigo, string? clave) => (catalogo, codigo, clave) switch    {
        ("catalogo_modalidad_delito", "ROBO_VEHICULO", "robo-test") => true,
        ("catalogo_modalidad_delito", "EXTORSION", "ext-test") => true,
        ("catalogo_subtipo_delito", "LESIONES_DOLOSAS", "les-test") => true,
        ("catalogo_subtipo_delito", "HOMICIDIO_DOLOSO", "hom-doloso") => true,
        _ => false
    };
}
