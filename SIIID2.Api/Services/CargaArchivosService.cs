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
    private readonly CargaIntegridadValidator _cargaIntegridadValidator;

    // Extensiones permitidas para los archivos de carga.
    private readonly string[] _extensionesPermitidas =
    {
        ".csv",
        ".xlsx"
    };

    // Tamaño máximo permitido por archivo: 50 MB.
    private const long TamanioMaximoBytes = 50 * 1024 * 1024;

    public CargaArchivosService(
        IArchivoReader archivoReader,
        CarpetasValidator carpetasValidator,
        DelitosValidator delitosValidator,
        VictimasValidator victimasValidator,
        CargaIntegridadValidator cargaIntegridadValidator)
    {
        _archivoReader = archivoReader;
        _carpetasValidator = carpetasValidator;
        _delitosValidator = delitosValidator;
        _victimasValidator = victimasValidator;
        _cargaIntegridadValidator = cargaIntegridadValidator;
    }

    public async Task<CargaValidacionResponse> ValidarArchivosAsync(IFormFileCollection archivos)
    {
        // Se genera un código por cada intento de carga.
        var response = new CargaValidacionResponse
        {
            CodigoReferencia = GenerarCodigoReferencia()
        };

        // Si no llegó ningún archivo, se regresa error controlado.
        if (archivos == null || archivos.Count == 0)
        {
            response.Errores.Add(new CargaValidacionError
            {
                Archivo = "general",
                Fila = null,
                Columna = "",
                Campo = "",
                Valor = null,
                Codigo = "GENERAL_SIN_ARCHIVOS",
                DescripcionResumen = "No se recibieron archivos",
                Mensaje = "Debe enviar los archivos de carpetas, delitos y víctimas."
            });

            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        var listaArchivos = archivos.ToList();

        // Validamos extensión y tamaño de cada archivo.
        foreach (var archivo in listaArchivos)
        {
            ValidarArchivoBase(archivo, response.Errores);
        }

        // Buscamos cada archivo por su nombre real, no por la key del form-data.
        var archivoCarpetas = BuscarArchivoPorNombre(listaArchivos, "carpeta");
        var archivoDelitos = BuscarArchivoPorNombre(listaArchivos, "delito");
        var archivoVictimas = BuscarArchivoPorNombre(listaArchivos, "victima");

        // Validamos que llegue un archivo de cada tipo.
        ValidarArchivoEsperado(archivoCarpetas, "carpetas", "carpeta", response.Errores);
        ValidarArchivoEsperado(archivoDelitos, "delitos", "delito", response.Errores);
        ValidarArchivoEsperado(archivoVictimas, "victimas", "victima", response.Errores);

        // Validamos que no se envíen archivos duplicados del mismo tipo.
        ValidarDuplicadosPorTipo(listaArchivos, "carpeta", "carpetas", response.Errores);
        ValidarDuplicadosPorTipo(listaArchivos, "delito", "delitos", response.Errores);
        ValidarDuplicadosPorTipo(listaArchivos, "victima", "victimas", response.Errores);

        // Si ya hay errores generales, no intentamos leer los archivos.
        if (response.Errores.Count > 0)
        {
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        // Leemos cada archivo y lo convertimos a filas genéricas.
        var filasCarpetas = await _archivoReader.LeerAsync(archivoCarpetas!);
        var filasDelitos = await _archivoReader.LeerAsync(archivoDelitos!);
        var filasVictimas = await _archivoReader.LeerAsync(archivoVictimas!);

        // Validaciones específicas de cada archivo.
        response.Errores.AddRange(_carpetasValidator.Validar(filasCarpetas));
        response.Errores.AddRange(_delitosValidator.Validar(filasDelitos));
        response.Errores.AddRange(_victimasValidator.Validar(filasVictimas));

        // Las validaciones cruzadas se ejecutan solo si las validaciones internas pasaron.
        // Esto evita errores repetidos o confusos cuando falta estructura básica.
        if (response.Errores.Count == 0)
        {
            response.Errores.AddRange(_cargaIntegridadValidator.Validar(
                filasCarpetas,
                filasDelitos,
                filasVictimas));
        }


        // Construimos resumen y mensaje final.
        FinalizarRespuesta(
            response,
            filasCarpetas.Count,
            filasDelitos.Count,
            filasVictimas.Count);

        return response;
    }

    private void ValidarArchivoBase(
        IFormFile archivo,
        List<CargaValidacionError> errores)
    {
        // Archivo vacío.
        if (archivo.Length == 0)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = archivo.FileName,
                Codigo = "GENERAL_ARCHIVO_VACIO",
                DescripcionResumen = "Archivo vacío",
                Mensaje = $"El archivo \"{archivo.FileName}\" está vacío."
            });
        }

        // Archivo demasiado grande.
        if (archivo.Length > TamanioMaximoBytes)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = archivo.FileName,
                Codigo = "GENERAL_ARCHIVO_EXCEDE_TAMANIO",
                DescripcionResumen = "Archivo excede tamaño máximo",
                Mensaje = $"El archivo \"{archivo.FileName}\" excede el tamaño máximo permitido de 50 MB."
            });
        }

        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();

        // Extensión no permitida.
        if (!_extensionesPermitidas.Contains(extension))
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = archivo.FileName,
                Codigo = "GENERAL_EXTENSION_NO_PERMITIDA",
                DescripcionResumen = "Extensión no permitida",
                Mensaje = $"El archivo \"{archivo.FileName}\" tiene una extensión no permitida. Solo se permiten .csv y .xlsx."
            });
        }
    }

    private IFormFile? BuscarArchivoPorNombre(
        List<IFormFile> archivos,
        string palabraEsperada)
    {
        var palabraNormalizada = NormalizarTexto(palabraEsperada);

        return archivos.FirstOrDefault(archivo =>
        {
            var nombreSinExtension = Path.GetFileNameWithoutExtension(archivo.FileName);
            var nombreNormalizado = NormalizarTexto(nombreSinExtension);

            return nombreNormalizado.Contains(palabraNormalizada);
        });
    }

    private void ValidarArchivoEsperado(
        IFormFile? archivo,
        string tipoArchivo,
        string palabraEsperada,
        List<CargaValidacionError> errores)
    {
        if (archivo != null)
            return;

        errores.Add(new CargaValidacionError
        {
            Archivo = tipoArchivo,
            Codigo = $"GENERAL_FALTA_ARCHIVO_{tipoArchivo.ToUpperInvariant()}",
            DescripcionResumen = $"Falta archivo de {tipoArchivo}",
            Mensaje = $"Debe enviar un archivo cuyo nombre contenga la palabra \"{palabraEsperada}\"."
        });
    }

    private void ValidarDuplicadosPorTipo(
        List<IFormFile> archivos,
        string palabraEsperada,
        string tipoArchivo,
        List<CargaValidacionError> errores)
    {
        var palabraNormalizada = NormalizarTexto(palabraEsperada);

        var coincidencias = archivos
            .Where(archivo =>
            {
                var nombreSinExtension = Path.GetFileNameWithoutExtension(archivo.FileName);
                var nombreNormalizado = NormalizarTexto(nombreSinExtension);

                return nombreNormalizado.Contains(palabraNormalizada);
            })
            .ToList();

        if (coincidencias.Count <= 1)
            return;

        errores.Add(new CargaValidacionError
        {
            Archivo = tipoArchivo,
            Codigo = $"GENERAL_ARCHIVO_DUPLICADO_{tipoArchivo.ToUpperInvariant()}",
            DescripcionResumen = $"Archivo duplicado de {tipoArchivo}",
            Mensaje = $"Se recibió más de un archivo para {tipoArchivo}. Solo debe enviarse uno."
        });
    }

    private void FinalizarRespuesta(
        CargaValidacionResponse response,
        int totalCarpetas,
        int totalDelitos,
        int totalVictimas)
    {
        // Armamos el resumen que puede alimentar una vista tipo tabla.
        response.ResumenValidacion = ConstruirResumenValidacion(
            response.Errores,
            totalCarpetas,
            totalDelitos,
            totalVictimas);

        response.Mensaje = response.EsValido
            ? "La información fue validada correctamente. Puede continuar con el acuse previo."
            : "La información contiene errores de validación.";
    }

    private List<CargaValidacionResumenItem> ConstruirResumenValidacion(
        List<CargaValidacionError> errores,
        int totalCarpetas,
        int totalDelitos,
        int totalVictimas)
    {
        // Totales principales de los tres archivos.
        var resumen = new List<CargaValidacionResumenItem>
        {
            new CargaValidacionResumenItem
            {
                Archivo = "carpetas",
                Codigo = "CARPETAS_TOTAL_REGISTROS",
                Descripcion = "Total de registros en el archivo de expedientes",
                TotalRegistros = totalCarpetas,
                EsError = false
            },
            new CargaValidacionResumenItem
            {
                Archivo = "delitos",
                Codigo = "DELITOS_TOTAL_REGISTROS",
                Descripcion = "Total de registros en el archivo de delitos",
                TotalRegistros = totalDelitos,
                EsError = false
            },
            new CargaValidacionResumenItem
            {
                Archivo = "victimas",
                Codigo = "VICTIMAS_TOTAL_REGISTROS",
                Descripcion = "Total de registros en el archivo de víctimas",
                TotalRegistros = totalVictimas,
                EsError = false
            }
        };

        // Agrupamos errores por código para obtener conteos por tipo de validación.
        var resumenErrores = errores
            .Where(e =>
                !string.IsNullOrWhiteSpace(e.Codigo) &&
                !string.IsNullOrWhiteSpace(e.DescripcionResumen))
            .GroupBy(e => new
            {
                e.Archivo,
                e.Codigo,
                e.DescripcionResumen
            })
            .Select(g => new CargaValidacionResumenItem
            {
                Archivo = g.Key.Archivo,
                Codigo = g.Key.Codigo,
                Descripcion = g.Key.DescripcionResumen,
                TotalRegistros = g.Count(),
                EsError = true
            })
            .OrderBy(x => x.Archivo)
            .ThenBy(x => x.Codigo)
            .ToList();

        resumen.AddRange(resumenErrores);

        return resumen;
    }

    private static string GenerarCodigoReferencia()
    {
        // Referencia corta estilo sistema anterior.
        return Guid.NewGuid()
            .ToString("N")
            .Substring(0, 13)
            .ToLowerInvariant();
    }

    private static string NormalizarTexto(string texto)
    {
        // Normaliza texto para comparar sin importar mayúsculas o acentos.
        var textoNormalizado = texto
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);

        var caracteres = textoNormalizado
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray();

        return new string(caracteres).Normalize(NormalizationForm.FormC);
    }
}