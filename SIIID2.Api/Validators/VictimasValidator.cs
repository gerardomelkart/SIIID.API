using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

// Validador específico para el archivo de víctimas.
// Aún no tiene reglas reales; se dejó la estructura lista para agregarlas.
public class VictimasValidator : IArchivoCargaValidator
{
    public string NombreArchivo => "victimas";

    public List<CargaValidacionError> Validar(List<ArchivoFila> filas)
    {
        var errores = new List<CargaValidacionError>();

        foreach (var fila in filas)
        {
            // Aquí después van las validaciones reales de víctimas.
            // Ejemplo futuro: columnas obligatorias, edad, sexo, nacionalidad, relación con delitos, etc.
        }

        return errores;
    }

    private void AgregarError(
        List<CargaValidacionError> errores,
        ArchivoFila fila,
        string columna,
        string mensaje)
    {
        fila.Columnas.TryGetValue(columna, out var valor);

        errores.Add(new CargaValidacionError
        {
            Archivo = NombreArchivo,
            Fila = fila.NumeroFila,
            Columna = columna,
            Campo = columna,
            Valor = valor,
            Mensaje = mensaje
        });
    }
}