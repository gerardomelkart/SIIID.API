using System.Globalization;
using SIIID2.Api.Models;
using SIIID2.Api.Readers;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;

public class SemanalCargaService : ISemanalCargaService
{
    private const long TamanioMaximoBytes = 50 * 1024 * 1024;
    private const string CodigoExclusionFueraTramo = "FUERA_TRAMO_SEMANAL";
    private static readonly HashSet<string> TiposContenidoPermitidos = new(StringComparer.OrdinalIgnoreCase) { "SOLO_SEMANA", "ACUMULADO_MES" };
    private static readonly HashSet<string> ExtensionesPermitidas = new(StringComparer.OrdinalIgnoreCase) { ".csv", ".xlsx" };

    private readonly IArchivoReader _archivoReader;
    private readonly CarpetasValidator _carpetasValidator;
    private readonly DelitosValidator _delitosValidator;
    private readonly VictimasValidator _victimasValidator;
    private readonly CargaIntegridadValidator _cargaIntegridadValidator;
    private readonly CatalogosValidator _catalogosValidator;
    private readonly ISemanalDelitoRepository _semanalDelitoRepository;
    private readonly ISemanalCargaRepository _semanalCargaRepository;
    private readonly ILogger<SemanalCargaService> _logger;

    public SemanalCargaService(IArchivoReader archivoReader, CarpetasValidator carpetasValidator, DelitosValidator delitosValidator, VictimasValidator victimasValidator, CargaIntegridadValidator cargaIntegridadValidator, CatalogosValidator catalogosValidator, ISemanalDelitoRepository semanalDelitoRepository, ISemanalCargaRepository semanalCargaRepository, ILogger<SemanalCargaService> logger)
    {
        _archivoReader = archivoReader;
        _carpetasValidator = carpetasValidator;
        _delitosValidator = delitosValidator;
        _victimasValidator = victimasValidator;
        _cargaIntegridadValidator = cargaIntegridadValidator;
        _catalogosValidator = catalogosValidator;
        _semanalDelitoRepository = semanalDelitoRepository;
        _semanalCargaRepository = semanalCargaRepository;
        _logger = logger;
    }

    public async Task<SemanalCargaValidacionResponse> ValidarArchivosAsync(SemanalCargaValidacionRequest request, int idUsuarioCarga)
    {
        var response = new SemanalCargaValidacionResponse { CodigoReferencia = GenerarCodigoReferencia() };
        var usuarioCarga = await _semanalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioCarga);

        if (usuarioCarga == null)
        {
            AgregarErrorGeneral(response, "SEMANAL_USUARIO_SIN_ACCESO", "Usuario sin acceso semanal", "El usuario no existe, está inactivo o no tiene habilitado el módulo semanal.");
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        if (!usuarioCarga.HabilitaCarga)
        {
            AgregarErrorGeneral(response, "SEMANAL_USUARIO_SIN_PERMISO_CARGA", "Usuario sin permiso de carga semanal", "El usuario no tiene habilitada la carga de información en el módulo semanal.");
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        response.Periodo = ValidarPeriodo(request, response.Errores);
        ValidarArchivoBase(request.Carpetas, "carpetas", response.Errores);
        ValidarArchivoBase(request.Delitos, "delitos", response.Errores);
        ValidarArchivoBase(request.Victimas, "victimas", response.Errores);

        if (response.Errores.Count > 0)
        {
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        var erroresEstructura = await ValidarEstructuraArchivosAsync(request.Carpetas!, request.Delitos!, request.Victimas!);

        if (erroresEstructura.Count > 0)
        {
            response.Errores.AddRange(erroresEstructura);
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        var periodo = response.Periodo!;
        var filasCarpetas = await _archivoReader.LeerAsync(request.Carpetas!);
        var filasDelitos = await _archivoReader.LeerAsync(request.Delitos!);
        var filasVictimas = await _archivoReader.LeerAsync(request.Victimas!);

        response.Errores.AddRange(_carpetasValidator.Validar(filasCarpetas, validarMesInmediatoAnterior: false));
        response.Errores.AddRange(_delitosValidator.Validar(filasDelitos));
        response.Errores.AddRange(_victimasValidator.Validar(filasVictimas));

        if (response.Errores.Count > 0)
        {
            FinalizarRespuesta(response, filasCarpetas.Count, filasDelitos.Count, filasVictimas.Count);
            return response;
        }

        var carpetasEtiquetadas = EtiquetarCarpetas(filasCarpetas, periodo);
        var idsCarpetasExcluidas = carpetasEtiquetadas.Where(x => !x.Incluido).Select(x => ObtenerValor(x.Fila, "id_ci")?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var delitosEtiquetados = EtiquetarPorCarpeta(filasDelitos, idsCarpetasExcluidas);
        var victimasEtiquetadas = EtiquetarPorCarpeta(filasVictimas, idsCarpetasExcluidas);
        var carpetasIncluidas = carpetasEtiquetadas.Where(x => x.Incluido).Select(x => x.Fila).ToList();
        var delitosIncluidos = delitosEtiquetados.Where(x => x.Incluido).Select(x => x.Fila).ToList();
        var victimasIncluidas = victimasEtiquetadas.Where(x => x.Incluido).Select(x => x.Fila).ToList();

        ActualizarTotales(response, carpetasEtiquetadas, delitosEtiquetados, victimasEtiquetadas);

        if (carpetasIncluidas.Count == 0)
        {
            AgregarErrorGeneral(response, "SEMANAL_SIN_CARPETAS_EN_TRAMO", "Sin carpetas en el tramo semanal", $"No existen carpetas cuya fecha de inicio esté entre {periodo.FechaInicioTramo:dd/MM/yyyy} y {periodo.FechaFinTramo:dd/MM/yyyy}.");
            FinalizarRespuesta(response, filasCarpetas.Count, filasDelitos.Count, filasVictimas.Count);
            return response;
        }

        var erroresIntegridad = _cargaIntegridadValidator.Validar(carpetasIncluidas, delitosIncluidos, victimasIncluidas);

        if (usuarioCarga.EsSuperUsuario)
        {
            response.Advertencias.AddRange(erroresIntegridad.Where(x => x.Codigo == "INTEGRIDAD_FECHA_HECHOS_MAYOR_FECHA_INICIO"));
            erroresIntegridad = erroresIntegridad.Where(x => x.Codigo != "INTEGRIDAD_FECHA_HECHOS_MAYOR_FECHA_INICIO").ToList();
        }

        response.Errores.AddRange(erroresIntegridad);
        response.Errores.AddRange(await _catalogosValidator.ValidarAsync(carpetasIncluidas, delitosIncluidos, victimasIncluidas));
        ValidarLongitudIdentificadorDelito(delitosIncluidos, response.Errores);

        var configuracion = await _semanalDelitoRepository.ObtenerConfiguracionAsync();
        var modalidadesConfiguradas = configuracion.Where(x => x.Seleccionado).OrderBy(x => x.Orden).ThenBy(x => x.ClaveModalidad).ToList();

        if (modalidadesConfiguradas.Count == 0)
        {
            AgregarErrorGeneral(response, "SEMANAL_SIN_MODALIDADES_CONFIGURADAS", "Sin modalidades semanales configuradas", "No existen modalidades activas habilitadas para la carga semanal.");
        }
        else
        {
            ValidarModalidadesConfiguradas(filasDelitos, configuracion, response.Errores);
        }

        response.Errores.AddRange(ValidarEntidadUsuarioCarga(usuarioCarga, delitosIncluidos));
        var idEntidadFederativa = ObtenerEntidadFederativaCarga(usuarioCarga, delitosIncluidos, response.Errores);

        if (response.Errores.Count == 0 && idEntidadFederativa.HasValue)
        {
            var cargaExistente = await _semanalCargaRepository.ObtenerCargaActivaAsync(idEntidadFederativa.Value, periodo);

            if (cargaExistente != null)
            {
                response.Errores.Add(new CargaValidacionError
                {
                    Archivo = "general",
                    Codigo = "SEMANAL_CARGA_EXISTENTE",
                    DescripcionResumen = "Ya existe una carga para el tramo semanal",
                    Valor = cargaExistente.CodigoReferencia,
                    Mensaje = $"Ya existe una carga semanal en estado {cargaExistente.Estado} para la entidad y tramo seleccionados. Código de referencia: {cargaExistente.CodigoReferencia}."
                });
            }
        }

        if (response.Errores.Count == 0)
        {
            response.Advertencias.AddRange(_cargaIntegridadValidator.ValidarAdvertencias(delitosIncluidos, victimasIncluidas));
        }

        FinalizarRespuesta(response, filasCarpetas.Count, filasDelitos.Count, filasVictimas.Count);

        if (!response.EsValido || !idEntidadFederativa.HasValue) return response;

        await _semanalCargaRepository.GuardarIntentoCargaAsync(new SemanalCargaPersistencia
        {
            IdUsuarioCarga = idUsuarioCarga,
            IdEntidadFederativa = idEntidadFederativa.Value,
            CodigoReferencia = response.CodigoReferencia,
            Periodo = periodo,
            TotalCarpetasIncluidas = response.TotalCarpetasIncluidas,
            TotalDelitosIncluidos = response.TotalDelitosIncluidos,
            TotalVictimasIncluidas = response.TotalVictimasIncluidas,
            TotalCarpetasExcluidas = response.TotalCarpetasExcluidas,
            TotalDelitosExcluidos = response.TotalDelitosExcluidos,
            TotalVictimasExcluidas = response.TotalVictimasExcluidas,
            ModalidadesConfiguradas = modalidadesConfiguradas,
            Carpetas = carpetasEtiquetadas,
            Delitos = delitosEtiquetados,
            Victimas = victimasEtiquetadas
        });

        _logger.LogInformation("Carga semanal validada. Referencia: {CodigoReferencia}, Entidad: {IdEntidad}, Semana: {NumeroSemana}/{AnioSemana}, Tramo: {FechaInicioTramo:yyyy-MM-dd} a {FechaFinTramo:yyyy-MM-dd}", response.CodigoReferencia, idEntidadFederativa.Value, periodo.NumeroSemana, periodo.AnioSemana, periodo.FechaInicioTramo, periodo.FechaFinTramo);

        return response;
    }

    public Task<ConfirmarCargaResponse> ConfirmarCargaAsync(ConfirmarCargaRequest request, int idUsuarioConfirmacion) => _semanalCargaRepository.ConfirmarCargaAsync(request.CodigoReferencia.Trim(), request.Aceptar, idUsuarioConfirmacion);

    private static SemanalPeriodoCarga? ValidarPeriodo(SemanalCargaValidacionRequest request, List<CargaValidacionError> errores)
    {
        var tipoContenido = request.TipoContenido.Trim().ToUpperInvariant();

        if (!TiposContenidoPermitidos.Contains(tipoContenido))
        {
            AgregarErrorGeneral(errores, "SEMANAL_TIPO_CONTENIDO_INVALIDO", "Tipo de contenido inválido", "El tipo de contenido debe ser SOLO_SEMANA o ACUMULADO_MES.", "tipoContenido", request.TipoContenido);
        }

        if (request.AnioSemana < 2000 || request.AnioSemana > 2100) AgregarErrorGeneral(errores, "SEMANAL_ANIO_SEMANA_INVALIDO", "Año de semana inválido", "El año de la semana debe estar entre 2000 y 2100.", "anioSemana", request.AnioSemana.ToString(CultureInfo.InvariantCulture));
        if (request.NumeroSemana < 1 || request.NumeroSemana > 53) AgregarErrorGeneral(errores, "SEMANAL_NUMERO_SEMANA_INVALIDO", "Número de semana inválido", "El número de semana debe estar entre 1 y 53.", "numeroSemana", request.NumeroSemana.ToString(CultureInfo.InvariantCulture));
        if (request.MesCorte < 1 || request.MesCorte > 12) AgregarErrorGeneral(errores, "SEMANAL_MES_CORTE_INVALIDO", "Mes de corte inválido", "El mes de corte debe estar entre 1 y 12.", "mesCorte", request.MesCorte.ToString(CultureInfo.InvariantCulture));
        if (request.AnioCorte < 2000 || request.AnioCorte > 2100) AgregarErrorGeneral(errores, "SEMANAL_ANIO_CORTE_INVALIDO", "Año de corte inválido", "El año de corte debe estar entre 2000 y 2100.", "anioCorte", request.AnioCorte.ToString(CultureInfo.InvariantCulture));

        var fechaInicioSemana = request.FechaInicioSemana.Date;

        if (fechaInicioSemana < new DateTime(2000, 1, 1) || fechaInicioSemana > new DateTime(2100, 12, 25))
        {
            AgregarErrorGeneral(errores, "SEMANAL_FECHA_INICIO_INVALIDA", "Fecha de inicio semanal inválida", "La fecha de inicio de la semana debe estar entre 01/01/2000 y 25/12/2100.", "fechaInicioSemana", request.FechaInicioSemana.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (errores.Count > 0) return null;

        var fechaFinSemana = fechaInicioSemana.AddDays(6);
        var fechaInicioMes = new DateTime(request.AnioCorte, request.MesCorte, 1);
        var fechaFinMes = fechaInicioMes.AddMonths(1).AddDays(-1);
        var fechaInicioTramo = fechaInicioSemana > fechaInicioMes ? fechaInicioSemana : fechaInicioMes;
        var fechaFinTramo = fechaFinSemana < fechaFinMes ? fechaFinSemana : fechaFinMes;

        if (fechaInicioTramo > fechaFinTramo)
        {
            AgregarErrorGeneral(errores, "SEMANAL_CORTE_FUERA_DE_SEMANA", "El corte no coincide con la semana", "El mes y año de corte seleccionados no intersectan con las fechas de la semana.", "mesCorte", $"{request.MesCorte:00}/{request.AnioCorte}");
            return null;
        }

        return new SemanalPeriodoCarga
        {
            TipoContenido = tipoContenido,
            AnioSemana = request.AnioSemana,
            NumeroSemana = request.NumeroSemana,
            FechaInicioSemana = fechaInicioSemana,
            FechaFinSemana = fechaFinSemana,
            FechaInicioTramo = fechaInicioTramo,
            FechaFinTramo = fechaFinTramo,
            MesCorte = request.MesCorte,
            AnioCorte = request.AnioCorte
        };
    }

    private static void ValidarArchivoBase(IFormFile? archivo, string tipoArchivo, List<CargaValidacionError> errores)
    {
        if (archivo == null)
        {
            errores.Add(new CargaValidacionError { Archivo = tipoArchivo, Codigo = $"SEMANAL_FALTA_ARCHIVO_{tipoArchivo.ToUpperInvariant()}", DescripcionResumen = $"Falta archivo de {tipoArchivo}", Mensaje = $"Debe seleccionar el archivo de {tipoArchivo}." });
            return;
        }

        if (archivo.Length == 0) errores.Add(new CargaValidacionError { Archivo = tipoArchivo, Codigo = "SEMANAL_ARCHIVO_VACIO", DescripcionResumen = "Archivo vacío", Mensaje = $"El archivo de {tipoArchivo} está vacío." });
        if (archivo.Length > TamanioMaximoBytes) errores.Add(new CargaValidacionError { Archivo = tipoArchivo, Codigo = "SEMANAL_ARCHIVO_EXCEDE_TAMANIO", DescripcionResumen = "Archivo excede tamaño máximo", Mensaje = $"El archivo de {tipoArchivo} excede el tamaño máximo permitido de 50 MB." });

        var extension = Path.GetExtension(archivo.FileName);

        if (!ExtensionesPermitidas.Contains(extension)) errores.Add(new CargaValidacionError { Archivo = tipoArchivo, Codigo = "SEMANAL_EXTENSION_NO_PERMITIDA", DescripcionResumen = "Extensión no permitida", Valor = extension, Mensaje = $"El archivo de {tipoArchivo} debe ser CSV o XLSX." });
    }

    private async Task<List<CargaValidacionError>> ValidarEstructuraArchivosAsync(IFormFile carpetas, IFormFile delitos, IFormFile victimas)
    {
        var errores = new List<CargaValidacionError>();
        await ValidarColumnasObligatoriasArchivoAsync(carpetas, "carpetas", _carpetasValidator.ColumnasObligatorias, errores);
        await ValidarColumnasObligatoriasArchivoAsync(delitos, "delitos", _delitosValidator.ColumnasObligatorias, errores);
        await ValidarColumnasObligatoriasArchivoAsync(victimas, "victimas", _victimasValidator.ColumnasObligatorias, errores);
        return errores;
    }

    private async Task ValidarColumnasObligatoriasArchivoAsync(IFormFile archivo, string nombreArchivo, IReadOnlyCollection<string> columnasObligatorias, List<CargaValidacionError> errores)
    {
        var encabezados = await _archivoReader.LeerEncabezadosAsync(archivo);
        var columnasArchivo = encabezados.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var columna in columnasObligatorias.Where(columna => !columnasArchivo.Contains(columna)))
        {
            errores.Add(new CargaValidacionError { Archivo = nombreArchivo, Fila = 1, Columna = columna, Campo = columna, Codigo = $"{nombreArchivo.ToUpperInvariant()}_COLUMNA_OBLIGATORIA_NO_ENCONTRADA", DescripcionResumen = "Columna obligatoria no encontrada", Mensaje = $"El archivo de {nombreArchivo} no contiene la columna obligatoria \"{columna}\"." });
        }
    }

    private static List<SemanalArchivoFilaCarga> EtiquetarCarpetas(List<ArchivoFila> filas, SemanalPeriodoCarga periodo)
    {
        return filas.Select(fila =>
        {
            var incluido = IntentarConvertirFecha(ObtenerValor(fila, "fha_de_ini"), out var fechaInicio) && fechaInicio.Date >= periodo.FechaInicioTramo && fechaInicio.Date <= periodo.FechaFinTramo;
            return new SemanalArchivoFilaCarga { Fila = fila, Incluido = incluido, CodigoExclusion = incluido ? null : CodigoExclusionFueraTramo };
        }).ToList();
    }

    private static List<SemanalArchivoFilaCarga> EtiquetarPorCarpeta(List<ArchivoFila> filas, HashSet<string> idsCarpetasExcluidas)
    {
        return filas.Select(fila =>
        {
            var idCi = ObtenerValor(fila, "id_ci")?.Trim();
            var incluido = string.IsNullOrWhiteSpace(idCi) || !idsCarpetasExcluidas.Contains(idCi);
            return new SemanalArchivoFilaCarga { Fila = fila, Incluido = incluido, CodigoExclusion = incluido ? null : CodigoExclusionFueraTramo };
        }).ToList();
    }

    private static void ActualizarTotales(SemanalCargaValidacionResponse response, List<SemanalArchivoFilaCarga> carpetas, List<SemanalArchivoFilaCarga> delitos, List<SemanalArchivoFilaCarga> victimas)
    {
        response.TotalCarpetasIncluidas = carpetas.Count(x => x.Incluido);
        response.TotalDelitosIncluidos = delitos.Count(x => x.Incluido);
        response.TotalVictimasIncluidas = victimas.Count(x => x.Incluido);
        response.TotalCarpetasExcluidas = carpetas.Count - response.TotalCarpetasIncluidas;
        response.TotalDelitosExcluidos = delitos.Count - response.TotalDelitosIncluidos;
        response.TotalVictimasExcluidas = victimas.Count - response.TotalVictimasIncluidas;
    }

    private static void ValidarModalidadesConfiguradas(List<ArchivoFila> filasDelitos, List<ConfiguracionModalidadSemanalItem> configuracion, List<CargaValidacionError> errores)
    {
        var catalogoPorClave = configuracion.GroupBy(x => x.ClaveModalidad.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var fila in filasDelitos)
        {
            var clave = ObtenerValor(fila, "clasf_de_dto")?.Trim();

            if (string.IsNullOrWhiteSpace(clave) || !catalogoPorClave.TryGetValue(clave, out var modalidad) || modalidad.Seleccionado) continue;

            errores.Add(new CargaValidacionError
            {
                Archivo = "delitos",
                Fila = fila.NumeroFila,
                Columna = "clasf_de_dto",
                Campo = "clasf_de_dto",
                Valor = clave,
                Codigo = "SEMANAL_MODALIDAD_NO_CONFIGURADA",
                DescripcionResumen = "Modalidad no incluida en el módulo semanal",
                Mensaje = $"La clasificación {clave} ({modalidad.Modalidad}) no está incluida en la configuración semanal."
            });
        }
    }

    private static void ValidarLongitudIdentificadorDelito(List<ArchivoFila> filasDelitos, List<CargaValidacionError> errores)
    {
        foreach (var fila in filasDelitos)
        {
            var identificador = ObtenerValor(fila, "id_delito")?.Trim();

            if (string.IsNullOrWhiteSpace(identificador) || identificador.Length <= 50) continue;

            errores.Add(new CargaValidacionError
            {
                Archivo = "delitos",
                Fila = fila.NumeroFila,
                Columna = "id_delito",
                Campo = "id_delito",
                Valor = identificador,
                Codigo = "SEMANAL_ID_DELITO_LONGITUD_EXCEDIDA",
                DescripcionResumen = "Identificador de delito con longitud excedida",
                Mensaje = "El campo id_delito excede la longitud máxima semanal de 50 caracteres."
            });
        }
    }

    private static List<CargaValidacionError> ValidarEntidadUsuarioCarga(UsuarioCargaInfo usuarioCarga, List<ArchivoFila> filasDelitos)
    {
        var errores = new List<CargaValidacionError>();

        if (usuarioCarga.EsSuperUsuario) return errores;

        if (!usuarioCarga.IdEntidadFederativa.HasValue)
        {
            AgregarErrorGeneral(errores, "SEMANAL_USUARIO_SIN_ENTIDAD", "Usuario sin entidad federativa asignada", "El usuario no tiene una entidad federativa asignada y no puede realizar cargas semanales.", "id_ent_hchos", null);
            return errores;
        }

        foreach (var fila in filasDelitos)
        {
            var valor = ObtenerValor(fila, "id_ent_hchos")?.Trim();

            if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idEntidadExcel) || idEntidadExcel == usuarioCarga.IdEntidadFederativa.Value) continue;

            errores.Add(new CargaValidacionError { Archivo = "delitos", Fila = fila.NumeroFila, Columna = "id_ent_hchos", Campo = "id_ent_hchos", Valor = valor, Codigo = "SEMANAL_ENTIDAD_NO_CORRESPONDE_USUARIO", DescripcionResumen = "Entidad federativa no corresponde al usuario", Mensaje = $"La entidad del delito ({valor}) no corresponde con la entidad asignada al usuario ({usuarioCarga.IdEntidadFederativa.Value})." });
        }

        return errores;
    }

    private static int? ObtenerEntidadFederativaCarga(UsuarioCargaInfo usuarioCarga, List<ArchivoFila> filasDelitos, List<CargaValidacionError> errores)
    {
        if (!usuarioCarga.EsSuperUsuario) return usuarioCarga.IdEntidadFederativa;

        var entidades = filasDelitos.Select(fila => ObtenerValor(fila, "id_ent_hchos")?.Trim()).Where(valor => int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)).Select(valor => int.Parse(valor!, NumberStyles.Integer, CultureInfo.InvariantCulture)).ToHashSet();

        if (entidades.Count == 1) return entidades.First();

        if (entidades.Count == 0)
        {
            AgregarErrorGeneral(errores, "SEMANAL_ENTIDAD_NO_DETECTADA", "No se pudo determinar la entidad de la carga", "No se pudo determinar la entidad federativa a partir del archivo de delitos.", "id_ent_hchos", null);
        }
        else
        {
            AgregarErrorGeneral(errores, "SEMANAL_ENTIDADES_MULTIPLES", "La carga contiene múltiples entidades", "Una carga semanal solo puede corresponder a una entidad federativa.", "id_ent_hchos", string.Join(", ", entidades.OrderBy(x => x)));
        }

        return null;
    }

    private static void FinalizarRespuesta(SemanalCargaValidacionResponse response, int totalCarpetas, int totalDelitos, int totalVictimas)
    {
        var resumen = new List<CargaValidacionResumenItem>
        {
            CrearResumen("carpetas", "SEMANAL_CARPETAS_TOTAL", "Total de registros recibidos", totalCarpetas),
            CrearResumen("carpetas", "SEMANAL_CARPETAS_INCLUIDAS", "Registros incluidos en el tramo semanal", response.TotalCarpetasIncluidas),
            CrearResumen("carpetas", "SEMANAL_CARPETAS_EXCLUIDAS", "Registros fuera del tramo semanal", response.TotalCarpetasExcluidas),
            CrearResumen("delitos", "SEMANAL_DELITOS_TOTAL", "Total de registros recibidos", totalDelitos),
            CrearResumen("delitos", "SEMANAL_DELITOS_INCLUIDOS", "Registros incluidos en el tramo semanal", response.TotalDelitosIncluidos),
            CrearResumen("delitos", "SEMANAL_DELITOS_EXCLUIDOS", "Registros fuera del tramo semanal", response.TotalDelitosExcluidos),
            CrearResumen("victimas", "SEMANAL_VICTIMAS_TOTAL", "Total de registros recibidos", totalVictimas),
            CrearResumen("victimas", "SEMANAL_VICTIMAS_INCLUIDAS", "Registros incluidos en el tramo semanal", response.TotalVictimasIncluidas),
            CrearResumen("victimas", "SEMANAL_VICTIMAS_EXCLUIDAS", "Registros fuera del tramo semanal", response.TotalVictimasExcluidas)
        };

        resumen.AddRange(response.Errores.Where(x => !string.IsNullOrWhiteSpace(x.Codigo) && !string.IsNullOrWhiteSpace(x.DescripcionResumen)).GroupBy(x => new { x.Archivo, x.Codigo, x.DescripcionResumen }).Select(x => new CargaValidacionResumenItem { Archivo = x.Key.Archivo, Codigo = x.Key.Codigo, Descripcion = x.Key.DescripcionResumen, TotalRegistros = x.Count(), EsError = true }).OrderBy(x => x.Archivo).ThenBy(x => x.Codigo));
        resumen.AddRange(response.Advertencias.Where(x => !string.IsNullOrWhiteSpace(x.Codigo) && !string.IsNullOrWhiteSpace(x.DescripcionResumen)).GroupBy(x => new { x.Archivo, x.Codigo, x.DescripcionResumen }).Select(x => new CargaValidacionResumenItem { Archivo = x.Key.Archivo, Codigo = x.Key.Codigo, Descripcion = x.Key.DescripcionResumen, TotalRegistros = x.Count(), EsError = false }).OrderBy(x => x.Archivo).ThenBy(x => x.Codigo));
        response.ResumenValidacion = resumen;

        response.Mensaje = response.EsValido
            ? response.Advertencias.Count > 0
                ? "La información semanal fue validada con advertencias. Revise el detalle antes de continuar."
                : "La información semanal fue validada correctamente. Puede confirmar la carga."
            : "La información semanal contiene errores de validación.";
    }

    private static CargaValidacionResumenItem CrearResumen(string archivo, string codigo, string descripcion, int total) => new() { Archivo = archivo, Codigo = codigo, Descripcion = descripcion, TotalRegistros = total, EsError = false };

    private static void AgregarErrorGeneral(SemanalCargaValidacionResponse response, string codigo, string descripcion, string mensaje) => AgregarErrorGeneral(response.Errores, codigo, descripcion, mensaje, string.Empty, null);

    private static void AgregarErrorGeneral(List<CargaValidacionError> errores, string codigo, string descripcion, string mensaje, string columna, string? valor)
    {
        errores.Add(new CargaValidacionError { Archivo = "general", Columna = columna, Campo = columna, Valor = valor, Codigo = codigo, DescripcionResumen = descripcion, Mensaje = mensaje });
    }

    private static string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }

    private static bool IntentarConvertirFecha(string? valor, out DateTime fecha)
    {
        fecha = default;

        if (string.IsNullOrWhiteSpace(valor)) return false;

        valor = valor.Trim();
        var formatos = new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd" };

        if (DateTime.TryParseExact(valor, formatos, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaExacta))
        {
            fecha = fechaExacta.Date;
            return true;
        }

        if (DateTime.TryParse(valor, new CultureInfo("es-MX"), DateTimeStyles.None, out var fechaMx))
        {
            fecha = fechaMx.Date;
            return true;
        }

        if (double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeroExcel) && numeroExcel > 0 && numeroExcel < 60000)
        {
            try
            {
                fecha = DateTime.FromOADate(numeroExcel).Date;
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static string GenerarCodigoReferencia() => $"sem-{Guid.NewGuid():N}"[..17].ToLowerInvariant();
}
