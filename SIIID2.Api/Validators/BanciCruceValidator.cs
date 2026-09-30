using System.Globalization;
using System.Text.Json;
using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

public sealed record BanciCruceArchivos(List<ArchivoFila> Carpetas, List<ArchivoFila> Delitos, List<ArchivoFila> Victimas);

// Los identificadores sólo relacionan filas DENTRO de cada fuente.
public static class BanciCruceValidator
{
    public static readonly string[] CamposCarpeta = ["fha_de_ini", "hra_de_ini", "rmen_de_hchos"];
    public static readonly string[] CamposDelito = ["dto", "moda_dto", "forma_acc", "fha_de_hchos", "hra_de_hchos", "emto_com_dto", "grdo_cons", "clasf_de_dto", "nom_ent_hchos", "id_ent_hchos", "nom_mun_hchos", "id_mun_hchos", "nom_loc_hchos", "id_loc_hchos", "nom_col_hchos", "id_col_hchos", "cp", "coord_x", "coord_y", "dom_hchos"];
    public static readonly string[] CamposVictima = ["id_tv", "id_tpm", "sexo", "genero", "pob", "disc", "fha_nac", "edad", "nacional"];
    public static string Valor(ArchivoFila fila, string campo) => fila.Columnas.GetValueOrDefault(campo)?.Trim() ?? "";
    public static string Clave(string valor) => valor.Trim().ToUpperInvariant();
    public static List<CargaValidacionError> Comparar(BanciCruceArchivos mensual, BanciCruceArchivos banci, ISet<string> clasificaciones)
    {
        var errores = new List<CargaValidacionError>();
        var delitosMensuales = mensual.Delitos.Where(d => clasificaciones.Contains(Valor(d, "clasf_de_dto"))).ToList();
        var mdPorCarpeta = delitosMensuales.ToLookup(d => Clave(Valor(d, "id_ci")));
        var bdPorCarpeta = banci.Delitos.ToLookup(d => Clave(Valor(d, "id_ci")));
        var mvPorDelito = mensual.Victimas.ToLookup(v => (Clave(Valor(v, "id_ci")), Clave(Valor(v, "id_delito"))));
        var bvPorDelito = banci.Victimas.ToLookup(v => (Clave(Valor(v, "id_ci")), Clave(Valor(v, "id_delito"))));
        var carpetasMensuales = mensual.Carpetas.Where(c => mdPorCarpeta.Contains(Clave(Valor(c, "id_ci")))).GroupBy(c => Clave(Valor(c, "ntra_ci"))).ToDictionary(g => g.Key, g => g.ToList());
        var carpetasBanci = banci.Carpetas.GroupBy(c => Clave(Valor(c, "ntra_ci"))).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var ntra in carpetasMensuales.Keys.Union(carpetasBanci.Keys))
        {
            if (!carpetasMensuales.TryGetValue(ntra, out var mc)) { Error("carpetas", null, ntra, "ntra_ci", "BANCI_OMITIDA", "Falta en Consolidado la carpeta y su información de desaparición registrada en BANCI."); continue; }
            if (!carpetasBanci.TryGetValue(ntra, out var bc)) { Error("carpetas", mc[0], ntra, "ntra_ci", "BANCI_NO_REPORTADA", "Debe registrar esta información en BANCI antes de volver a cargar el Consolidado. Si no tiene acceso, solicítelo a un usuario autorizado."); continue; }
            if (mc.Count != 1 || bc.Count != 1) { Error("carpetas", mc[0], ntra, "ntra_ci", "BANCI_AMBIGUA", "NTRA_CI no identifica una única carpeta en ambas fuentes. Corrija la duplicidad."); continue; }
            CompararCampos("carpetas", ntra, mc[0], bc[0], CamposCarpeta);
            var md = mdPorCarpeta[Clave(Valor(mc[0], "id_ci"))].ToList();
            var bd = bdPorCarpeta[Clave(Valor(bc[0], "id_ci"))].ToList();
            if (md.Count != 1 || bd.Count != 1) { Error("delitos", mc[0], ntra, "clasf_de_dto", "BANCI_CANTIDAD_DELITOS", $"Se requiere un delito de desaparición por carpeta: Consolidado={md.Count}; BANCI={bd.Count}."); continue; }
            CompararCampos("delitos", ntra, md[0], bd[0], CamposDelito);
            var mv = mvPorDelito[(Clave(Valor(mc[0], "id_ci")), Clave(Valor(md[0], "id_delito")))].ToList();
            var pendientes = bvPorDelito[(Clave(Valor(bc[0], "id_ci")), Clave(Valor(bd[0], "id_delito")))].ToList();
            var grupos = pendientes.GroupBy(v => Firma(v, true)).ToDictionary(g => g.Key, g => new Queue<ArchivoFila>(g));
            var faltantes = new List<ArchivoFila>();
            foreach (var v in mv)
            {
                if (grupos.TryGetValue(Firma(v, false), out var grupo) && grupo.Count > 0) grupo.Dequeue();
                else faltantes.Add(v);
            }
            pendientes = grupos.Values.SelectMany(g => g).ToList();
            if (faltantes.Count == 1 && pendientes.Count == 1) CompararCampos("victimas", ntra, faltantes[0], pendientes[0], CamposVictima);
            else
            {
                foreach (var v in faltantes) Error("victimas", v, ntra, "", "BANCI_VICTIMA_DIFERENTE", "Registro del Consolidado sin equivalente en BANCI: " + Describir(v));
                foreach (var v in pendientes) Error("victimas", null, ntra, "", "BANCI_VICTIMA_OMITIDA", "Registro BANCI sin equivalente en Consolidado: " + Describir(v));
            }
        }
        return errores;

        void CompararCampos(string archivo, string ntra, ArchivoFila m, ArchivoFila b, string[] campos)
        {
            foreach (var campo in campos.Where(c => Normal(m, c, false) != Normal(b, c, true)))
                Error(archivo, m, ntra, campo, "BANCI_DIFERENCIA", $"Consolidado: '{Valor(m, campo)}'; BANCI: '{Valor(b, campo)}'. Corrija la diferencia antes de continuar.");
        }
        void Error(string archivo, ArchivoFila? fila, string ntra, string campo, string codigo, string mensaje) => errores.Add(new() { Archivo = archivo, Fila = fila?.NumeroFila, Columna = campo, Campo = campo, Valor = fila == null ? ntra : Valor(fila, campo), Codigo = codigo, DescripcionResumen = "Cruce Consolidado–BANCI", Mensaje = $"Carpeta {ntra}: {mensaje}" });
    }
    private static string Firma(ArchivoFila v, bool banci) => JsonSerializer.Serialize(CamposVictima.Select(c => Normal(v, c, banci)));
    private static string Describir(ArchivoFila v) => JsonSerializer.Serialize(CamposVictima.ToDictionary(c => c, c => Valor(v, c)));
    private static string Normal(ArchivoFila fila, string campo, bool banci)
    {
        var valor = Clave(Valor(fila, campo));
        if (valor is "" or "ND" or "N/D" or "NO DISPONIBLE") return "";
        if (banci && (campo is "pob" or "disc") && valor == "0") return "2";
        if (campo.StartsWith("fha_", StringComparison.Ordinal))
        {
            if (DateTime.TryParseExact(valor, ["yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
                || DateTime.TryParse(valor, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out fecha)) return fecha.ToString("yyyy-MM-dd");
            if (double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial > 0 && serial < 60000) return DateTime.FromOADate(serial).ToString("yyyy-MM-dd");
        }
        if (campo.StartsWith("hra_", StringComparison.Ordinal))
        {
            if (double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial >= 0 && serial < 1) return Math.Round(TimeSpan.FromDays(serial).TotalSeconds).ToString(CultureInfo.InvariantCulture);
            if (TimeSpan.TryParse(valor, CultureInfo.InvariantCulture, out var hora)) return Math.Round(hora.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }
        if ((campo.StartsWith("id_", StringComparison.Ordinal) || campo is "forma_acc" or "emto_com_dto" or "grdo_cons" or "sexo" or "genero" or "pob" or "disc" or "edad" or "nacional" or "coord_x" or "coord_y" or "cp") && decimal.TryParse(valor, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var numero)) return numero.ToString("G29", CultureInfo.InvariantCulture);
        return valor;
    }
}
