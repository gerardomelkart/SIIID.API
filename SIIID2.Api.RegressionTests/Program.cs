using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;

// Ejecutar sin conexión a BD: los catálogos simulados prueban que la clave se obtiene de sus datos.
var campos = new[] { "sexo", "genero", "pob", "disc", "nacional", "forma_acc", "emto_com_dto", "grdo_cons" };
var catalogos = campos.Select(c => new BanciFormularioOpcion
{
    Campo = c, Clave = c == "nacional" ? "174" : c == "emto_com_dto" ? "7" : "3",
    Descripcion = c == "nacional" ? "No identificada" : "No identificado"
}).ToList();
var pruebas = 0;
void Verificar(bool condicion, string caso)
{
    if(!condicion) throw new Exception(caso);
    pruebas++;
}
BanciLecturaArchivosResultado Lectura() => new()
{
    Delitos = [new ArchivoFila { Columnas = new()
    {
        ["id_ci"] = "ci", ["id_delito"] = "del", ["forma_acc"] = "1", ["emto_com_dto"] = "2", ["grdo_cons"] = "1",
        ["clasf_de_dto"] = "2.07.04", ["id_ent_hchos"] = "9", ["id_mun_hchos"] = "015"
    } }],
    Victimas = [new ArchivoFila { Columnas = new()
    {
        ["id_ci"] = "ci", ["id_delito"] = "del", ["id_tv"] = "1", ["sexo"] = "1", ["genero"] = "1",
        ["pob"] = "2", ["disc"] = "2", ["nacional"] = "73"
    } }]
};
ArchivoFila Fila(BanciLecturaArchivosResultado l, string campo) =>
    campos.Take(5).Contains(campo) ? l.Victimas[0] : l.Delitos[0];
foreach(var campo in campos)
{
    foreach(var entrada in new string?[] { null, "", " ", "0", "000", "+0" })
    {
        var lectura = Lectura();
        Fila(lectura, campo).Columnas[campo] = entrada;
        BanciCatalogosNormalizer.Preparar(lectura, catalogos);
        Verificar(lectura.Errores.Count == 0 && Fila(lectura, campo).Columnas[campo] == catalogos.Single(c => c.Campo == campo).Clave,
            $"Normalizar {campo}: '{entrada}'");
    }
    var omitida = Lectura();
    Fila(omitida, campo).Columnas.Remove(campo);
    BanciCatalogosNormalizer.Preparar(omitida, catalogos);
    Verificar(Fila(omitida, campo).Columnas[campo] == catalogos.Single(c => c.Campo == campo).Clave, "Campo omitido: " + campo);

    var invalida = Lectura();
    Fila(invalida, campo).Columnas[campo] = "99";
    BanciCatalogosNormalizer.Preparar(invalida, catalogos);
    var errores = await new CatalogosValidator(new CatalogosSimulados()).ValidarBanciAsync([], invalida.Delitos, invalida.Victimas);
    Verificar(errores.Any(e => e.Campo == campo), "No sustituir una clave inválida: " + campo);
}
var alternativa = Lectura();
alternativa.Victimas[0].Columnas["genero"] = "0";
var cambiados = catalogos.Select(c => new BanciFormularioOpcion { Campo = c.Campo, Clave = c.Campo == "genero" ? "19" : c.Clave, Descripcion = c.Descripcion }).ToList();
BanciCatalogosNormalizer.Preparar(alternativa, cambiados);
Verificar(alternativa.Victimas[0].Columnas["genero"] == "19", "Leer la clave del catálogo, no fijar 3");
var sinOpcion = Lectura();
sinOpcion.Victimas[0].Columnas["genero"] = "0";
BanciCatalogosNormalizer.Preparar(sinOpcion, catalogos.Where(c => c.Campo != "genero").ToList());
Verificar(sinOpcion.Errores.Any(e => e.Campo == "genero"), "No inventar clave si falta la opción activa");

var cruce = Lectura();
cruce.Victimas[0].Columnas["genero"] = "0";
var original = new BanciCruceArchivos([new ArchivoFila { Columnas = new() { ["id_ci"] = "ci", ["ntra_ci"] = "prueba" } }], cruce.Delitos, cruce.Victimas);
var normal = BanciCatalogosNormalizer.NormalizarCruce(original, catalogos);
Verificar(original.Victimas[0].Columnas["genero"] == "0" && normal.Victimas[0].Columnas["genero"] == "3", "Conservar snapshot original");
Verificar(BanciCruceValidator.Comparar(original, normal, new HashSet<string> { "2.07.04" }, catalogos).Count == 0,
    "Cruce reconoce cero y No identificado con el catálogo vigente");
Console.WriteLine($"{pruebas} verificaciones aprobadas.");

sealed class CatalogosSimulados : ICatalogoRepository
{
    public Task<HashSet<int>> ObtenerClavesNumericasActivasAsync(string tabla, string columnaClave) =>
        Task.FromResult(tabla == "catalogo_entidad_federativa" ? new HashSet<int> { 9 } : new HashSet<int> { 1, 2, 3, 4, 7, 8 });
    public Task<HashSet<string>> ObtenerClavesTextoActivasAsync(string tabla, string columnaClave) =>
        Task.FromResult(tabla == "banci_catalogo_clasificacion_delito" ? new HashSet<string> { "2.07.04" } : new HashSet<string> { "73", "174", "09" });
    public Task<HashSet<string>> ObtenerMunicipiosPorEntidadActivosAsync() => Task.FromResult(new HashSet<string> { "9|015" });
    public Task<bool> CoordenadasCorrespondenMunicipioAsync(int idEntidadFederativa, int idMunicipio, decimal coordX, decimal coordY) => Task.FromResult(true);
    public Task<List<EntidadFederativaCatalogoItem>> ObtenerEntidadesFederativasActivasAsync() => Task.FromResult(new List<EntidadFederativaCatalogoItem>());
    public Task<List<RolCatalogoItem>> ObtenerRolesActivosAsync() => Task.FromResult(new List<RolCatalogoItem>());
}
