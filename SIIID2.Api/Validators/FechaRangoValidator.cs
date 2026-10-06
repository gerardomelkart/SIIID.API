namespace SIIID2.Api.Validators;

public static class FechaRangoValidator
{
    public const int AntiguedadMaxima = 120;

    // Día civil de la sede del sistema, igual que la validación de localización.
    public static DateTime Hoy() => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6)).Date;

    public static bool EsValida(DateTime fecha, DateTime hoy) =>
        fecha.Date >= hoy.Date.AddYears(-AntiguedadMaxima) && fecha.Date <= hoy.Date;

    public static string Mensaje(string campo, DateTime hoy) =>
        $"El campo {campo} debe estar entre {hoy.Date.AddYears(-AntiguedadMaxima):dd/MM/yyyy} y {hoy:dd/MM/yyyy}. No se permiten fechas futuras ni con más de {AntiguedadMaxima} años de antigüedad.";
}
