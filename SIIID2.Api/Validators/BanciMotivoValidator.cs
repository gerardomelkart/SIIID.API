using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

public static class BanciMotivoValidator
{
    public static IEnumerable<BanciCargaValidacionError> Validar(
        Dictionary<string, string?> datos, int fila, IReadOnlyList<BanciFormularioOpcion> catalogos)
    {
        var tipo = datos.GetValueOrDefault("constitutiva_delito")?.Trim();
        var motivo = datos.GetValueOrDefault("motivo_desaparicion")?.Trim();
        if (string.IsNullOrEmpty(tipo) && string.IsNullOrEmpty(motivo)) yield break;
        if (tipo is not ("1" or "2"))
            yield return Error(fila, "constitutiva_delito", "Indique 1 = Delito o 2 = No delito junto con motivo_desaparicion.");
        if (string.IsNullOrEmpty(motivo))
            yield return Error(fila, "motivo_desaparicion", "Informe motivo_desaparicion junto con constitutiva_delito.");
        else if (tipo is "1" or "2")
        {
            var campo = tipo == "1" ? "motivo_delito" : "motivo_no_delito";
            if (catalogos.Count(o => o.Campo == campo && o.Clave == motivo) != 1)
                yield return Error(fila, "motivo_desaparicion", "La clave no pertenece al catálogo de la opción seleccionada. Consulte la plantilla actualizada.");
        }
    }

    private static BanciCargaValidacionError Error(int fila, string campo, string mensaje) => new()
    {
        Archivo = "actualizacion",
        NumeroFila = fila,
        Campo = campo,
        Codigo = "BANCI_MOTIVO_INVALIDO",
        Mensaje = mensaje
    };
}
