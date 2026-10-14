using System.Reflection;
using SIIID2.Api.Services;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;

// Ejecutar sin conexión a BD: los catálogos simulados prueban que la clave se obtiene de sus datos.
var campos = new[]
{
    "sexo",
    "genero",
    "pob",
    "disc",
    "nacional",
    "forma_acc",
    "emto_com_dto",
    "grdo_cons"
};
var catalogos = campos.Select(c => new BanciFormularioOpcion
{
    CodigoRegla = "NO_IDENTIFICADO",
    Campo = c,
    Clave = c == "nacional" ? "174" : c == "emto_com_dto" ? "7" : "3",
    Descripcion = c == "nacional" ? "No identificada" : "No identificado"
}).ToList();
var pruebas = 0;
void Verificar(bool condicion, string caso)
{
    if(!condicion)
        throw new Exception(caso);
    pruebas++;
}

BanciLecturaArchivosResultado Lectura() => new()
{
    Delitos = [new ArchivoFila
    {
        Columnas = new()
        {
            ["id_ci"] = "ci",
            ["id_delito"] = "del",
            ["forma_acc"] = "1",
            ["emto_com_dto"] = "2",
            ["grdo_cons"] = "1",
            ["clasf_de_dto"] = "2.07.04",
            ["id_ent_hchos"] = "9",
            ["id_mun_hchos"] = "015"
        }
    }

    ],
    Victimas = [new ArchivoFila
    {
        Columnas = new()
        {
            ["id_ci"] = "ci",
            ["id_delito"] = "del",
            ["id_tv"] = "1",
            ["sexo"] = "1",
            ["genero"] = "1",
            ["pob"] = "2",
            ["disc"] = "2",
            ["nacional"] = "73"
        }
    }

    ]
};
ArchivoFila Fila(BanciLecturaArchivosResultado l, string campo) => campos.Take(5).Contains(campo) ? l.Victimas[0] : l.Delitos[0];
foreach (var campo in campos)
{
    foreach (var entrada in new string? []
    {
        null,
        "",
        " ",
        "0",
        "000",
        "+0"
    }

    )
    {
        var lectura = Lectura();
        Fila(lectura, campo).Columnas[campo] = entrada;
        BanciCatalogosNormalizer.Preparar(lectura, catalogos);
        Verificar(lectura.Errores.Count == 0 && Fila(lectura, campo).Columnas[campo] == catalogos.Single(c => c.Campo == campo).Clave, $"Normalizar {campo}: '{entrada}'");
    }

    var omitida = Lectura();
    Fila(omitida, campo).Columnas.Remove(campo);
    BanciCatalogosNormalizer.Preparar(omitida, catalogos);
    Verificar(Fila(omitida, campo).Columnas[campo] == catalogos.Single(c => c.Campo == campo).Clave, "Campo omitido: " + campo);
    var invalida = Lectura();
    Fila(invalida, campo).Columnas[campo] = "99";
    BanciCatalogosNormalizer.Preparar(invalida, catalogos);
    var errores = await new CatalogosValidator(new CatalogosSimulados(), new ReglasSimuladas()).ValidarBanciAsync([], invalida.Delitos, invalida.Victimas);
    Verificar(errores.Any(e => e.Campo == campo), "No sustituir una clave inválida: " + campo);
}

var alternativa = Lectura();
alternativa.Victimas[0].Columnas["genero"] = "0";
var cambiados = catalogos.Select(c => new BanciFormularioOpcion
{
    Campo = c.Campo,
    Clave = c.Campo == "genero" ? "19" : c.Clave,
    Descripcion = "Etiqueta modificada",
    CodigoRegla = c.CodigoRegla
}).ToList();
BanciCatalogosNormalizer.Preparar(alternativa, cambiados);
Verificar(alternativa.Victimas[0].Columnas["genero"] == "19", "Leer la clave del catálogo, no fijar 3");
var sinOpcion = Lectura();
sinOpcion.Victimas[0].Columnas["genero"] = "0";
BanciCatalogosNormalizer.Preparar(sinOpcion, catalogos.Where(c => c.Campo != "genero").ToList());
Verificar(sinOpcion.Errores.Any(e => e.Campo == "genero"), "No inventar clave si falta la opción activa");
var cruce = Lectura();
cruce.Victimas[0].Columnas["genero"] = "0";
var original = new BanciCruceArchivos([new ArchivoFila { Columnas = new()
{
    ["id_ci"] = "ci",
    ["ntra_ci"] = "prueba"
} }], cruce.Delitos, cruce.Victimas);
var normal = BanciCatalogosNormalizer.NormalizarCruce(original, catalogos);
Verificar(original.Victimas[0].Columnas["genero"] == "0" && normal.Victimas[0].Columnas["genero"] == "3", "Conservar snapshot original");
Verificar(BanciCruceValidator.Comparar(original, normal, new HashSet<string> { "2.07.04" }, catalogos).Count == 0, "Cruce reconoce cero y No identificado con el catálogo vigente");
// Una descripción igual sin significado configurado no habilita un valor por defecto.
var sinSignificado = Lectura();
sinSignificado.Victimas[0].Columnas["genero"] = "0";
BanciCatalogosNormalizer.Preparar(sinSignificado, catalogos.Select(c => new BanciFormularioOpcion
{
    Campo = c.Campo,
    Clave = c.Clave,
    Descripcion = c.Descripcion,
    CodigoRegla = c.Campo == "genero" ? null : c.CodigoRegla
}).ToList());
Verificar(sinSignificado.Errores.Any(e => e.Campo == "genero"), "No resolver significados por descripción");
var motivos = new List<BanciFormularioOpcion>
{
    new()
    {
        Campo = "constitutiva_delito",
        Clave = "17",
        CodigoRegla = "DELITO",
        Descripcion = "Etiqueta A"
    },
    new()
    {
        Campo = "constitutiva_delito",
        Clave = "29",
        CodigoRegla = "NO_DELITO",
        Descripcion = "Etiqueta B"
    },
    new()
    {
        Campo = "motivo_delito",
        Clave = "delito-real"
    },
    new()
    {
        Campo = "motivo_no_delito",
        Clave = "otro-real"
    }
};
foreach (var(tipo, motivo)in new[]
{
    ("17", "delito-real"),
    ("29", "otro-real")
}

)
    Verificar(!BanciMotivoValidator.Validar(new()
    {
        ["constitutiva_delito"] = tipo,
        ["motivo_desaparicion"] = motivo
    }, 1, motivos).Any(), "Motivo con clave vigente: " + tipo);
Verificar(BanciMotivoValidator.Validar(new()
{
    ["constitutiva_delito"] = "1",
    ["motivo_desaparicion"] = "delito-real"
}, 1, motivos).Any(), "Rechazar clave anterior de constitutiva");
Verificar(BanciMotivoValidator.Validar(new()
{
    ["constitutiva_delito"] = "17",
    ["motivo_desaparicion"] = "otro-real"
}, 1, motivos).Any(), "Rechazar motivo del otro catálogo");
var metodologia = Lectura();
var opcionesIniciales = catalogos.Concat(new[] { new BanciFormularioOpcion
{
    Campo = "localizado_o_no_localizado",
    Clave = "55",
    CodigoRegla = "NO_LOCALIZADO",
    Descripcion = "Etiqueta nueva"
} }).ToList();
new BanciMetodologiaValidator().Validar(metodologia, opcionesIniciales);
Verificar(metodologia.Victimas[0].Columnas["localizado_o_no_localizado"] == "55", "Estado inicial toma la clave del catálogo");
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
Console.WriteLine($"{pruebas} verificaciones aprobadas.");
sealed class CatalogosSimulados : ICatalogoRepository
{
    public Task<HashSet<int>> ObtenerClavesNumericasActivasAsync(string tabla,
        string columnaClave) => Task.FromResult(tabla == "catalogo_entidad_federativa" ? new HashSet<int> { 9 } : new HashSet<int>
    {
        1,
        2,
        3,
        4,
        7,
        8
    });
    public Task<HashSet<string>> ObtenerClavesTextoActivasAsync(string tabla,
        string columnaClave) => Task.FromResult(tabla == "banci_catalogo_clasificacion_delito" ? new HashSet<string> { "2.07.04" } : new HashSet<string>
    {
        "73",
        "174",
        "09"
    });
    public Task<HashSet<string>> ObtenerMunicipiosPorEntidadActivosAsync() => Task.FromResult(new HashSet<string> { "9|015" });
    public Task<bool> CoordenadasCorrespondenMunicipioAsync(int idEntidadFederativa,
        int idMunicipio,
        decimal coordX,
        decimal coordY) => Task.FromResult(true);
    public Task<List<EntidadFederativaCatalogoItem>> ObtenerEntidadesFederativasActivasAsync() => Task.FromResult(new List<EntidadFederativaCatalogoItem>());
    public Task<List<RolCatalogoItem>> ObtenerRolesActivosAsync() => Task.FromResult(new List<RolCatalogoItem>());
}

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
