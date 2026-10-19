namespace SIIID2.Api.Services;

public static class ColoresExcelModulo
{
    public static string Principal(string modulo) => modulo switch
    {
        "mensual" => "691C32",
        "federal" => "1F4E79",
        "semanal" => "235B4E",
        "banci" => "6F2C91",
        _ => throw new ArgumentException("Módulo no válido.")
    };

    public static string Claro(string modulo) => modulo switch
    {
        "mensual" => "F5E8ED",
        "federal" => "E7EFF7",
        "semanal" => "E6F0EB",
        "banci" => "EEE1F6",
        _ => throw new ArgumentException("Módulo no válido.")
    };
}
