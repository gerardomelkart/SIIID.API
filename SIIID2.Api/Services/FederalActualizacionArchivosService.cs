using System.Globalization;
using System.Text;
using SIIID2.Api.Models;
using SIIID2.Api.Readers;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;

public class FederalActualizacionArchivosService : IFederalActualizacionArchivosService
{
    private readonly IArchivoReader _archivoReader;
    private readonly CarpetasValidator _carpetasValidator;
    private readonly DelitosValidator _delitosValidator;
    private readonly VictimasValidator _victimasValidator;
    private readonly CargaIntegridadValidator _cargaIntegridadValidator;
    private readonly CatalogosValidator _catalogosValidator;
    private readonly IFederalCargaRepository _federalCargaRepository;
    private readonly IFederalActualizacionRepository _federalActualizacionRepository;
    private readonly IFederalArchivosOriginalesService _archivosOriginalesService;

    private readonly string[] _extensionesPermitidas = [".csv", ".xlsx"];
    private const long TamanioMaximoBytes = 50 * 1024 * 1024;

    public FederalActualizacionArchivosService(IArchivoReader archivoReader, CarpetasValidator carpetasValidator, DelitosValidator delitosValidator, VictimasValidator victimasValidator, CargaIntegridadValidator cargaIntegridadValidator, CatalogosValidator catalogosValidator, IFederalCargaRepository federalCargaRepository, IFederalActualizacionRepository federalActualizacionRepository, IFederalArchivosOriginalesService archivosOriginalesService)
    {
        _archivoReader = archivoReader;
        _carpetasValidator = carpetasValidator;
        _delitosValidator = delitosValidator;
        _victimasValidator = victimasValidator;
        _cargaIntegridadValidator = cargaIntegridadValidator;
        _catalogosValidator = catalogosValidator;
        _federalCargaRepository = federalCargaRepository;
        _federalActualizacionRepository = federalActualizacionRepository;
        _archivosOriginalesService = archivosOriginalesService;
    }

    public async Task<CargaValidacionResponse> ValidarActualizacionAsync(IFormCollection form, int idUsuarioCarga)
    {
        var usuarioCarga = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioCarga);

        if (usuarioCarga == null) return RespuestaUsuarioInvalido(idUsuarioCarga);
        if (!usuarioCarga.HabilitaModificacion) return RespuestaUsuarioSinPermiso(usuarioCarga);

        var response = new CargaValidacionResponse { CodigoReferencia = GenerarCodigoReferencia() };
        var archivos = form.Files;
        var periodo = ObtenerPeriodoCorte(form, response.Errores);
        var mesCorte = periodo.Mes;
        var anioCorte = periodo.Anio;

        if (archivos == null || archivos.Count == 0)
        {
            response.Errores.Add(new CargaValidacionError
            {
                Archivo = "general",
                Codigo = "FEDERAL_GENERAL_SIN_ARCHIVOS",
                DescripcionResumen = "No se recibieron archivos",
                Mensaje = "Debe enviar los archivos federales actualizados de carpetas, delitos y víctimas."
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

        response.Errores.AddRange(_carpetasValidator.Validar(filasCarpetas, validarMesInmediatoAnterior: false));
        response.Errores.AddRange(_delitosValidator.Validar(filasDelitos));
        response.Errores.AddRange(_victimasValidator.Validar(filasVictimas));

        if (mesCorte.HasValue && anioCorte.HasValue) response.Errores.AddRange(ValidarPeriodoCarpetas(filasCarpetas, mesCorte.Value, anioCorte.Value));

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

        if (mesCorte.HasValue && anioCorte.HasValue)
        {
            if (!await _federalCargaRepository.ExisteCargaConfirmadaAsync(mesCorte.Value, anioCorte.Value))
            {
                response.Errores.Add(new CargaValidacionError
                {
                    Archivo = "general",
                    Valor = $"{mesCorte:00}/{anioCorte}",
                    Codigo = "FEDERAL_ACTUALIZACION_SIN_CARGA_CONFIRMADA",
                    DescripcionResumen = "No existe carga federal confirmada",
                    Mensaje = $"No existe información federal confirmada para el periodo {mesCorte:00}/{anioCorte}. Primero debe existir una carga inicial confirmada."
                });
            }
            else
            {
                var cargaPendiente = await _federalActualizacionRepository.ObtenerPendienteAsync(mesCorte.Value, anioCorte.Value);

                if (cargaPendiente != null)
                {
                    var enRevisionAdministrativa = string.Equals(cargaPendiente.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase);

                    response.Errores.Add(new CargaValidacionError
                    {
                        Archivo = "general",
                        Valor = cargaPendiente.CodigoReferencia,
                        Codigo = enRevisionAdministrativa ? "FEDERAL_ACTUALIZACION_PENDIENTE_APROBACION" : "FEDERAL_ACTUALIZACION_PENDIENTE_EXISTENTE",
                        DescripcionResumen = enRevisionAdministrativa ? "Actualización federal en revisión administrativa" : "Ya existe actualización federal pendiente",
                        Mensaje = enRevisionAdministrativa
                            ? $"Ya existe una actualización federal en revisión administrativa para el periodo {mesCorte:00}/{anioCorte}. Código de referencia pendiente: {cargaPendiente.CodigoReferencia}."
                            : $"Ya existe una actualización federal validada pendiente de confirmar para el periodo {mesCorte:00}/{anioCorte}. Código de referencia pendiente: {cargaPendiente.CodigoReferencia}."
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

        if (!mesCorte.HasValue || !anioCorte.HasValue || response.Errores.Any(x => x.Codigo is "FEDERAL_ACTUALIZACION_SIN_CARGA_CONFIRMADA" or "FEDERAL_ACTUALIZACION_PENDIENTE_EXISTENTE" or "FEDERAL_ACTUALIZACION_PENDIENTE_APROBACION")) return response;

        var estadoCarga = response.EsValido ? "VALIDADO_PENDIENTE_ACTUALIZACION" : "RECHAZADO_VALIDACION_ACTUALIZACION";
        var mensajeError = response.EsValido ? null : $"La actualización federal contiene errores de validación. Total de errores: {response.Errores.Count}.";

        var idFederalCarga = await _federalActualizacionRepository.GuardarIntentoAsync(idUsuarioCarga, response.CodigoReferencia, mesCorte.Value, anioCorte.Value, filasCarpetas.Count, filasDelitos.Count, filasVictimas.Count, estadoCarga, mensajeError, response.Advertencias, filasCarpetas, filasDelitos, filasVictimas);
        await _archivosOriginalesService.GuardarAsync(idUsuarioCarga, response.CodigoReferencia, "ACTUALIZACION", mesCorte.Value, anioCorte.Value, archivoCarpetas!, archivoDelitos!, archivoVictimas!);

        if (response.EsValido)
        {
            response.ResumenValidacion.AddRange(await _federalActualizacionRepository.ObtenerResumenDiferenciasAsync(idFederalCarga));
            response.Mensaje = "Actualización federal validada correctamente. Revise las diferencias antes de continuar.";
        }

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
            Columna = "habilita_modificacion",
            Campo = "habilita_modificacion",
            Valor = usuarioCarga.HabilitaModificacion.ToString(),
            Codigo = "FEDERAL_GENERAL_USUARIO_SIN_PERMISO_MODIFICACION",
            DescripcionResumen = "Usuario sin permiso de actualización federal",
            Mensaje = "El usuario autenticado no tiene habilitada la actualización de información federal."
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

    private static (int? Mes, int? Anio) ObtenerPeriodoCorte(IFormCollection form, List<CargaValidacionError> errores)
    {
        int? mes = null;
        int? anio = null;
        var valorMes = form.TryGetValue("mesCorte", out var meses) ? meses.FirstOrDefault() : null;
        var valorAnio = form.TryGetValue("anioCorte", out var anios) ? anios.FirstOrDefault() : null;

        if (!int.TryParse(valorMes, out var mesConvertido) || mesConvertido is < 1 or > 12)
        {
            errores.Add(new CargaValidacionError { Archivo = "general", Columna = "mesCorte", Campo = "mesCorte", Valor = valorMes, Codigo = "FEDERAL_ACTUALIZACION_MES_CORTE_INVALIDO", DescripcionResumen = "Mes de corte inválido", Mensaje = "Debe seleccionar un mes de corte válido entre 1 y 12." });
        }
        else mes = mesConvertido;

        if (!int.TryParse(valorAnio, out var anioConvertido) || anioConvertido is < 2000 or > 2100)
        {
            errores.Add(new CargaValidacionError { Archivo = "general", Columna = "anioCorte", Campo = "anioCorte", Valor = valorAnio, Codigo = "FEDERAL_ACTUALIZACION_ANIO_CORTE_INVALIDO", DescripcionResumen = "Año de corte inválido", Mensaje = "Debe seleccionar un año de corte válido entre 2000 y 2100." });
        }
        else anio = anioConvertido;

        return (mes, anio);
    }

    private static List<CargaValidacionError> ValidarPeriodoCarpetas(List<ArchivoFila> filasCarpetas, int mesCorte, int anioCorte)
    {
        var errores = new List<CargaValidacionError>();

        foreach (var fila in filasCarpetas)
        {
            fila.Columnas.TryGetValue("fha_de_ini", out var valor);
            if (string.IsNullOrWhiteSpace(valor)) continue;

            var fechaValida = DateTime.TryParse(valor, new CultureInfo("es-MX"), DateTimeStyles.None, out var fecha) || DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);
            if (!fechaValida || fecha.Month == mesCorte && fecha.Year == anioCorte) continue;

            errores.Add(new CargaValidacionError { Archivo = "carpetas", Fila = fila.NumeroFila, Columna = "fha_de_ini", Campo = "fha_de_ini", Valor = valor, Codigo = "FEDERAL_ACTUALIZACION_PERIODO_NO_CORRESPONDE", DescripcionResumen = "Fecha fuera del periodo seleccionado", Mensaje = $"La fecha de inicio debe pertenecer al periodo {mesCorte:00}/{anioCorte}." });
        }

        return errores;
    }

    public async Task<ActualizacionDiferenciasResponse> ObtenerDiferenciasAsync(string codigoReferencia, int idUsuarioConsulta, int limitePorSeccion, bool incluirResumen = true)
    {
        var usuario = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);
        if (usuario == null) return RespuestaDiferenciasInvalida(codigoReferencia, "El usuario no tiene acceso activo al módulo Federal.");

        var resultado = await _federalActualizacionRepository.ObtenerDiferenciasAsync(codigoReferencia, idUsuarioConsulta, limitePorSeccion);
        if (resultado == null) return RespuestaDiferenciasInvalida(codigoReferencia, "No se encontró una actualización federal pendiente válida para el código indicado.");

        if (incluirResumen) AplicarResumen(resultado, await _federalActualizacionRepository.ObtenerResumenDiferenciasAsync(resultado.IdCarga));
        return resultado;
    }

    public Task<ConfirmarCargaResponse> ConfirmarActualizacionAsync(ConfirmarCargaRequest request, int idUsuarioConfirmacion)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CodigoReferencia)) return Task.FromResult(new ConfirmarCargaResponse { EsValido = false, Estado = "SOLICITUD_INVALIDA", Mensaje = "Debe enviar el código de referencia de la actualización federal." });
        return _federalActualizacionRepository.ConfirmarAsync(request.CodigoReferencia.Trim(), request.Aceptar, idUsuarioConfirmacion);
    }

    public async Task<ActualizacionPeriodoResponse> ConsultarPeriodoAsync(int mesCorte, int anioCorte, int idUsuarioConsulta)
    {
        var usuario = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);
        if (usuario == null) return RespuestaPeriodo(false, false, mesCorte, anioCorte, "El usuario no tiene acceso activo al módulo Federal.");
        if (!usuario.HabilitaModificacion) return RespuestaPeriodo(false, false, mesCorte, anioCorte, "El usuario no tiene habilitada la actualización de información federal.");
        if (mesCorte is < 1 or > 12 || anioCorte is < 2000 or > 2100) return RespuestaPeriodo(false, false, mesCorte, anioCorte, "El periodo seleccionado no es válido.");

        var existeConfirmada = await _federalCargaRepository.ExisteCargaConfirmadaAsync(mesCorte, anioCorte);
        if (!existeConfirmada) return RespuestaPeriodo(true, false, mesCorte, anioCorte, $"No existe una carga federal confirmada para el periodo {mesCorte:00}/{anioCorte}.");

        var pendiente = await _federalActualizacionRepository.ObtenerPendienteAsync(mesCorte, anioCorte);
        if (pendiente == null) return RespuestaPeriodo(true, true, mesCorte, anioCorte, $"Existe información federal confirmada para el periodo {mesCorte:00}/{anioCorte}. Puede continuar con la actualización.", tieneCargaConfirmada: true);

        var revision = string.Equals(pendiente.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase);
        return new ActualizacionPeriodoResponse { EsValido = true, PuedeActualizar = false, TieneCargaConfirmada = true, ExisteActualizacionPendiente = true, CodigoActualizacionPendiente = pendiente.CodigoReferencia, EstadoActualizacionPendiente = pendiente.Estado, MesCorte = mesCorte, AnioCorte = anioCorte, Mensaje = revision ? $"La actualización federal del periodo {mesCorte:00}/{anioCorte} se encuentra en revisión administrativa." : $"Ya existe una actualización federal pendiente para el periodo {mesCorte:00}/{anioCorte}." };
    }

    public async Task<List<ActualizacionAnioDisponibleItem>> ObtenerPeriodosDisponiblesAsync(int idUsuarioConsulta)
    {
        var usuario = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);
        return usuario == null || !usuario.HabilitaModificacion ? [] : await _federalActualizacionRepository.ObtenerPeriodosDisponiblesAsync();
    }

    private static ActualizacionPeriodoResponse RespuestaPeriodo(bool esValido, bool puedeActualizar, int mesCorte, int anioCorte, string mensaje, bool tieneCargaConfirmada = false) => new() { EsValido = esValido, PuedeActualizar = puedeActualizar, TieneCargaConfirmada = tieneCargaConfirmada, ExisteActualizacionPendiente = false, MesCorte = mesCorte, AnioCorte = anioCorte, Mensaje = mensaje };

    private static ActualizacionDiferenciasResponse RespuestaDiferenciasInvalida(string codigoReferencia, string mensaje) => new() { EsValido = false, CodigoReferencia = codigoReferencia, Mensaje = mensaje };

    private static void AplicarResumen(ActualizacionDiferenciasResponse response, IEnumerable<CargaValidacionResumenItem> resumen)
    {
        foreach (var item in resumen)
        {
            var destino = item.Archivo.ToLowerInvariant() switch { "carpetas" => response.ResumenCarpetas, "delitos" => response.ResumenDelitos, "victimas" => response.ResumenVictimas, _ => null };
            if (destino == null) continue;
            if (item.Codigo.EndsWith("_NUEVO", StringComparison.OrdinalIgnoreCase)) destino.Nuevos += item.TotalRegistros;
            else if (item.Codigo.EndsWith("_MODIFICADO", StringComparison.OrdinalIgnoreCase)) destino.Modificados += item.TotalRegistros;
            else if (item.Codigo.EndsWith("_ELIMINADO", StringComparison.OrdinalIgnoreCase)) destino.Eliminados += item.TotalRegistros;
        }

        response.ResumenTotal = new ActualizacionDiferenciasResumen { Nuevos = response.ResumenCarpetas.Nuevos + response.ResumenDelitos.Nuevos + response.ResumenVictimas.Nuevos, Modificados = response.ResumenCarpetas.Modificados + response.ResumenDelitos.Modificados + response.ResumenVictimas.Modificados, Eliminados = response.ResumenCarpetas.Eliminados + response.ResumenDelitos.Eliminados + response.ResumenVictimas.Eliminados };
        response.TotalCarpetas = response.ResumenCarpetas.Nuevos + response.ResumenCarpetas.Modificados + response.ResumenCarpetas.Eliminados;
        response.TotalDelitos = response.ResumenDelitos.Nuevos + response.ResumenDelitos.Modificados + response.ResumenDelitos.Eliminados;
        response.TotalVictimas = response.ResumenVictimas.Nuevos + response.ResumenVictimas.Modificados + response.ResumenVictimas.Eliminados;
        response.TotalDiferencias = response.TotalCarpetas + response.TotalDelitos + response.TotalVictimas;
        response.DetalleLimitado = response.Carpetas.Count < response.TotalCarpetas || response.Delitos.Count < response.TotalDelitos || response.Victimas.Count < response.TotalVictimas;
    }

    private static string GenerarCodigoReferencia() => Guid.NewGuid().ToString("N")[..13].ToLowerInvariant();

    private static string NormalizarTexto(string texto)
    {
        var textoNormalizado = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var caracteres = textoNormalizado.Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark).ToArray();
        return new string(caracteres).Normalize(NormalizationForm.FormC);
    }
}
