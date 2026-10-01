using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;

public sealed class BanciCruceException(List<CargaValidacionError> errores) : Exception("El cruce con BANCI impide integrar la carga. Corrija las diferencias y vuelva a validar.")
{
    public List<CargaValidacionError> Errores { get; } = errores;
}
public sealed class BanciCruceService(IDbConnectionFactory factory, SistemaConfiguracionService config)
{
    public Task<List<CargaValidacionError>> ValidarAsync(int entidad, int mes, int anio, BanciCruceArchivos archivos) => ValidarInternoAsync(entidad, null, mes, anio, archivos);
    public Task<List<CargaValidacionError>> ValidarFederalAsync(int usuario, int mes, int anio, BanciCruceArchivos archivos) => ValidarInternoAsync(null, usuario, mes, anio, archivos);
    private async Task<List<CargaValidacionError>> ValidarInternoAsync(int? entidad, int? usuarioFederal, int mes, int anio, BanciCruceArchivos archivos)
    {
        await config.CargarAsync();
        if (!config.Activa(usuarioFederal.HasValue ? "FEDERAL" : "MENSUAL", "CRUCE_BANCI")) return [];
        using var db = factory.CrearConexion(); db.Open();
        using var tx = db.BeginTransaction();
        var (errores, _) = await CompararAsync(db, tx, entidad, usuarioFederal, mes, anio, archivos);
        tx.Commit(); return errores;
    }
    public static Task GuardarAsync(IDbConnection db, IDbTransaction tx, long idCarga, List<ArchivoFila> carpetas, List<ArchivoFila> delitos, List<ArchivoFila> victimas) => GuardarInternoAsync(db, tx, idCarga, new(carpetas, delitos, victimas), false);
    public static Task GuardarFederalAsync(IDbConnection db, IDbTransaction tx, long idCarga, List<ArchivoFila> carpetas, List<ArchivoFila> delitos, List<ArchivoFila> victimas) => GuardarInternoAsync(db, tx, idCarga, new(carpetas, delitos, victimas), true);
    private static Task GuardarInternoAsync(IDbConnection db, IDbTransaction tx, long idCarga, BanciCruceArchivos archivos, bool federal)
    {
        var tabla = federal ? "banci_cruce_federal" : "banci_cruce_mensual";
        var columna = federal ? "id_federal_carga" : "id_carga";
        return db.ExecuteAsync($"INSERT dbo.{tabla} ({columna},archivos_json) VALUES (@idCarga,@json);", new { idCarga, json = JsonSerializer.Serialize(archivos) }, tx);
    }
    // Reutilizado por la revisión previa y por la integración final: nunca integra la carga.
    public async Task ConfirmarAsync(IDbConnection db, IDbTransaction tx, long idCarga, bool federal = false)
    {
        await config.CargarAsync();
        if (!config.Activa(federal ? "FEDERAL" : "MENSUAL", "CRUCE_BANCI")) return;
        var tabla = federal ? "federal_carga" : "carga";
        var columna = federal ? "id_federal_carga" : "id_carga";
        var cruce = federal ? "banci_cruce_federal" : "banci_cruce_mensual";
        var carga = await db.QuerySingleAsync($"SELECT id_entidad_federativa entidad, id_usuario_carga usuario, mes_corte mes, anio_corte anio FROM dbo.{tabla} WHERE {columna}=@idCarga;", new { idCarga }, tx);
        var json = await db.QuerySingleOrDefaultAsync<string>($"SELECT archivos_json FROM dbo.{cruce} WHERE {columna}=@idCarga;", new { idCarga }, tx);
        if (json == null) throw new BanciCruceException([new() { Archivo = "general", Codigo = "BANCI_REVALIDAR", DescripcionResumen = "Cruce BANCI", Mensaje = "Esta carga fue validada antes de instalar el cruce. Rechácela y vuelva a validar los archivos completos." }]);
        var archivos = JsonSerializer.Deserialize<BanciCruceArchivos>(json) ?? throw new InvalidOperationException("Snapshot inválido.");
        int? entidad = federal ? null : (int)carga.entidad;
        int? usuario = federal ? (int)carga.usuario : null;
        var (errores, ids) = await CompararAsync(db, tx, entidad, usuario, (int)carga.mes, (int)carga.anio, archivos);
        if (errores.Count > 0) throw new BanciCruceException(errores);
        await db.ExecuteAsync($"UPDATE dbo.{cruce} SET victimas_json=@ids,fecha_cruce_utc=SYSUTCDATETIME() WHERE {columna}=@idCarga;", new { idCarga, ids = JsonSerializer.Serialize(ids) }, tx);
    }
    private static async Task<(List<CargaValidacionError>, long[])> CompararAsync(IDbConnection db, IDbTransaction tx, int? entidad, int? usuarioFederal, int mes, int anio, BanciCruceArchivos archivos)
    {
        var errores = new List<CargaValidacionError>(); var ids = new List<long>();
        // Todas las entidades, en orden fijo, para detectar también las omitidas por completo.
        foreach (var e in entidad.HasValue ? new[] { entidad.Value } : Enumerable.Range(1, 32))
        {
            var (banci, claves) = await LeerAsync(db, tx, e, mes, anio, usuarioFederal);
            var parcial = usuarioFederal.HasValue ? PorEntidad(archivos, claves, e) : archivos;
            var diferencias = BanciCruceValidator.Comparar(parcial, banci, claves);
            foreach (var error in diferencias)
            {
                error.EntidadCruce = e;
                if (usuarioFederal.HasValue) { error.DescripcionResumen = "Cruce Federal–BANCI"; error.Mensaje = error.Mensaje.Replace("Consolidado", "Federal"); }
            }
            errores.AddRange(diferencias);
            ids.AddRange(banci.Victimas.Select(v => long.Parse(v.Columnas["id_banci_victima"]!, CultureInfo.InvariantCulture)));
        }
        return (errores, ids.Distinct().ToArray());
    }
    public static BanciCruceArchivos PorEntidad(BanciCruceArchivos archivos, ISet<string> claves, int entidad)
    {
        var delitos = archivos.Delitos.Where(d => int.TryParse(BanciCruceValidator.Valor(d, "id_ent_hchos"), out var e) && e == entidad && claves.Contains(BanciCruceValidator.Valor(d, "clasf_de_dto"))).ToList();
        var ci = delitos.Select(d => BanciCruceValidator.Clave(BanciCruceValidator.Valor(d, "id_ci"))).ToHashSet();
        return new(archivos.Carpetas.Where(c => ci.Contains(BanciCruceValidator.Clave(BanciCruceValidator.Valor(c, "id_ci")))).ToList(), delitos, archivos.Victimas);
    }
    private static async Task<(BanciCruceArchivos, HashSet<string>)> LeerAsync(IDbConnection db, IDbTransaction tx, int entidad, int mes, int anio, int? usuarioFederal)
    {
        var bloqueo = await db.ExecuteScalarAsync<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@recurso,@LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=10000; SELECT @r;", new { recurso = $"BANCI:ENTIDAD:{entidad}" }, tx);
        if (bloqueo < 0) throw new TimeoutException("BANCI está integrando información de esta entidad. Reintente.");
        var inicio = new DateTime(anio, mes, 1);
        using var datos = await db.QueryMultipleAsync("""
            SELECT c.* INTO #CruceCarpetas FROM dbo.banci_carpeta_investigacion c WHERE c.activo=1 AND c.id_entidad_federativa=@entidad AND c.fha_de_ini>=@inicio AND c.fha_de_ini<@fin AND ((@usuarioFederal IS NULL AND c.id_usuario_reporte_federal IS NULL) OR c.id_usuario_reporte_federal=@usuarioFederal);
            SELECT * FROM #CruceCarpetas;
            SELECT d.*,c.id_ci FROM dbo.banci_delito d JOIN #CruceCarpetas c ON c.id_banci_carpeta_investigacion=d.id_banci_carpeta_investigacion WHERE d.activo=1;
            SELECT v.*,d.id_delito,c.id_ci FROM dbo.banci_victima v JOIN dbo.banci_delito d ON d.id_banci_delito=v.id_banci_delito JOIN #CruceCarpetas c ON c.id_banci_carpeta_investigacion=d.id_banci_carpeta_investigacion WHERE v.activo=1 AND d.activo=1;
            SELECT clave FROM dbo.banci_catalogo_clasificacion_delito;
            DROP TABLE #CruceCarpetas;
            """, new { entidad, usuarioFederal, inicio, fin = inicio.AddMonths(1) }, tx);
        var carpetas = Filas(await datos.ReadAsync()); var delitos = Filas(await datos.ReadAsync()); var victimas = Filas(await datos.ReadAsync());
        return (new(carpetas, delitos, victimas), (await datos.ReadAsync<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
    private static List<ArchivoFila> Filas(IEnumerable<dynamic> filas) => filas.Select(f => new ArchivoFila { Columnas = ((IDictionary<string, object>)f).ToDictionary(p => p.Key, p => p.Value switch { null => null, DateTime fecha => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), _ => Convert.ToString(p.Value, CultureInfo.InvariantCulture) }, StringComparer.OrdinalIgnoreCase) }).ToList();
}
