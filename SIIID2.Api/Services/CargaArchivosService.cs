using System.Globalization;
using System.Text;
using SIIID2.Api.Models;
using SIIID2.Api.Readers;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;

// Servicio principal del flujo de carga.
// Coordina validación base, lectura de archivos y validadores específicos.
public class CargaArchivosService : ICargaArchivosService
{
    private readonly IArchivoReader _archivoReader;
    private readonly CarpetasValidator _carpetasValidator;
    private readonly DelitosValidator _delitosValidator;
    private readonly VictimasValidator _victimasValidator;
    // Extensiones permitidas por ahora para archivos de carga.
    private readonly string[] _extensionesPermitidas =
    {
        ".csv",
        ".xlsx"
    };
    // Tamaño máximo permitido por archivo: 50 MB.
    private const long TamanioMaximoBytes = 50 * 1024 * 1024;
    public CargaArchivosService(IArchivoReader archivoReader, CarpetasValidator carpetasValidator, DelitosValidator delitosValidator, VictimasValidator victimasValidator)
    {
        _archivoReader = archivoReader;
        _carpetasValidator = carpetasValidator;
        _delitosValidator = delitosValidator;
        _victimasValidator = victimasValidator;
    }
    public async Task<CargaValidacionResponse> ValidarArchivosAsync(IFormFileCollection archivos)
    {
        var response = new CargaValidacionResponse();
        // Si no llegó ningún archivo, se corta el flujo y se devuelve error controlado.
        if (archivos == null || archivos.Count == 0)
        {
            response.Errores.Add(new CargaValidacionError
            {
                Archivo = "general",
                Mensaje = "Debe enviar los archivos de carpetas, delitos y víctimas."
            });
            return response;
        }
        var listaArchivos = archivos.ToList();
        // Validación base para cada archivo: vacío, tamaño y extensión.
        foreach (var archivo in listaArchivos)
        {
            ValidarArchivoBase(archivo, response.Errores);
        }
        // Se identifica cada archivo por su nombre real, no por la llave del form-data.
        var archivoCarpetas = BuscarArchivoPorNombre(listaArchivos, "carpeta");
        var archivoDelitos = BuscarArchivoPorNombre(listaArchivos, "delito");
        var archivoVictimas = BuscarArchivoPorNombre(listaArchivos, "victima");
        // Validamos que exista exactamente un archivo de cada tipo lógico.
        ValidarArchivoEsperado(archivoCarpetas, "carpetas", response.Errores);
        ValidarArchivoEsperado(archivoDelitos, "delitos", response.Errores);
        ValidarArchivoEsperado(archivoVictimas, "victimas", response.Errores);

        ValidarDuplicadosPorTipo(listaArchivos, "carpeta", "carpetas", response.Errores);
        ValidarDuplicadosPorTipo(listaArchivos, "delito", "delitos", response.Errores);
        ValidarDuplicadosPorTipo(listaArchivos, "victima", "victimas", response.Errores);
        // Si hay errores de archivo, no tiene sentido leer contenido todavía.
        if (response.Errores.Count > 0)
        {
            return response;
        }
        // Lectura de cada archivo a una estructura común: List<ArchivoFila>.
        var filasCarpetas = await _archivoReader.LeerAsync(archivoCarpetas!);
        var filasDelitos = await _archivoReader.LeerAsync(archivoDelitos!);
        var filasVictimas = await _archivoReader.LeerAsync(archivoVictimas!);
        // Ejecución de validadores específicos por archivo.
        response.Errores.AddRange(_carpetasValidator.Validar(filasCarpetas));
        response.Errores.AddRange(_delitosValidator.Validar(filasDelitos));
        response.Errores.AddRange(_victimasValidator.Validar(filasVictimas));
        return response;
    }
    private void ValidarArchivoBase(IFormFile archivo, List<CargaValidacionError> errores)
    {
        if (archivo.Length == 0)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = archivo.FileName,
                Mensaje = $"El archivo \"{archivo.FileName}\" está vacío."
            });
        }
        if (archivo.Length > TamanioMaximoBytes)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = archivo.FileName,
                Mensaje = $"El archivo \"{archivo.FileName}\" excede el tamaño máximo permitido de 50 MB."
            });
        }
        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();
        if (!_extensionesPermitidas.Contains(extension))
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = archivo.FileName,
                Mensaje = $"El archivo \"{archivo.FileName}\" tiene una extensión no permitida. Solo se permiten .csv y .xlsx."
            });
        }
    }
    private IFormFile? BuscarArchivoPorNombre(List<IFormFile> archivos, string palabraEsperada)
    {
        var palabraNormalizada = NormalizarTexto(palabraEsperada);
        // Busca la palabra esperada dentro del nombre real del archivo.
        // Ejemplos válidos: 09_202602_carpetas.xlsx, delitosCDMX.csv, víctimas_enero.xlsx.
        return archivos.FirstOrDefault(archivo =>
        {
            var nombreSinExtension = Path.GetFileNameWithoutExtension(archivo.FileName);
            var nombreNormalizado = NormalizarTexto(nombreSinExtension);

            return nombreNormalizado.Contains(palabraNormalizada);
        });
    }
    private void ValidarArchivoEsperado(IFormFile? archivo, string tipoArchivo, List<CargaValidacionError> errores)
    {
        if (archivo != null)
        {
            return;
        }
        errores.Add(new CargaValidacionError
        {
            Archivo = tipoArchivo,
            Mensaje = $"Debe enviar un archivo cuyo nombre contenga la palabra \"{tipoArchivo}\"."
        });
    }
    private void ValidarDuplicadosPorTipo(List<IFormFile> archivos, string palabraEsperada, string tipoArchivo, List<CargaValidacionError> errores)
    {
        var palabraNormalizada = NormalizarTexto(palabraEsperada);
        
        var coincidencias = archivos
            .Where(archivo =>
            {
                var nombreSinExtension = Path.GetFileNameWithoutExtension(archivo.FileName);
                var nombreNormalizado = NormalizarTexto(nombreSinExtension);
                return nombreNormalizado.Contains(palabraNormalizada);
            }).ToList();

        if (coincidencias.Count <= 1)
        {
            return;
        }
        errores.Add(new CargaValidacionError
        {
            Archivo = tipoArchivo,
            Mensaje = $"Se recibió más de un archivo para {tipoArchivo}. Solo debe enviarse uno."
        });
    }
    private static string NormalizarTexto(string texto)
    {
        // Normaliza texto para comparar nombres sin importar mayúsculas ni acentos.
        var textoNormalizado = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);

        var caracteres = textoNormalizado
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(caracteres).Normalize(NormalizationForm.FormC);
    }
}
