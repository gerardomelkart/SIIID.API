using System.Globalization;
using System.Text.RegularExpressions;

namespace SIIID2.Api.Validators;
// Validación local compartida: no depende del encendido de RENAPO ni de BANCI.
public static class CurpFormatoValidator
{
    private static readonly Regex Formato = new(@"\A[A-Z][AEIOUX][A-Z]{2}[0-9]{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12][0-9]|3[01])[HM](?:AS|BC|BS|CC|CL|CM|CS|CH|DF|DG|GT|GR|HG|JC|MC|MN|MS|NT|NL|OC|PL|QT|QR|SP|SL|SR|TC|TS|TL|VZ|YN|ZS|NE)[B-DF-HJ-NP-TV-Z]{3}[A-Z0-9][0-9]\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    public static bool EsFormatoValido(string curp)
    {
        if(!Formato.IsMatch(curp))
            return false;
        var anio = (char.IsDigit(curp[16]) ? "19" : "20") + curp.Substring(4, 2);
        if(!DateTime.TryParseExact(anio + curp.Substring(6, 4), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            || fecha > DateTime.Today)
            return false;
        const string caracteres = "0123456789ABCDEFGHIJKLMNÑOPQRSTUVWXYZ";
        var suma = 0;
        for(var i = 0; i < 17; i++)
            suma += caracteres.IndexOf(curp[i]) * (18 - i);
        return (10 - suma % 10) % 10 == curp[17] - '0';
    }
}
