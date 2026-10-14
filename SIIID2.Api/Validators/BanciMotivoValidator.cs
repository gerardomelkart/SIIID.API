using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;
public static class BanciMotivoValidator
{
    public static IEnumerable<BanciCargaValidacionError> Validar(Dictionary<string, string?> datos,
        int fila,
        IReadOnlyList<BanciFormularioOpcion> catalogos)
    {
        var tipo = datos.GetValueOrDefault("constitutiva_delito")?.Trim();
        var motivo = datos.GetValueOrDefault("motivo_desaparicion")?.Trim();
        if(string.IsNullOrEmpty(tipo) && string.IsNullOrEmpty(motivo))
            yield break;
        var opcion = catalogos.SingleOrDefault(o => o.Campo == "constitutiva_delito" && o.Clave == tipo);
        var campo = opcion?.CodigoRegla switch        {
            "DELITO" => "motivo_delito",
            "NO_DELITO" => "motivo_no_delito",
            _ => null
        };
        if(campo is null)
            yield return Error(fila, "constitutiva_delito", "Seleccione una clave activa de Constitutiva de delito junto con motivo_desaparicion.");
        if(string.IsNullOrEmpty(motivo))
            yield return Error(fila, "motivo_desaparicion", "Informe motivo_desaparicion junto con constitutiva_delito.");
        else if(campo is not null)
        {
            if(catalogos.Count(o => o.Campo == campo && o.Clave == motivo) != 1)
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
