using System.Data;
using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public sealed class SistemaConfiguracionService(IDbConnectionFactory factory)
{
    public long Version { get; private set; }
    public List<SistemaModulo> Modulos { get; private set; } = [];
    public List<SistemaOpcion> Opciones { get; private set; } = [];
    public async Task CargarAsync()
    {
        if (Version > 0) return;
        using var db = factory.CrearConexion();
        using var datos = await db.QueryMultipleAsync("SELECT version FROM dbo.sistema_configuracion_version WHERE id = 1; SELECT clave, nombre, activo FROM dbo.catalogo_modulo WHERE clave IN (N'MENSUAL',N'SEMANAL',N'FEDERAL',N'BANCI'); SELECT m.clave AS Modulo, c.clave, c.descripcion, c.habilitado, c.disponible FROM dbo.sistema_configuracion c JOIN dbo.catalogo_modulo m ON m.id_modulo = c.id_modulo;");
        Version = await datos.ReadSingleAsync<long>();
        Modulos = (await datos.ReadAsync<SistemaModulo>()).ToList();
        Opciones = (await datos.ReadAsync<SistemaOpcion>()).ToList();
    }
    public bool Activa(string modulo, string clave)
    {
        if (Version == 0) throw new InvalidOperationException("La configuración no se ha cargado.");
        if (!Modulos.Any(m => m.Clave == modulo && m.Activo)) return false;
        var opcion = Opciones.SingleOrDefault(o => o.Modulo == modulo && o.Clave == clave) ?? throw new InvalidOperationException($"Falta configurar {modulo}/{clave}.");
        if (!opcion.Habilitado || !opcion.Disponible) return false;
        if (modulo == "MENSUAL" && clave == "RENAPO") return Activa("MENSUAL", "FEMINICIDIO_DATOS_ADICIONALES");
        if (clave == "CRUCE_BANCI") return Modulos.Any(m => m.Clave == "BANCI" && m.Activo);
        return true;
    }
    public async Task<bool> EsAdministradorAsync(int id)
    {
        using var db = factory.CrearConexion();
        return await db.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.sistema_administrador a JOIN dbo.usuario u ON u.id_usuario = a.id_usuario JOIN dbo.roles r ON r.id_rol = u.id_rol WHERE a.id_usuario = @id AND a.activo = 1 AND u.activo = 1 AND r.activo = 1 AND r.rol = N'SUPER_USUARIO') THEN 1 ELSE 0 END AS BIT);", new { id });
    }
    public async Task<bool> EsCuentaProtegidaAsync(int id)
    {
        using var db = factory.CrearConexion();
        return await db.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.sistema_administrador WHERE id_usuario = @id AND activo = 1) THEN 1 ELSE 0 END AS BIT);", new { id });
    }
    public async Task<IDisposable> BloquearLecturaAsync()
    {
        var db = factory.CrearConexion();
        try
        {
            db.Open();
            var resultado = await db.ExecuteScalarAsync<int>("DECLARE @r INT; EXEC @r = sys.sp_getapplock @Resource=N'SIIID2:CONFIGURACION', @LockMode=N'Shared', @LockOwner=N'Session', @LockTimeout=10000; SELECT @r;");
            if (resultado < 0) throw new TimeoutException("La configuración está cambiando. Reintente.");
            return new Bloqueo(db);
        }
        catch { db.Dispose(); throw; }
    }
    private sealed class Bloqueo(IDbConnection db) : IDisposable
    {
        public void Dispose()
        {
            try { db.Execute("EXEC sys.sp_releaseapplock @Resource=N'SIIID2:CONFIGURACION', @LockOwner=N'Session';"); }
            finally { db.Dispose(); }
        }
    }
    public async Task PrepararFeminicidioAsync(List<ArchivoFila> filas, int entidad, int mes, int anio, bool conservar)
    {
        if (Activa("MENSUAL", "FEMINICIDIO_DATOS_ADICIONALES")) return;
        foreach (var fila in filas) foreach (var campo in new[] { "nombre_vicfem", "1apellido_vicfem", "2apellido_vicfem", "curp_vicfem" }) fila.Columnas[campo] = null;
        if (!conservar || filas.Count == 0) return;
        var json = System.Text.Json.JsonSerializer.Serialize(filas.Select((f, i) => new { indice = i, ci = f.Columnas.GetValueOrDefault("id_ci"), delito = f.Columnas.GetValueOrDefault("id_delito"), victima = f.Columnas.GetValueOrDefault("id_vicf") }));
        using var db = factory.CrearConexion();
        const string sql = """
            WITH existentes AS (
                SELECT j.indice AS Indice, v.nombre_vicfem AS Nombre, v.primer_apellido_vicfem AS Primero, v.segundo_apellido_vicfem AS Segundo, v.curp_vicfem AS Curp,
                    ROW_NUMBER() OVER (PARTITION BY j.indice ORDER BY ISNULL(c.fecha_confirmacion, '19000101') DESC, v.id_carga DESC, v.id_victima DESC) AS rn
                FROM OPENJSON(@json) WITH (indice INT, ci NVARCHAR(250), delito NVARCHAR(250), victima NVARCHAR(250)) j
                JOIN dbo.carpeta_investigacion ci ON ci.identificador_carpeta_fiscalia = j.ci AND ci.activo = 1
                JOIN dbo.delito d ON d.id_carpeta_investigacion = ci.id_carpeta_investigacion AND d.identificador_delito_fiscalia = j.delito AND d.activo = 1
                JOIN dbo.victima v ON v.id_delito = d.id_delito AND v.identificador_victima_fiscalia = j.victima AND v.activo = 1
                JOIN dbo.carga c ON c.id_carga = v.id_carga AND c.activo = 1
                WHERE c.id_entidad_federativa = @entidad AND c.mes_corte = @mes AND c.anio_corte = @anio AND c.estado IN ('CONFIRMADO', 'CONFIRMADO_ACTUALIZACION')
            ) SELECT Indice, Nombre, Primero, Segundo, Curp FROM existentes WHERE rn = 1;
            """;
        foreach (var dato in await db.QueryAsync<FeminicidioConservado>(sql, new { json, entidad, mes, anio }, commandTimeout: 120))
        {
            var campos = filas[dato.Indice].Columnas;
            campos["nombre_vicfem"] = dato.Nombre; campos["1apellido_vicfem"] = dato.Primero; campos["2apellido_vicfem"] = dato.Segundo; campos["curp_vicfem"] = dato.Curp;
        }
    }
    private sealed class FeminicidioConservado
    {
        public int Indice { get; set; }
        public string? Nombre { get; set; }
        public string? Primero { get; set; }
        public string? Segundo { get; set; }
        public string? Curp { get; set; }
    }
}
public sealed class SistemaModulo
{
    public string Clave { get; set; } = "";
    public string Nombre { get; set; } = "";
    public bool Activo { get; set; }
}
public sealed class SistemaOpcion
{
    public string Modulo { get; set; } = "";
    public string Clave { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public bool Habilitado { get; set; }
    public bool Disponible { get; set; }
}
