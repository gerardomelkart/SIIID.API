using System.Globalization;
using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

/// <summary>Relaciona el formato de alta por NTRA_CI y asigna llaves exclusivamente en el servidor.</summary>
public static class BanciAltaNormalizer
{
    public static void Preparar(BanciLecturaArchivosResultado lectura, DateTimeOffset? ahora = null)
    {
        var hoy = TimeZoneInfo.ConvertTime(ahora ?? DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City")).Date;
        var inicio = new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-1);
        var fin = inicio.AddMonths(2);
        var carpetas = new Dictionary<string, ArchivoFila>(StringComparer.OrdinalIgnoreCase);
        foreach (var (archivo, filas) in new[] { ("carpetas", lectura.Carpetas), ("delitos", lectura.Delitos), ("victimas", lectura.Victimas) })
            foreach (var fila in filas)
                foreach (var campo in new[] { "id_ci", "id_delito", "id_vicf", "no_banci" })
                    if (Valor(fila, campo).Length > 0)
                        Error(lectura, fila, archivo, campo, "BANCI_ID_AUTOMATICO", "Este campo lo genera el sistema. Déjelo vacío y use NTRA_CI para relacionar los archivos.");
        foreach (var carpeta in lectura.Carpetas)
        {
            var referencia = Valor(carpeta, "ntra_ci");
            if (referencia.Length == 0 || !carpetas.TryAdd(referencia, carpeta))
                Error(lectura, carpeta, "carpetas", "ntra_ci", "BANCI_REFERENCIA_DUPLICADA", "NTRA_CI debe estar informado y aparecer una sola vez en Carpetas.");
            carpeta.Columnas["id_ci"] = NuevoId("CI");
            var texto = Valor(carpeta, "fha_de_ini");
            if (TryFecha(texto, out var fecha))
            {
                carpeta.Columnas["fha_de_ini"] = fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (fecha < inicio || fecha >= fin)
                    Error(lectura, carpeta, "carpetas", "fha_de_ini", "BANCI_PERIODO_NO_PERMITIDO", "La fecha de inicio debe pertenecer al mes en curso o al mes anterior (Ciudad de México).");
            }
        }
        var delitos = new Dictionary<string, ArchivoFila>(StringComparer.OrdinalIgnoreCase);
        foreach (var delito in lectura.Delitos)
        {
            var referencia = Valor(delito, "ntra_ci");
            if (!carpetas.TryGetValue(referencia, out var carpeta))
                Error(lectura, delito, "delitos", "ntra_ci", "BANCI_CARPETA_NO_ENCONTRADA", "NTRA_CI no corresponde a una fila de Carpetas.");
            else delito.Columnas["id_ci"] = Valor(carpeta, "id_ci");
            if (!delitos.TryAdd(referencia, delito))
                Error(lectura, delito, "delitos", "ntra_ci", "BANCI_UN_DELITO_POR_CARPETA", "Sólo se permite un delito por carpeta.");
            delito.Columnas["id_delito"] = NuevoId("DEL");
        }
        foreach (var (referencia, carpeta) in carpetas)
            if (!delitos.ContainsKey(referencia))
                Error(lectura, carpeta, "carpetas", "ntra_ci", "BANCI_UN_DELITO_POR_CARPETA", "Cada carpeta requiere exactamente un delito.");
        foreach (var victima in lectura.Victimas)
        {
            if (!delitos.TryGetValue(Valor(victima, "ntra_ci"), out var delito))
                Error(lectura, victima, "victimas", "ntra_ci", "BANCI_DELITO_NO_ENCONTRADO", "NTRA_CI no corresponde al delito de una carpeta de esta carga.");
            else
            {
                victima.Columnas["id_ci"] = Valor(delito, "id_ci");
                victima.Columnas["id_delito"] = Valor(delito, "id_delito");
            }
            victima.Columnas["id_vicf"] = NuevoId("VIC");
        }
    }

    private static string NuevoId(string tipo) => $"{tipo}-{Guid.NewGuid():N}";
    private static string Valor(ArchivoFila fila, string campo) => fila.Columnas.GetValueOrDefault(campo)?.Trim() ?? "";
    private static bool TryFecha(string texto, out DateTime fecha)
    {
        if (DateTime.TryParseExact(texto, ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha)
            || DateTime.TryParse(texto, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out fecha)) return true;
        if (double.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial) && serial is > 0 and < 2958466)
        {
            fecha = DateTime.FromOADate(serial).Date;
            return true;
        }
        return false;
    }
    private static void Error(BanciLecturaArchivosResultado lectura, ArchivoFila fila, string archivo, string campo, string codigo, string mensaje) =>
        lectura.Errores.Add(new BanciCargaValidacionError { Archivo = archivo, NumeroFila = fila.NumeroFila, Campo = campo, Codigo = codigo, Mensaje = mensaje });
}
