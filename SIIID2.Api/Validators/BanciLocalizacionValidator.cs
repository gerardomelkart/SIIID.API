using System.Globalization;
using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

public static class BanciLocalizacionValidator
{
    // Día civil de la sede del sistema (Ciudad de México), independiente de la zona del servidor.
    public static DateTime Hoy() => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).Date;

    public static IEnumerable<BanciCargaValidacionError> Validar(Dictionary<string, string?> datos, BanciVictimaIdentificada victima, int fila, DateTime hoy)
    {
        var campos = new[] { "fecha_localizacion", "localizado_o_no_localizado", "con_o_sin_vida", "voluntaria_o_fue_delito" };
        if (!campos.Any(c => !string.IsNullOrWhiteSpace(datos.GetValueOrDefault(c)))) yield break;
        var valor = datos.GetValueOrDefault("fecha_localizacion");
        var fecha = valor == null ? victima.FechaLocalizacion : DateTime.ParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!fecha.HasValue) yield break;
        if (fecha.Value.Date > hoy.Date) yield return Error(fila, "La fecha de localización no puede ser posterior a hoy.");
        if (victima.FechaInicio.HasValue && fecha.Value.Date < victima.FechaInicio.Value.Date) yield return Error(fila, "La fecha de localización no puede ser anterior al inicio de la carpeta.");
        if (victima.FechaHechos.HasValue && fecha.Value.Date < victima.FechaHechos.Value.Date) yield return Error(fila, "La fecha de localización no puede ser anterior a la fecha de los hechos.");
    }

    private static BanciCargaValidacionError Error(int fila, string mensaje) => new() { Archivo = "actualizacion", NumeroFila = fila, Campo = "fecha_localizacion", Codigo = "BANCI_FECHA_LOCALIZACION", Mensaje = mensaje };
}
