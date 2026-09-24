using System.Globalization;
using System.Text.RegularExpressions;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Validators;

public sealed class BanciRenapoValidator(IRenapoCurpService renapo, SistemaConfiguracionService config)
{
    private static readonly Regex Formato = new(@"\A[A-Z][AEIOUX][A-Z]{2}[0-9]{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12][0-9]|3[01])[HM](?:AS|BC|BS|CC|CL|CM|CS|CH|DF|DG|GT|GR|HG|JC|MC|MN|MS|NT|NL|OC|PL|QT|QR|SP|SL|SR|TC|TS|TL|VZ|YN|ZS|NE)[B-DF-HJ-NP-TV-Z]{3}[A-Z0-9][0-9]\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static bool EsFormatoValido(string curp)
    {
        if (!Formato.IsMatch(curp)) return false;
        var anio = (char.IsDigit(curp[16]) ? "19" : "20") + curp.Substring(4, 2);
        if (!DateTime.TryParseExact(anio + curp.Substring(6, 4), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha) || fecha > DateTime.Today) return false;
        const string caracteres = "0123456789ABCDEFGHIJKLMNÑOPQRSTUVWXYZ";
        var suma = 0;
        for (var i = 0; i < 17; i++) suma += caracteres.IndexOf(curp[i]) * (18 - i);
        return (10 - suma % 10) % 10 == curp[17] - '0';
    }

    public async Task<(List<BanciCargaValidacionError> Errores, List<BanciCargaValidacionError> Advertencias)> ValidarAsync(IEnumerable<ArchivoFila> filas, CancellationToken cancellationToken = default)
    {
        var errores = new List<BanciCargaValidacionError>();
        var advertencias = new List<BanciCargaValidacionError>();
        var grupos = filas.Where(f => !string.IsNullOrWhiteSpace(f.Columnas.GetValueOrDefault("curp"))).GroupBy(f => f.Columnas["curp"]!.Trim().ToUpperInvariant()).ToList();
        foreach (var grupo in grupos)
        {
            foreach (var fila in grupo) fila.Columnas["curp"] = grupo.Key;
            if (!EsFormatoValido(grupo.Key)) errores.Add(Error(grupo.First(), "BANCI_CURP_FORMATO", "La CURP debe tener estructura, fecha y dígito verificador válidos. Si no cuenta con CURP, deje el campo vacío."));
        }
        if (errores.Count > 0 || !config.Activa("BANCI", "RENAPO")) return (errores, advertencias);
        foreach (var lote in grupos.Chunk(4))
        {
            var resultados = await Task.WhenAll(lote.Select(async g => (Grupo: g, Resultado: await renapo.ConsultarAsync(g.Key, cancellationToken))));
            var noDisponible = false;
            foreach (var (grupo, resultado) in resultados)
            {
                if (resultado.Estado == EstadoConsultaRenapo.Encontrada) continue;
                if (resultado.Estado == EstadoConsultaRenapo.NoDisponible) { noDisponible = true; continue; }
                var mensaje = resultado.Estado switch
                {
                    EstadoConsultaRenapo.NoEncontrada => "La CURP no existe en RENAPO. Corríjala antes de continuar.",
                    EstadoConsultaRenapo.CurpInvalida => "RENAPO rechazó la CURP. Corríjala antes de continuar.",
                    _ => "RENAPO presenta un error de configuración, autorización o respuesta. Debe resolverse antes de continuar."
                };
                errores.Add(Error(grupo.First(), "BANCI_RENAPO_" + resultado.Estado.ToString().ToUpperInvariant(), mensaje));
            }
            if (!noDisponible) continue;
            advertencias.Add(new() { Archivo = "victimas", Campo = "curp", Codigo = "BANCI_RENAPO_NO_DISPONIBLE", Mensaje = "La validación RENAPO quedó incompleta por indisponibilidad temporal. Revise y acepte explícitamente esta advertencia si desea continuar." });
            break;
        }
        return (errores, advertencias);
    }

    private static BanciCargaValidacionError Error(ArchivoFila fila, string codigo, string mensaje) => new() { Archivo = "victimas", NumeroFila = fila.NumeroFila, Campo = "curp", Codigo = codigo, Mensaje = mensaje };
}
