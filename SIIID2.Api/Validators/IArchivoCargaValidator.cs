using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

// Contrato común para los validadores de cada archivo de carga.
// Cada archivo puede tener reglas distintas, pero todos devuelven la misma estructura de errores.
public interface IArchivoCargaValidator
{
    // Nombre lógico del archivo que valida: carpetas, delitos o victimas.
    string NombreArchivo { get; }

    // Ejecuta las validaciones específicas sobre las filas ya leídas.
    List<CargaValidacionError> Validar(List<ArchivoFila> filas);
}
