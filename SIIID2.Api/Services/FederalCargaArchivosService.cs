using System.Globalization;
using System.Text;
using SIIID2.Api.Models;
using SIIID2.Api.Readers;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;

public class FederalCargaArchivosService : IFederalCargaArchivosService
{
    private readonly IArchivoReader _archivoReader;
    private readonly CarpetasValidator _carpetasValidator;
    private readonly DelitosValidator _delitosValidator;
    private readonly VictimasValidator _victimasValidator;
    private readonly CargaIntegridadValidator _cargaIntegridadValidator;
    private readonly CatalogosValidator _catalogosValidator;
    private readonly IFederalCargaRepository _federalCargaRepository;

    private readonly string[] _extensionesPermitidas = [".csv", ".xlsx"];
    private const long TamanioMaximoBytes = 50 * 1024 * 1024;

    public FederalCargaArchivosService(IArchivoReader archivoReader, CarpetasValidator carpetasValidator, DelitosValidator delitosValidator, VictimasValidator victimasValidator, CargaIntegridadValidator cargaIntegridadValidator, CatalogosValidator catalogosValidator, IFederalCargaRepository federalCargaRepository)
    {
        _archivoReader = archivoReader;
        _carpetasValidator = carpetasValidator;
        _delitosValidator = delitosValidator;
        _victimasValidator = victimasValidator;
        _cargaIntegridadValidator = cargaIntegridadValidator;
        _catalogosValidator = catalogosValidator;
        _federalCargaRepository = federalCargaRepository;
    }

    public async Task<CargaValidacionResponse> ValidarArchivosAsync(IFormCollection form, int idUsuarioCarga)
    {
        var usuarioCarga = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioCarga);

        if (usuarioCarga == null)
        {
            return RespuestaUsuarioInvalido(idUsuarioCarga);
        }

        if (!usuarioCarga.HabilitaCarga)
        {
            return RespuestaUsuarioSinPermiso(usuarioCarga);
        }

        var response = new CargaValidacionResponse { CodigoReferencia = GenerarCodigoReferencia() };
        var archivos = form.Files;

        if (archivos == null || archivos.Count == 0)
        {
            response.Errores.Add(new CargaValidacionError
            {
                Archivo = "general",
                Codigo = "FEDERAL_GENERAL_SIN_ARCHIVOS",
                DescripcionResumen = "No se recibieron archivos",
                Mensaje = "Debe enviar los archivos federales de carpetas, delitos y víctimas."
            });

            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        var listaArchivos = archivos.ToList();

        foreach (var archivo in listaArchivos) ValidarArchivoBase(archivo, response.Errores);

        var archivoCarpetas = BuscarArchivoPorNombre(listaArchivos, "carpeta");
        var archivoDelitos = BuscarArchivoPorNombre(listaArchivos, "delito");
        var archivoVictimas = BuscarArchivoPorNombre(listaArchivos, "victima");

        ValidarArchivoEsperado(archivoCarpetas, "carpetas", "carpeta", response.Errores);
        ValidarArchivoEsperado(archivoDelitos, "delitos", "delito", response.Errores);
        ValidarArchivoEsperado(archivoVictimas, "victimas", "victima", response.Errores);
        ValidarDuplicadosPorTipo(listaArchivos, "carpeta", "carpetas", response.Errores);
        ValidarDuplicadosPorTipo(listaArchivos, "delito", "delitos", response.Errores);
        ValidarDuplicadosPorTipo(listaArchivos, "victima", "victimas", response.Errores);

        if (response.Errores.Count > 0)
        {
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        var erroresEstructura = await ValidarEstructuraArchivosAsync(archivoCarpetas!, archivoDelitos!, archivoVictimas!);

        if (erroresEstructura.Count > 0)
        {
            response.Errores.AddRange(erroresEstructura);
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        var filasCarpetas = await _archivoReader.LeerAsync(archivoCarpetas!);
        var filasDelitos = await _archivoReader.LeerAsync(archivoDelitos!);
        var filasVictimas = await _archivoReader.LeerAsync(archivoVictimas!);

        response.Errores.AddRange(_carpetasValidator.Validar(filasCarpetas));
        response.Errores.AddRange(_delitosValidator.Validar(filasDelitos));
        response.Errores.AddRange(_victimasValidator.Validar(filasVictimas));

        var advertenciasPendientes = new List<CargaValidacionError>();

        if (response.Errores.Count == 0)
        {
            var erroresIntegridad = _cargaIntegridadValidator.Validar(filasCarpetas, filasDelitos, filasVictimas);

            if (usuarioCarga.EsSuperUsuario)
            {
                advertenciasPendientes.AddRange(erroresIntegridad.Where(x => x.Codigo == "INTEGRIDAD_FECHA_HECHOS_MAYOR_FECHA_INICIO"));
                erroresIntegridad = erroresIntegridad.Where(x => x.Codigo != "INTEGRIDAD_FECHA_HECHOS_MAYOR_FECHA_INICIO").ToList();
            }

            response.Errores.AddRange(erroresIntegridad);
        }

        response.Errores.AddRange(await _catalogosValidator.ValidarFederalAsync(filasCarpetas, filasDelitos, filasVictimas));

        var mesCorte = ObtenerMesCorteDesdeCarpetas(filasCarpetas);
        var anioCorte = ObtenerAnioCorteDesdeCarpetas(filasCarpetas);

        if (response.Errores.Count == 0)
        {
            if (await _federalCargaRepository.ExisteCargaConfirmadaAsync(mesCorte, anioCorte))
            {
                response.Errores.Add(new CargaValidacionError
                {
                    Archivo = "general",
                    Codigo = "FEDERAL_CARGA_PERIODO_YA_CONFIRMADO",
                    DescripcionResumen = "Ya existe carga federal confirmada",
                    Mensaje = $"Ya existe información federal confirmada para el periodo {mesCorte:00}/{anioCorte}. Para continuar debe usar el flujo de actualización federal."
                });
            }
            else
            {
                var cargaPendiente = await _federalCargaRepository.ObtenerCodigoCargaPendienteAsync(mesCorte, anioCorte);

                if (cargaPendiente != null)
                {
                    var enRevisionAdministrativa = string.Equals(cargaPendiente.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase);

                    response.Errores.Add(new CargaValidacionError
                    {
                        Archivo = "general",
                        Valor = cargaPendiente.CodigoReferencia,
                        Codigo = enRevisionAdministrativa ? "FEDERAL_CARGA_PENDIENTE_APROBACION" : "FEDERAL_CARGA_PENDIENTE_EXISTENTE",
                        DescripcionResumen = enRevisionAdministrativa ? "Carga federal en revisión administrativa" : "Ya existe carga federal pendiente",
                        Mensaje = enRevisionAdministrativa
                            ? $"Ya existe una carga federal en revisión administrativa para el periodo {mesCorte:00}/{anioCorte}. Código de referencia pendiente: {cargaPendiente.CodigoReferencia}."
                            : $"Ya existe una carga federal validada pendiente de confirmar para el periodo {mesCorte:00}/{anioCorte}. Código de referencia pendiente: {cargaPendiente.CodigoReferencia}."
                    });
                }
            }
        }

        if (response.Errores.Count == 0)
        {
            response.Advertencias.AddRange(advertenciasPendientes);
            response.Advertencias.AddRange(_delitosValidator.ValidarAdvertencias(filasDelitos));
            response.Advertencias.AddRange(_cargaIntegridadValidator.ValidarAdvertencias(filasDelitos, filasVictimas));
        }

        FinalizarRespuesta(response, filasCarpetas.Count, filasDelitos.Count, filasVictimas.Count);

        if (response.Errores.Any(x => x.Codigo is "FEDERAL_CARGA_PERIODO_YA_CONFIRMADO" or "FEDERAL_CARGA_PENDIENTE_EXISTENTE" or "FEDERAL_CARGA_PENDIENTE_APROBACION")) return response;

        var estadoCarga = response.EsValido ? "VALIDADO_PENDIENTE" : "RECHAZADO_VALIDACION";
        var mensajeError = response.EsValido ? null : $"La información federal contiene errores de validación. Total de errores: {response.Errores.Count}.";

        await _federalCargaRepository.GuardarIntentoCargaAsync(idUsuarioCarga, response.CodigoReferencia, mesCorte, anioCorte, filasCarpetas.Count, filasDelitos.Count, filasVictimas.Count, estadoCarga, mensajeError, response.Advertencias, filasCarpetas, filasDelitos, filasVictimas);

        return response;
    }

    private static CargaValidacionResponse RespuestaUsuarioInvalido(int idUsuarioCarga)
    {
        var response = new CargaValidacionResponse { CodigoReferencia = GenerarCodigoReferencia() };

        response.Errores.Add(new CargaValidacionError
        {
            Archivo = "general",
            Columna = "idUsuarioCarga",
            Campo = "idUsuarioCarga",
            Valor = idUsuarioCarga.ToString(CultureInfo.InvariantCulture),
            Codigo = "FEDERAL_GENERAL_USUARIO_CARGA_NO_EXISTE",
            DescripcionResumen = "Usuario federal no habilitado",
            Mensaje = "El usuario autenticado no existe, está inactivo o no tiene habilitado el módulo FEDERAL."
        });

        FinalizarRespuesta(response, 0, 0, 0);
        return response;
    }

    private static CargaValidacionResponse RespuestaUsuarioSinPermiso(UsuarioCargaInfo usuarioCarga)
    {
        var response = new CargaValidacionResponse { CodigoReferencia = GenerarCodigoReferencia() };

        response.Errores.Add(new CargaValidacionError
        {
            Archivo = "general",
            Columna = "habilita_carga",
            Campo = "habilita_carga",
            Valor = usuarioCarga.HabilitaCarga.ToString(),
            Codigo = "FEDERAL_GENERAL_USUARIO_SIN_PERMISO_CARGA",
            DescripcionResumen = "Usuario sin permiso de carga federal",
            Mensaje = "El usuario autenticado no tiene habilitada la carga de información federal."
        });

        FinalizarRespuesta(response, 0, 0, 0);
        return response;
    }

    private void ValidarArchivoBase(IFormFile archivo, List<CargaValidacionError> errores)
    {
        if (archivo.Length == 0)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = archivo.FileName,
                Codigo = "FEDERAL_GENERAL_ARCHIVO_VACIO",
                DescripcionResumen = "Archivo vacío",
                Mensaje = $"El archivo \"{archivo.FileName}\" está vacío."
            });
        }

        if (archivo.Length > TamanioMaximoBytes)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = archivo.FileName,
                Codigo = "FEDERAL_GENERAL_ARCHIVO_EXCEDE_TAMANIO",
                DescripcionResumen = "Archivo excede tamaño máximo",
                Mensaje = $"El archivo \"{archivo.FileName}\" excede el tamaño máximo permitido de 50 MB."
            });
        }

        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();

        if (!_extensionesPermitidas.Contains(extension))
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = archivo.FileName,
                Codigo = "FEDERAL_GENERAL_EXTENSION_NO_PERMITIDA",
                DescripcionResumen = "Extensión no permitida",
                Mensaje = $"El archivo \"{archivo.FileName}\" tiene una extensión no permitida. Solo se permiten .csv y .xlsx."
            });
        }
    }

    private static IFormFile? BuscarArchivoPorNombre(List<IFormFile> archivos, string palabraEsperada)
    {
        var palabraNormalizada = NormalizarTexto(palabraEsperada);

        return archivos.FirstOrDefault(archivo => NormalizarTexto(Path.GetFileNameWithoutExtension(archivo.FileName)).Contains(palabraNormalizada));
    }

    private static void ValidarArchivoEsperado(IFormFile? archivo, string tipoArchivo, string palabraEsperada, List<CargaValidacionError> errores)
    {
        if (archivo != null) return;

        errores.Add(new CargaValidacionError
        {
            Archivo = tipoArchivo,
            Codigo = $"FEDERAL_GENERAL_FALTA_ARCHIVO_{tipoArchivo.ToUpperInvariant()}",
            DescripcionResumen = $"Falta archivo de {tipoArchivo}",
            Mensaje = $"Debe enviar un archivo cuyo nombre contenga la palabra \"{palabraEsperada}\"."
        });
    }

    private static void ValidarDuplicadosPorTipo(List<IFormFile> archivos, string palabraEsperada, string tipoArchivo, List<CargaValidacionError> errores)
    {
        var palabraNormalizada = NormalizarTexto(palabraEsperada);
        var coincidencias = archivos.Count(archivo => NormalizarTexto(Path.GetFileNameWithoutExtension(archivo.FileName)).Contains(palabraNormalizada));

        if (coincidencias <= 1) return;

        errores.Add(new CargaValidacionError
        {
            Archivo = tipoArchivo,
            Codigo = $"FEDERAL_GENERAL_ARCHIVO_DUPLICADO_{tipoArchivo.ToUpperInvariant()}",
            DescripcionResumen = $"Archivo duplicado de {tipoArchivo}",
            Mensaje = $"Se recibió más de un archivo para {tipoArchivo}. Solo debe enviarse uno."
        });
    }

    private async Task<List<CargaValidacionError>> ValidarEstructuraArchivosAsync(IFormFile archivoCarpetas, IFormFile archivoDelitos, IFormFile archivoVictimas)
    {
        var errores = new List<CargaValidacionError>();

        await ValidarColumnasObligatoriasArchivoAsync(archivoCarpetas, "carpetas", _carpetasValidator.ColumnasObligatorias, errores);
        await ValidarColumnasObligatoriasArchivoAsync(archivoDelitos, "delitos", _delitosValidator.ColumnasObligatorias, errores);
        await ValidarColumnasObligatoriasArchivoAsync(archivoVictimas, "victimas", _victimasValidator.ColumnasObligatorias, errores);

        return errores;
    }

    private async Task ValidarColumnasObligatoriasArchivoAsync(IFormFile archivo, string nombreArchivo, IReadOnlyCollection<string> columnasObligatorias, List<CargaValidacionError> errores)
    {
        var encabezados = await _archivoReader.LeerEncabezadosAsync(archivo);
        var columnasArchivo = encabezados.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var columna in columnasObligatorias)
        {
            if (columnasArchivo.Contains(columna)) continue;

            errores.Add(new CargaValidacionError
            {
                Archivo = nombreArchivo,
                Fila = 1,
                Columna = columna,
                Campo = columna,
                Codigo = $"FEDERAL_{nombreArchivo.ToUpperInvariant()}_COLUMNA_OBLIGATORIA_NO_ENCONTRADA",
                DescripcionResumen = "Columna obligatoria no encontrada",
                Mensaje = $"El archivo federal de {nombreArchivo} no contiene la columna obligatoria \"{columna}\"."
            });
        }
    }

    private static void FinalizarRespuesta(CargaValidacionResponse response, int totalCarpetas, int totalDelitos, int totalVictimas)
    {
        response.ResumenValidacion = ConstruirResumenValidacion(response.Errores.Concat(response.Advertencias).ToList(), totalCarpetas, totalDelitos, totalVictimas);

        response.Mensaje = response.EsValido
            ? response.Advertencias.Count > 0
                ? "La información federal fue validada con advertencias. Revise las advertencias antes de continuar."
                : "La información federal fue validada correctamente. Puede continuar con el acuse previo."
            : "La información federal contiene errores de validación.";
    }

    private static List<CargaValidacionResumenItem> ConstruirResumenValidacion(List<CargaValidacionError> errores, int totalCarpetas, int totalDelitos, int totalVictimas)
    {
        var resumen = new List<CargaValidacionResumenItem>
        {
            new() { Archivo = "carpetas", Codigo = "CARPETAS_TOTAL_REGISTROS", Descripcion = "Total de registros en el archivo de carpetas", TotalRegistros = totalCarpetas, EsError = false },
            new() { Archivo = "delitos", Codigo = "DELITOS_TOTAL_REGISTROS", Descripcion = "Total de registros en el archivo de delitos", TotalRegistros = totalDelitos, EsError = false },
            new() { Archivo = "victimas", Codigo = "VICTIMAS_TOTAL_REGISTROS", Descripcion = "Total de registros en el archivo de víctimas", TotalRegistros = totalVictimas, EsError = false }
        };

        resumen.AddRange(errores
            .Where(x => !string.IsNullOrWhiteSpace(x.Codigo) && !string.IsNullOrWhiteSpace(x.DescripcionResumen))
            .GroupBy(x => new { x.Archivo, x.Codigo, x.DescripcionResumen })
            .Select(x => new CargaValidacionResumenItem
            {
                Archivo = x.Key.Archivo,
                Codigo = x.Key.Codigo,
                Descripcion = x.Key.DescripcionResumen,
                TotalRegistros = x.Count(),
                EsError = true
            })
            .OrderBy(x => x.Archivo)
            .ThenBy(x => x.Codigo));

        return resumen;
    }

    private static int ObtenerMesCorteDesdeCarpetas(List<ArchivoFila> filasCarpetas)
    {
        var fecha = ObtenerPrimeraFechaInicioValida(filasCarpetas);
        return fecha?.Month ?? DateTime.Today.AddMonths(-1).Month;
    }

    private static int ObtenerAnioCorteDesdeCarpetas(List<ArchivoFila> filasCarpetas)
    {
        var fecha = ObtenerPrimeraFechaInicioValida(filasCarpetas);
        return fecha?.Year ?? DateTime.Today.AddMonths(-1).Year;
    }

    private static DateTime? ObtenerPrimeraFechaInicioValida(List<ArchivoFila> filasCarpetas)
    {
        foreach (var fila in filasCarpetas)
        {
            fila.Columnas.TryGetValue("fha_de_ini", out var valor);

            if (string.IsNullOrWhiteSpace(valor)) continue;
            if (DateTime.TryParse(valor, new CultureInfo("es-MX"), DateTimeStyles.None, out var fecha)) return fecha.Date;
            if (DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaInvariante)) return fechaInvariante.Date;
        }

        return null;
    }

    private static string GenerarCodigoReferencia() => Guid.NewGuid().ToString("N")[..13].ToLowerInvariant();

    private static string NormalizarTexto(string texto)
    {
        var textoNormalizado = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var caracteres = textoNormalizado.Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark).ToArray();
        return new string(caracteres).Normalize(NormalizationForm.FormC);
    }
}
