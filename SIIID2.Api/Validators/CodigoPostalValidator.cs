using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

public static class CodigoPostalValidator
{
    public static List<CargaValidacionError> Validar(List<ArchivoFila> filasDelitos)
    {
        var errores = new List<CargaValidacionError>();

        foreach (var fila in filasDelitos)
        {
            fila.Columnas.TryGetValue("cp", out var valor);

            if (string.IsNullOrWhiteSpace(valor))
            {
                continue;
            }

            valor = valor.Trim();

            if (valor.All(c => c == '0'))
            {
                continue;
            }

            if (valor.Length == 5 && valor.All(char.IsDigit))
            {
                continue;
            }

            errores.Add(new CargaValidacionError
            {
                Archivo = "delitos",
                Fila = fila.NumeroFila,
                Columna = "cp",
                Campo = "cp",
                Valor = valor,
                Codigo = "DELITOS_CP_FORMATO_INCORRECTO",
                DescripcionResumen = "Código postal con formato incorrecto",
                Mensaje = "El campo cp, cuando se informa, debe contener exactamente 5 dígitos. Los valores vacíos o compuestos únicamente por ceros se consideran sin información."
            });
        }

        return errores;
    }
}
