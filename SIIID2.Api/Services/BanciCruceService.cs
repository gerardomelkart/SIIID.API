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
    public async Task<List<CargaValidacionError>> ValidarAsync(int entidad, int mes, int anio, BanciCruceArchivos archivos)
    {
        await config.CargarAsync();
        if (!config.Activa("MENSUAL", "CRUCE_BANCI")) return [];
        using var db = factory.CrearConexion();
        db.Open();
        using var tx = db.BeginTransaction();
        var (banci, claves) = await LeerAsync(db, tx, entidad, mes, anio);
        var errores = BanciCruceValidator.Comparar(archivos, banci, claves);
        tx.Commit();
        return errores;
    }

    // Conserva todos los campos originales: el staging mensual omite algunos nombres y horas.
    // Se guarda en la misma transacción que carga/staging, incluso si el cruce está apagado.
    public static Task GuardarAsync(IDbConnection db, IDbTransaction tx, long idCarga, List<ArchivoFila> carpetas, List<ArchivoFila> delitos, List<ArchivoFila> victimas) => db.ExecuteAsync("INSERT dbo.banci_cruce_mensual (id_carga, archivos_json) VALUES (@idCarga, @json);", new { idCarga, json = JsonSerializer.Serialize(new BanciCruceArchivos(carpetas, delitos, victimas)) }, tx);

    public async Task ConfirmarAsync(IDbConnection db, IDbTransaction tx, long idCarga)
    {
        await config.CargarAsync();
        if (!config.Activa("MENSUAL", "CRUCE_BANCI")) return;
        var carga = await db.QuerySingleAsync("SELECT id_entidad_federativa entidad, mes_corte mes, anio_corte anio FROM dbo.carga WHERE id_carga=@idCarga;", new { idCarga }, tx);
        var json = await db.QuerySingleOrDefaultAsync<string>("SELECT archivos_json FROM dbo.banci_cruce_mensual WHERE id_carga=@idCarga;", new { idCarga }, tx);
        if (json == null) throw new BanciCruceException([new() { Archivo = "general", Codigo = "BANCI_REVALIDAR", DescripcionResumen = "Cruce BANCI", Mensaje = "Esta carga fue validada antes de instalar el cruce. Rechácela y vuelva a validar los archivos completos." }]);
        var archivos = JsonSerializer.Deserialize<BanciCruceArchivos>(json) ?? throw new InvalidOperationException("Snapshot mensual inválido.");
        var (banci, claves) = await LeerAsync(db, tx, (int)carga.entidad, (int)carga.mes, (int)carga.anio);
        var errores = BanciCruceValidator.Comparar(archivos, banci, claves);
        if (errores.Count > 0) throw new BanciCruceException(errores);
        var ids = banci.Victimas.Select(v => long.Parse(v.Columnas["id_banci_victima"]!, CultureInfo.InvariantCulture)).ToArray();
        await db.ExecuteAsync("UPDATE dbo.banci_cruce_mensual SET victimas_json=@ids, fecha_cruce_utc=SYSUTCDATETIME() WHERE id_carga=@idCarga;", new { idCarga, ids = JsonSerializer.Serialize(ids) }, tx);
    }

    private static async Task<(BanciCruceArchivos Archivos, HashSet<string> Claves)> LeerAsync(IDbConnection db, IDbTransaction tx, int entidad, int mes, int anio)
    {
        var bloqueo = await db.ExecuteScalarAsync<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@recurso,@LockMode=N'Shared',@LockOwner=N'Transaction',@LockTimeout=10000; SELECT @r;", new { recurso = $"BANCI:ENTIDAD:{entidad}" }, tx);
        if (bloqueo < 0) throw new TimeoutException("BANCI está integrando información de esta entidad. Reintente.");
        var inicio = new DateTime(anio, mes, 1);
        using var datos = await db.QueryMultipleAsync("""
            SELECT c.* FROM dbo.banci_carpeta_investigacion c WHERE c.activo=1 AND c.id_entidad_federativa=@entidad AND c.fha_de_ini>=@inicio AND c.fha_de_ini<@fin;
            SELECT d.*, c.id_ci FROM dbo.banci_delito d JOIN dbo.banci_carpeta_investigacion c ON c.id_banci_carpeta_investigacion=d.id_banci_carpeta_investigacion WHERE d.activo=1 AND c.activo=1 AND c.id_entidad_federativa=@entidad AND c.fha_de_ini>=@inicio AND c.fha_de_ini<@fin;
            SELECT v.*, d.id_delito, c.id_ci FROM dbo.banci_victima v JOIN dbo.banci_delito d ON d.id_banci_delito=v.id_banci_delito JOIN dbo.banci_carpeta_investigacion c ON c.id_banci_carpeta_investigacion=d.id_banci_carpeta_investigacion WHERE v.activo=1 AND d.activo=1 AND c.activo=1 AND c.id_entidad_federativa=@entidad AND c.fha_de_ini>=@inicio AND c.fha_de_ini<@fin;
            SELECT clave FROM dbo.banci_catalogo_clasificacion_delito;
            """, new { entidad, inicio, fin = inicio.AddMonths(1) }, tx);
        var carpetas = Filas(await datos.ReadAsync());
        var delitos = Filas(await datos.ReadAsync());
        var victimas = Filas(await datos.ReadAsync());
        return (new(carpetas, delitos, victimas), (await datos.ReadAsync<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
    private static List<ArchivoFila> Filas(IEnumerable<dynamic> filas) => filas.Select(f => new ArchivoFila { Columnas = ((IDictionary<string, object>)f).ToDictionary(p => p.Key, p => p.Value switch { null => null, DateTime fecha => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), _ => Convert.ToString(p.Value, CultureInfo.InvariantCulture) }, StringComparer.OrdinalIgnoreCase) }).ToList();
}
