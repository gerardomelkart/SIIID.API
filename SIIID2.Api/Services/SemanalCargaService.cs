using System.Globalization;
using SIIID2.Api.Models;
using SIIID2.Api.Readers;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace SIIID2.Api.Services;

public class SemanalCargaService : ISemanalCargaService
{
    private const long TamanioMaximoBytes = 50 * 1024 * 1024;
    private const string CodigoExclusionFueraPeriodo = "FUERA_PERIODO_CARGA";
    private readonly IMemoryCache _cache;

    private static readonly HashSet<string> TiposContenidoPermitidos =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "ACUMULADO_MES"
        };

    private static readonly HashSet<string> TiposCargaPermitidos =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "CARGA_INICIAL",
        "ACTUALIZACION"
        };

    private static readonly HashSet<string> ExtensionesPermitidas =
        new(StringComparer.OrdinalIgnoreCase)
        {
        ".csv",
        ".xlsx"
        };

    private static readonly string[] ColumnasCarpetasComparacion =
    {
        "id_ci",
        "ntra_ci",
        "fha_de_ini",
        "hra_de_ini",
        "rmen_de_hchos",
        "denuncia_anonima",
        "denuncia_anonima_089",
        "denuncia_anonima_otro_medio"
    };

    private static readonly string[] ColumnasDenunciaAnonima =
    {
        "denuncia_anonima",
        "denuncia_anonima_089",
        "denuncia_anonima_otro_medio"
    };

    private static readonly HashSet<string> ClavesExtorsion =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "4.04.01",
        "4.04.02"
        };

    private static readonly HashSet<string> ClavesRoboVehiculo =
    new(StringComparer.OrdinalIgnoreCase)
    {
        "4.01.02.01",
        "4.01.02.02"
    };

    private static readonly HashSet<int> ElementosComisionLesionesDolosasAdvertencia = new() { 6, 7, 8 };

    private static readonly HashSet<string> ValoresControlDenunciaAnonima =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "SI",
        "NO",
        "SE DESCONOCE"
        };

    private static readonly string[] ColumnasDelitosComparacion =
    {
        "id_ci",
        "id_delito",
        "dto",
        "moda_dto",
        "forma_acc",
        "fha_de_hchos",
        "hra_de_hchos",
        "emto_com_dto",
        "grdo_cons",
        "clasf_de_dto",
        "id_ent_hchos",
        "id_mun_hchos",
        "id_loc_hchos",
        "nom_loc_hchos",
        "id_col_hchos",
        "nom_col_hchos",
        "cp",
        "coord_x",
        "coord_y",
        "dom_hchos"
    };

    private static readonly string[] ColumnasVictimasComparacion =
    {
        "id_ci",
        "id_delito",
        "id_vicf",
        "id_tv",
        "id_tpm",
        "sexo",
        "genero",
        "pob",
        "disc",
        "fha_nac",
        "edad",
        "nacional"
    };

    private static readonly HashSet<string> ColumnasFechaComparacion =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "fha_de_ini",
        "fha_de_hchos",
        "fha_nac"
        };

    private static readonly HashSet<string> ColumnasHoraComparacion =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "hra_de_ini",
        "hra_de_hchos"
        };

    private static readonly HashSet<string> ColumnasEnterasComparacion =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "forma_acc",
        "emto_com_dto",
        "grdo_cons",
        "id_ent_hchos",
        "id_mun_hchos",
        "id_tv",
        "id_tpm",
        "sexo",
        "genero",
        "pob",
        "disc",
        "edad",
        "nacional"
        };

    private static readonly HashSet<string> ColumnasCeroEquivaleVacioComparacion =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "id_tpm",
        "sexo",
        "genero",
        "pob",
        "disc",
        "fha_nac",
        "nacional"
        };

    private static readonly HashSet<string> ColumnasDecimalesComparacion =
        new(StringComparer.OrdinalIgnoreCase)
        {
        "coord_x",
        "coord_y"
        };

    private sealed class SemanalDatosSemana
    {
        public List<ArchivoFila> Carpetas { get; } = new();
        public List<ArchivoFila> Delitos { get; } = new();
        public List<ArchivoFila> Victimas { get; } = new();
    }

    private sealed class SemanalDiferenciasCache
    {
        public int IdUsuarioCarga { get; init; }
        public int IdEntidadFederativa { get; init; }
        public int LimiteDetalle { get; init; }
        public string ResponseJson { get; init; } = string.Empty;
    }

    private readonly IArchivoReader _archivoReader;
    private readonly CarpetasValidator _carpetasValidator;
    private readonly DelitosValidator _delitosValidator;
    private readonly VictimasValidator _victimasValidator;
    private readonly CargaIntegridadValidator _cargaIntegridadValidator;
    private readonly CatalogosValidator _catalogosValidator;
    private readonly ISemanalDelitoRepository _semanalDelitoRepository;
    private readonly ISemanalCargaRepository _semanalCargaRepository;
    private readonly ILogger<SemanalCargaService> _logger;
    private readonly IUltimosArchivosEntidadService _ultimosArchivosEntidadService;

    public SemanalCargaService(IArchivoReader archivoReader, CarpetasValidator carpetasValidator, DelitosValidator delitosValidator, VictimasValidator victimasValidator, CargaIntegridadValidator cargaIntegridadValidator, CatalogosValidator catalogosValidator, ISemanalDelitoRepository semanalDelitoRepository, ISemanalCargaRepository semanalCargaRepository, IUltimosArchivosEntidadService ultimosArchivosEntidadService, ILogger<SemanalCargaService> logger, IMemoryCache cache)
    {
        _archivoReader = archivoReader;
        _carpetasValidator = carpetasValidator;
        _delitosValidator = delitosValidator;
        _victimasValidator = victimasValidator;
        _cargaIntegridadValidator = cargaIntegridadValidator;
        _catalogosValidator = catalogosValidator;
        _semanalDelitoRepository = semanalDelitoRepository;
        _semanalCargaRepository = semanalCargaRepository;
        _ultimosArchivosEntidadService = ultimosArchivosEntidadService;
        _logger = logger;
        _cache = cache;
    }

    public async Task<SemanalSemanaDisponibilidadResponse> ValidarSemanaAsync(string tipoCarga, int anioSemana, int numeroSemana, int? idEntidadFederativa, int idUsuario)
    {
        var tipoCargaNormalizado = (tipoCarga ?? string.Empty).Trim().ToUpperInvariant();

        if (!TiposCargaPermitidos.Contains(tipoCargaNormalizado))
        {
            return new SemanalSemanaDisponibilidadResponse
            {
                EsValido = false,
                Codigo = "SEMANAL_TIPO_CARGA_INVALIDO",
                Mensaje = "El tipo de operación semanal no es válido."
            };
        }

        if (anioSemana < 2000 || anioSemana > 9999 || numeroSemana < 1 || numeroSemana > 53)
        {
            return new SemanalSemanaDisponibilidadResponse
            {
                EsValido = false,
                Codigo = "SEMANAL_SEMANA_INVALIDA",
                Mensaje = "La semana seleccionada no es válida."
            };
        }

        var usuario = await _semanalCargaRepository.ObtenerUsuarioCargaAsync(idUsuario);

        if (usuario == null)
        {
            return new SemanalSemanaDisponibilidadResponse
            {
                EsValido = false,
                Codigo = "SEMANAL_USUARIO_SIN_ACCESO",
                Mensaje = "El usuario no tiene acceso al módulo semanal."
            };
        }

        var esActualizacion = string.Equals(tipoCargaNormalizado, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase);

        if (!usuario.HabilitaCarga)
        {
            return new SemanalSemanaDisponibilidadResponse
            {
                EsValido = false,
                Codigo = "SEMANAL_USUARIO_SIN_PERMISO_CARGA",
                Mensaje = "El usuario no tiene habilitada la carga de información semanal."
            };
        }

        int? idEntidadConsulta;

        if (usuario.EsSuperUsuario)
        {
            if (!esActualizacion)
            {
                return new SemanalSemanaDisponibilidadResponse
                {
                    EsValido = true,
                    Disponible = true,
                    Codigo = "SEMANAL_CARGA_ENTIDAD_DESDE_ARCHIVOS",
                    Mensaje = $"La semana {numeroSemana}/{anioSemana} está disponible para captura. La entidad se determinará al validar el archivo de delitos."
                };
            }

            if (!idEntidadFederativa.HasValue || idEntidadFederativa.Value <= 0)
            {
                return new SemanalSemanaDisponibilidadResponse
                {
                    EsValido = false,
                    Disponible = false,
                    Codigo = "SEMANAL_ENTIDAD_SELECCIONADA_OBLIGATORIA",
                    Mensaje = "Debe seleccionar la entidad federativa que desea actualizar."
                };
            }

            idEntidadConsulta = idEntidadFederativa.Value;
        }
        else
        {
            if (!usuario.IdEntidadFederativa.HasValue)
            {
                return new SemanalSemanaDisponibilidadResponse
                {
                    EsValido = false,
                    Disponible = false,
                    Codigo = "SEMANAL_USUARIO_SIN_ENTIDAD",
                    Mensaje = "No fue posible determinar la entidad federativa del usuario."
                };
            }

            idEntidadConsulta = usuario.IdEntidadFederativa.Value;
        }

        var estado = await _semanalCargaRepository.ObtenerEstadoSemanaAsync(idEntidadConsulta.Value, idUsuario, anioSemana, numeroSemana);
        var existePendiente = !string.IsNullOrWhiteSpace(estado.EstadoPendiente);
        var pendientePropia = existePendiente && (usuario.EsSuperUsuario || estado.IdUsuarioCargaPendiente == idUsuario);
        var pendienteMismoFlujo = string.Equals(estado.TipoCargaPendiente, tipoCargaNormalizado, StringComparison.OrdinalIgnoreCase);
        var estadoPendienteResoluble = esActualizacion ? "VALIDADO_PENDIENTE_ACTUALIZACION" : "VALIDADO_PENDIENTE";
        var puedeResolverPendiente = pendientePropia && pendienteMismoFlujo && string.Equals(estado.EstadoPendiente, estadoPendienteResoluble, StringComparison.OrdinalIgnoreCase);

        if (existePendiente)
        {
            if (puedeResolverPendiente)
            {
                return new SemanalSemanaDisponibilidadResponse
                {
                    EsValido = true,
                    Disponible = false,
                    TieneCargaConfirmada = estado.TieneCargaConfirmada,
                    ExisteOperacionPendiente = true,
                    Codigo = esActualizacion ? "SEMANAL_ACTUALIZACION_PENDIENTE_RESOLUBLE" : "SEMANAL_CARGA_PENDIENTE_RESOLUBLE",
                    Mensaje = esActualizacion
                        ? $"Existe una actualización validada pendiente de aceptar o rechazar para la semana {numeroSemana}/{anioSemana}."
                        : $"Existe una carga validada pendiente de aceptar o rechazar para la semana {numeroSemana}/{anioSemana}.",
                    CodigoReferenciaPendiente = estado.CodigoReferenciaPendiente,
                    EstadoPendiente = estado.EstadoPendiente,
                    TipoCargaPendiente = estado.TipoCargaPendiente,
                    PendientePropia = true,
                    PuedeResolverPendiente = true
                };
            }

            var enRevisionAdministrativa = string.Equals(estado.EstadoPendiente, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase);
            var pendienteActualizacion = string.Equals(estado.TipoCargaPendiente, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase);
            var puedeIrActualizacion = !esActualizacion && pendientePropia && pendienteActualizacion && !enRevisionAdministrativa;

            return new SemanalSemanaDisponibilidadResponse
            {
                EsValido = true,
                Disponible = false,
                TieneCargaConfirmada = estado.TieneCargaConfirmada,
                ExisteOperacionPendiente = true,
                Codigo = enRevisionAdministrativa ? "SEMANAL_SEMANA_PENDIENTE_APROBACION" : "SEMANAL_SEMANA_CON_OPERACION_PENDIENTE",
                Mensaje = enRevisionAdministrativa
                    ? $"La semana {numeroSemana}/{anioSemana} tiene una operación pendiente de aprobación administrativa. Debe aprobarse o rechazarse antes de registrar otra operación."
                    : pendientePropia
                        ? $"La semana {numeroSemana}/{anioSemana} tiene una operación pendiente de tipo {estado.TipoCargaPendiente}. Debe resolverse desde el flujo correspondiente."
                        : $"La semana {numeroSemana}/{anioSemana} tiene una operación pendiente registrada por otro usuario.",
                CodigoReferenciaPendiente = estado.CodigoReferenciaPendiente,
                EstadoPendiente = estado.EstadoPendiente,
                TipoCargaPendiente = estado.TipoCargaPendiente,
                PendientePropia = pendientePropia,
                PuedeResolverPendiente = false,
                DebeUsarActualizacion = puedeIrActualizacion
            };
        }

        if (!esActualizacion && estado.TieneCargaConfirmada)
        {
            return new SemanalSemanaDisponibilidadResponse
            {
                EsValido = true,
                Disponible = false,
                TieneCargaConfirmada = true,
                Codigo = "SEMANAL_SEMANA_YA_CONFIRMADA",
                Mensaje = $"La semana {numeroSemana}/{anioSemana} ya tiene información confirmada. Para modificarla debe utilizar el flujo de actualización semanal.",
                DebeUsarActualizacion = true
            };
        }

        if (esActualizacion && !estado.TieneCargaConfirmada)
        {
            return new SemanalSemanaDisponibilidadResponse
            {
                EsValido = true,
                Disponible = false,
                Codigo = "SEMANAL_ACTUALIZACION_SIN_CARGA_CONFIRMADA",
                Mensaje = $"La semana {numeroSemana}/{anioSemana} no tiene información semanal confirmada para actualizar."
            };
        }

        return new SemanalSemanaDisponibilidadResponse
        {
            EsValido = true,
            Disponible = true,
            TieneCargaConfirmada = estado.TieneCargaConfirmada,
            Codigo = esActualizacion ? "SEMANAL_SEMANA_DISPONIBLE_ACTUALIZACION" : "SEMANAL_SEMANA_DISPONIBLE_CARGA",
            Mensaje = esActualizacion
                ? $"La semana {numeroSemana}/{anioSemana} está disponible para actualización."
                : $"La semana {numeroSemana}/{anioSemana} está disponible para carga."
        };
    }

    public async Task<SemanalCargaValidacionResponse> ValidarCargaCeroAsync(SemanalCargaCeroRequest request, int idUsuarioCarga)
    {
        var fechaActual = DateTime.Today;
        var fechaInicioSemana = ObtenerInicioSemana(fechaActual).AddDays(-7);
        var fechaFinSemana = fechaInicioSemana.AddDays(6);

        var response = new SemanalCargaValidacionResponse
        {
            CodigoReferencia = GenerarCodigoReferencia(),
            TipoCarga = "CARGA_INICIAL",
            Periodo = new SemanalPeriodoCarga
            {
                TipoContenido = "ACUMULADO_MES",
                AnioSemana = ISOWeek.GetYear(fechaInicioSemana),
                NumeroSemana = ISOWeek.GetWeekOfYear(fechaInicioSemana),
                FechaInicioSemana = fechaInicioSemana,
                FechaFinSemana = fechaFinSemana,
                FechaInicioTramo = fechaInicioSemana,
                FechaFinTramo = fechaFinSemana,
                MesCorte = fechaFinSemana.Month,
                AnioCorte = fechaFinSemana.Year
            },
            Ventana = new SemanalVentanaCarga
            {
                FechaMinimaPermitida = fechaInicioSemana,
                FechaMaximaPermitida = fechaFinSemana,
                PermiteMesAnterior = fechaInicioSemana.Month != fechaFinSemana.Month
            }
        };

        if (request.IdDelito <= 0)
        {
            AgregarErrorGeneral(response, "SEMANAL_CARGA_CERO_DELITO_OBLIGATORIO", "Delito obligatorio", "Debe seleccionar el delito que desea reportar en cero.");
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        var usuarioCarga = await _semanalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioCarga);

        if (usuarioCarga == null)
        {
            AgregarErrorGeneral(response, "SEMANAL_USUARIO_SIN_ACCESO", "Usuario sin acceso semanal", "El usuario no existe, está inactivo o no tiene habilitado el módulo semanal.");
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        if (!usuarioCarga.HabilitaCarga)
        {
            AgregarErrorGeneral(response, "SEMANAL_USUARIO_SIN_PERMISO_CARGA", "Usuario sin permiso de carga semanal", "El usuario no tiene habilitada la carga de información semanal.");
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        int? idEntidadFederativa;

        if (usuarioCarga.EsSuperUsuario)
        {
            if (!request.IdEntidadFederativa.HasValue || request.IdEntidadFederativa.Value <= 0)
            {
                AgregarErrorGeneral(response, "SEMANAL_CARGA_CERO_ENTIDAD_OBLIGATORIA", "Entidad federativa obligatoria", "Debe seleccionar la entidad federativa que reportará la semana en cero.");
                FinalizarRespuesta(response, 0, 0, 0);
                return response;
            }

            idEntidadFederativa = request.IdEntidadFederativa.Value;
        }
        else
        {
            idEntidadFederativa = usuarioCarga.IdEntidadFederativa;

            if (!idEntidadFederativa.HasValue)
            {
                AgregarErrorGeneral(response, "SEMANAL_USUARIO_SIN_ENTIDAD", "Usuario sin entidad federativa", "El usuario no tiene una entidad federativa asignada.");
                FinalizarRespuesta(response, 0, 0, 0);
                return response;
            }
        }

        var configuracion = await _semanalDelitoRepository.ObtenerConfiguracionAsync();

        var modalidadesConfiguradas = configuracion
            .Where(x => x.Seleccionado && x.IdDelito == request.IdDelito)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.ClaveModalidad)
            .ToList();

        if (modalidadesConfiguradas.Count == 0)
        {
            AgregarErrorGeneral(response, "SEMANAL_CARGA_CERO_DELITO_NO_HABILITADO", "Delito no habilitado", "El delito seleccionado no está habilitado en la configuración semanal.");
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        response.Bloques = ObtenerBloquesCargaCero(fechaInicioSemana, fechaFinSemana);

        var bloquesConfirmados = await _semanalCargaRepository.ObtenerBloquesConfirmadosAsync(idEntidadFederativa.Value, idUsuarioCarga, request.IdDelito, fechaInicioSemana, fechaFinSemana);

        if (bloquesConfirmados.Count > 0)
        {
            AgregarErrorGeneral(response, "SEMANAL_CARGA_CERO_YA_REGISTRADA", "Semana previamente registrada", $"El delito seleccionado ya tiene una carga confirmada para la semana del {fechaInicioSemana:dd/MM/yyyy} al {fechaFinSemana:dd/MM/yyyy}. Si necesita agregar información debe realizar una actualización.");
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        var bloquesPendientes = await _semanalCargaRepository.ObtenerBloquesPendientesAsync(idEntidadFederativa.Value, idUsuarioCarga, request.IdDelito, fechaInicioSemana, fechaFinSemana);

        if (bloquesPendientes.Count > 0)
        {
            var pendiente = bloquesPendientes.OrderByDescending(x => x.FechaInicioSemana).First();

            AgregarErrorGeneral(response, "SEMANAL_CARGA_CERO_PENDIENTE", "Existe una operación pendiente", $"Ya existe una carga pendiente para ese delito y semana. Código de referencia: {pendiente.CodigoReferencia}.");
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        FinalizarRespuesta(response, 0, 0, 0);

        var idSemanalCargaGuardada = await _semanalCargaRepository.GuardarIntentoCargaAsync(new SemanalCargaPersistencia
        {
            IdUsuarioCarga = idUsuarioCarga,
            IdEntidadFederativa = idEntidadFederativa.Value,
            IdDelito = request.IdDelito,
            CodigoReferencia = response.CodigoReferencia,
            TipoCarga = "CARGA_INICIAL",
            Periodo = response.Periodo!,
            Ventana = response.Ventana,
            Bloques = response.Bloques,
            TotalCarpetasIncluidas = 0,
            TotalDelitosIncluidos = 0,
            TotalVictimasIncluidas = 0,
            TotalCarpetasExcluidas = 0,
            TotalDelitosExcluidos = 0,
            TotalVictimasExcluidas = 0,
            Advertencias = new List<CargaValidacionError>(),
            ModalidadesConfiguradas = modalidadesConfiguradas,
            Carpetas = new List<SemanalArchivoFilaCarga>(),
            Delitos = new List<SemanalArchivoFilaCarga>(),
            Victimas = new List<SemanalArchivoFilaCarga>()
        });

        if (!idSemanalCargaGuardada.HasValue)
        {
            AgregarErrorGeneral(response, "SEMANAL_CARGA_CERO_PENDIENTE", "Existe una operación pendiente", "La semana seleccionada ya forma parte de otra operación pendiente.");
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        response.Mensaje = $"La carga en cero del delito seleccionado para la semana del {fechaInicioSemana:dd/MM/yyyy} al {fechaFinSemana:dd/MM/yyyy} fue preparada correctamente. Revise el informe previo antes de confirmarla.";

        _logger.LogInformation("Carga semanal en cero validada. Referencia: {CodigoReferencia}, Entidad: {IdEntidad}, Delito: {IdDelito}, Semana: {FechaInicio:yyyy-MM-dd} a {FechaFin:yyyy-MM-dd}", response.CodigoReferencia, idEntidadFederativa.Value, request.IdDelito, fechaInicioSemana, fechaFinSemana);

        return response;
    }

    public async Task<SemanalCargaValidacionResponse> ValidarArchivosAsync(SemanalCargaValidacionRequest request, int idUsuarioCarga)
    {
        var tipoCarga = (request.TipoCarga ?? string.Empty).Trim().ToUpperInvariant();

        var response = new SemanalCargaValidacionResponse
        {
            CodigoReferencia = GenerarCodigoReferencia(),
            TipoCarga = tipoCarga,
            Ventana = ObtenerVentanaCarga(DateTime.Today)
        };

        if (!TiposCargaPermitidos.Contains(tipoCarga))
        {
            AgregarErrorGeneral(response, "SEMANAL_TIPO_CARGA_INVALIDO", "Tipo de operación inválido", "El tipo de operación debe ser CARGA_INICIAL o ACTUALIZACION.");
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        var usuarioCarga = await _semanalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioCarga);

        if (usuarioCarga == null)
        {
            AgregarErrorGeneral(
                response,
                "SEMANAL_USUARIO_SIN_ACCESO",
                "Usuario sin acceso semanal",
                "El usuario no existe, está inactivo o no tiene habilitado el módulo semanal.");

            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        if (!usuarioCarga.HabilitaCarga)
        {
            AgregarErrorGeneral(response, "SEMANAL_USUARIO_SIN_PERMISO_CARGA", "Usuario sin permiso de carga semanal", "El usuario no tiene habilitada la carga de información semanal.");
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

        var erroresEstructura =
            await ValidarEstructuraArchivosAsync(
                request.Carpetas!,
                request.Delitos!,
                request.Victimas!);

        if (erroresEstructura.Count > 0)
        {
            response.Errores.AddRange(erroresEstructura);
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        var periodo = response.Periodo!;
        var ventana = response.Ventana!;

        var filasCarpetas =
            await _archivoReader.LeerAsync(request.Carpetas!);

        var filasDelitos =
            await _archivoReader.LeerAsync(request.Delitos!);

        var filasVictimas =
            await _archivoReader.LeerAsync(request.Victimas!);

        NormalizarColumnasExtorsion(filasCarpetas);
        NormalizarGradoConsumacionExtorsion(filasDelitos);

        response.Errores.AddRange(
            _carpetasValidator.Validar(
                filasCarpetas,
                validarMesInmediatoAnterior: false));

        response.Errores.AddRange(
            _delitosValidator.Validar(filasDelitos));

        response.Errores.AddRange(
            _victimasValidator.Validar(filasVictimas));

        if (response.Errores.Count > 0)
        {
            FinalizarRespuesta(
                response,
                filasCarpetas.Count,
                filasDelitos.Count,
                filasVictimas.Count);

            return response;
        }

        var carpetasEtiquetadas = EtiquetarCarpetas(filasCarpetas, ventana);

        var exclusionesPorCarpeta = ObtenerExclusionesPorCarpeta(carpetasEtiquetadas);

        var delitosEtiquetados = EtiquetarPorCarpeta(filasDelitos, exclusionesPorCarpeta);

        var victimasEtiquetadas = EtiquetarPorCarpeta(filasVictimas, exclusionesPorCarpeta);

        var carpetasPeriodo = carpetasEtiquetadas
            .Where(x => x.Incluido)
            .Select(x => x.Fila)
            .ToList();

        var delitosPeriodo = delitosEtiquetados
            .Where(x => x.Incluido)
            .Select(x => x.Fila)
            .ToList();

        var victimasPeriodo = victimasEtiquetadas
            .Where(x => x.Incluido)
            .Select(x => x.Fila)
            .ToList();

        ActualizarTotales(
            response,
            carpetasEtiquetadas,
            delitosEtiquetados,
            victimasEtiquetadas);

        ValidarFechasFueraVentana(carpetasEtiquetadas, ventana, response.Errores);

        if (response.Errores.Count > 0)
        {
            FinalizarRespuesta(response, filasCarpetas.Count, filasDelitos.Count, filasVictimas.Count);
            return response;
        }

        if (carpetasPeriodo.Count == 0)
        {
            AgregarErrorGeneral(response, "SEMANAL_SIN_CARPETAS_EN_VENTANA", "Sin carpetas dentro de la ventana permitida", $"No existen carpetas cuya fecha de inicio esté entre {ventana.FechaMinimaPermitida:dd/MM/yyyy} y {ventana.FechaMaximaPermitida:dd/MM/yyyy}.");

            FinalizarRespuesta(response, filasCarpetas.Count, filasDelitos.Count, filasVictimas.Count);
            return response;
        }

        response.Errores.AddRange(ValidarEntidadUsuarioCarga(usuarioCarga, delitosPeriodo));

        var idEntidadFederativa = ObtenerEntidadFederativaCarga(usuarioCarga, delitosPeriodo, response.Errores);

        if (response.Errores.Count > 0 || !idEntidadFederativa.HasValue)
        {
            FinalizarRespuesta(response, filasCarpetas.Count, filasDelitos.Count, filasVictimas.Count);

            return response;
        }

        var configuracion = await _semanalDelitoRepository.ObtenerConfiguracionAsync();

        var clavesModalidadCarga = delitosPeriodo
            .Select(x => ObtenerValor(x, "clasf_de_dto")?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var idsDelitosCarga = configuracion
            .Where(x => clavesModalidadCarga.Contains(x.ClaveModalidad))
            .Select(x => x.IdDelito)
            .Distinct()
            .ToList();

        if (idsDelitosCarga.Count != 1)
        {
            AgregarErrorGeneral(
                response,
                "SEMANAL_CARGA_DEBE_CONTENER_UN_DELITO",
                "No fue posible identificar un único delito",
                "Cada paquete del módulo preliminar debe contener información de un solo delito. Homicidio, Extorsión y los demás delitos deben cargarse en paquetes independientes.");

            FinalizarRespuesta(response, filasCarpetas.Count, filasDelitos.Count, filasVictimas.Count);
            return response;
        }

        var idDelitoCarga = idsDelitosCarga[0];

        response.Bloques = ObtenerBloquesCarga(carpetasPeriodo, delitosPeriodo, victimasPeriodo);

        var bloquesConfirmados = await _semanalCargaRepository.ObtenerBloquesConfirmadosAsync(idEntidadFederativa.Value, idUsuarioCarga, idDelitoCarga, response.Bloques.Min(x => x.FechaInicioTramo), response.Bloques.Max(x => x.FechaFinTramo));
        MarcarBloquesParaReemplazo(response.Bloques, bloquesConfirmados);

        var tieneBloquesReemplazo = response.Bloques.Any(x => x.ReemplazaInformacion);

        tipoCarga = tieneBloquesReemplazo ? "ACTUALIZACION" : "CARGA_INICIAL";
        response.TipoCarga = tipoCarga;
        var esActualizacion = tieneBloquesReemplazo;

        var fechaInicioOperacion = response.Bloques.Min(x => x.FechaInicioTramo);
        var fechaFinOperacion = response.Bloques.Max(x => x.FechaFinTramo);
        var bloquesPendientes = await _semanalCargaRepository.ObtenerBloquesPendientesAsync(idEntidadFederativa.Value, idUsuarioCarga, idDelitoCarga, fechaInicioOperacion, fechaFinOperacion);
        var clavesBloquesOperacion = response.Bloques.Select(x => (x.FechaInicioSemana.Date, x.AnioCorte, x.MesCorte)).ToHashSet();

        foreach (var pendiente in bloquesPendientes.Where(x => clavesBloquesOperacion.Contains((x.FechaInicioSemana.Date, x.AnioCorte, x.MesCorte))).OrderBy(x => x.FechaInicioSemana).ThenBy(x => x.AnioCorte).ThenBy(x => x.MesCorte))
        {
            AgregarErrorGeneral(response.Errores, "SEMANAL_BLOQUE_CON_CARGA_PENDIENTE", "Bloque con una carga pendiente", $"La semana {pendiente.NumeroSemana}/{pendiente.AnioSemana}, corte {pendiente.MesCorte:00}/{pendiente.AnioCorte}, ya forma parte de una carga en estado {pendiente.Estado}. Código de referencia: {pendiente.CodigoReferencia}.", "bloque", $"{pendiente.AnioSemana}-W{pendiente.NumeroSemana:00}-{pendiente.AnioCorte}-{pendiente.MesCorte:00}");
        }

        if (response.Errores.Count > 0)
        {
            FinalizarRespuesta(response, filasCarpetas.Count, filasDelitos.Count, filasVictimas.Count);
            return response;
        }

        var carpetasIncluidas = carpetasEtiquetadas
            .Where(x => x.Incluido)
            .Select(x => x.Fila)
            .ToList();

        var delitosIncluidos = delitosEtiquetados
            .Where(x => x.Incluido)
            .Select(x => x.Fila)
            .ToList();

        var victimasIncluidas = victimasEtiquetadas
            .Where(x => x.Incluido)
            .Select(x => x.Fila)
            .ToList();

        if (carpetasIncluidas.Count == 0)
        {
            AgregarErrorGeneral(
                response,
                esActualizacion ? "SEMANAL_ACTUALIZACION_SIN_REGISTROS" : "SEMANAL_CARGA_SIN_REGISTROS",
                esActualizacion ? "La actualización no contiene registros" : "La carga no contiene registros",
                esActualizacion ? "No existen registros dentro del periodo mensual permitido para actualizar." : "No existen registros dentro del periodo mensual permitido para cargar.");

            FinalizarRespuesta(
                response,
                filasCarpetas.Count,
                filasDelitos.Count,
                filasVictimas.Count);

            return response;
        }

        var erroresIntegridad = _cargaIntegridadValidator.Validar(carpetasIncluidas, delitosIncluidos, victimasIncluidas);

        if (usuarioCarga.EsSuperUsuario)
        {
            response.Advertencias.AddRange(erroresIntegridad.Where(x =>  x.Codigo == "INTEGRIDAD_FECHA_HECHOS_MAYOR_FECHA_INICIO"));

            erroresIntegridad = erroresIntegridad
                .Where(x =>
                    x.Codigo !=
                    "INTEGRIDAD_FECHA_HECHOS_MAYOR_FECHA_INICIO")
                .ToList();
        }

        response.Errores.AddRange(erroresIntegridad);

        response.Errores.AddRange(
            await _catalogosValidator.ValidarAsync(
                carpetasIncluidas,
                delitosIncluidos,
                victimasIncluidas));

        ValidarExtorsionDenunciaAnonima(carpetasIncluidas, delitosIncluidos, response.Errores);

        ValidarHomicidioTentativa(delitosIncluidos, response.Errores);

        ValidarHomicidioDolosoGradoConsumacion(delitosIncluidos, response.Errores);

        ValidarHomicidioDolosoTipoVictima(delitosIncluidos, victimasIncluidas, response.Errores);

        var validacionCoordenadas = await _catalogosValidator.ValidarCoordenadasHomicidioDolosoSemanalAsync(delitosIncluidos);

        response.Errores.AddRange(validacionCoordenadas.Errores);

        if (validacionCoordenadas.Advertencia is not null) response.Advertencias.Add(validacionCoordenadas.Advertencia);

        ValidarLongitudIdentificadorDelito(delitosIncluidos, response.Errores);

        ValidarRoboVehiculoFormaAccion(delitosIncluidos, response.Errores);

        var modalidadesConfiguradas = configuracion
            .Where(x => x.Seleccionado)
            .OrderBy(x => x.Orden)
            .ThenBy(x => x.ClaveModalidad)
            .ToList();

        if (modalidadesConfiguradas.Count == 0)
        {
            AgregarErrorGeneral(
                response,
                "SEMANAL_SIN_MODALIDADES_CONFIGURADAS",
                "Sin modalidades semanales configuradas",
                "No existen modalidades activas habilitadas para la carga semanal.");
        }
        else
        {
            ValidarModalidadesConfiguradas(
                delitosIncluidos,
                configuracion,
                response.Errores);
        }

        if (response.Errores.Count == 0)
        {
            response.Advertencias.AddRange(_delitosValidator.ValidarAdvertencias(delitosIncluidos));
            response.Advertencias.AddRange(_cargaIntegridadValidator.ValidarAdvertencias(delitosIncluidos, victimasIncluidas));
            ValidarHomicidioDolosoResumenHechos(carpetasIncluidas, delitosIncluidos, response.Advertencias);
            ValidarLesionesDolosasElementoComision(delitosIncluidos, response.Advertencias);
        }

        FinalizarRespuesta(
            response,
            filasCarpetas.Count,
            filasDelitos.Count,
            filasVictimas.Count);

        if (!response.EsValido)
        {
            return response;
        }

        var idSemanalCargaGuardada = await _semanalCargaRepository.GuardarIntentoCargaAsync(
            new SemanalCargaPersistencia
            {
                IdUsuarioCarga = idUsuarioCarga,
                IdEntidadFederativa = idEntidadFederativa.Value,
                IdDelito = idDelitoCarga,
                CodigoReferencia = response.CodigoReferencia,
                TipoCarga = tipoCarga,
                Periodo = periodo,
                Ventana = response.Ventana,
                Bloques = response.Bloques,
                TotalCarpetasIncluidas = response.TotalCarpetasIncluidas,
                TotalDelitosIncluidos = response.TotalDelitosIncluidos,
                TotalVictimasIncluidas = response.TotalVictimasIncluidas,
                TotalCarpetasExcluidas = response.TotalCarpetasExcluidas,
                TotalDelitosExcluidos = response.TotalDelitosExcluidos,
                TotalVictimasExcluidas = response.TotalVictimasExcluidas,
                Advertencias = response.Advertencias,
                ModalidadesConfiguradas = modalidadesConfiguradas,
                Carpetas = carpetasEtiquetadas,
                Delitos = delitosEtiquetados,
                Victimas = victimasEtiquetadas
            });

        if (!idSemanalCargaGuardada.HasValue)
        {
            AgregarErrorGeneral(
                response,
                "SEMANAL_BLOQUE_CON_CARGA_PENDIENTE",
                "Bloque con una carga pendiente",
                "Uno o más bloques lunes-domingo del mismo delito ya forman parte de otra carga pendiente.");

            FinalizarRespuesta(response, filasCarpetas.Count, filasDelitos.Count, filasVictimas.Count);
            return response;
        }

        await _ultimosArchivosEntidadService.GuardarSemanalAsync(
            idEntidadFederativa.Value,
            idUsuarioCarga,
            response.CodigoReferencia,
            tipoCarga,
            periodo,
            request.Carpetas!,
            request.Delitos!,
            request.Victimas!);

        _logger.LogInformation(
            "Operación preliminar acumulativa validada. Tipo: {TipoCarga}, Referencia: {CodigoReferencia}, Entidad: {IdEntidad}, Corte: {MesCorte:00}/{AnioCorte}, Periodo: {FechaInicioTramo:yyyy-MM-dd} a {FechaFinTramo:yyyy-MM-dd}",
            tipoCarga,
            response.CodigoReferencia,
            idEntidadFederativa.Value,
            periodo.MesCorte,
            periodo.AnioCorte,
            periodo.FechaInicioTramo,
            periodo.FechaFinTramo);

        return response;
    }

    public async Task<ActualizacionDiferenciasResponse> ObtenerDiferenciasAsync(string codigoReferencia, int idUsuarioConsulta, int limitePorSeccion, bool soloMuestra = false)
    {
        var codigoLimpio = codigoReferencia?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(codigoLimpio))
        {
            return ErrorDiferencias(
                codigoLimpio,
                "Debe proporcionar el código de referencia de la actualización semanal.");
        }

        var usuario = await _semanalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuario == null)
        {
            return ErrorDiferencias(
                codigoLimpio,
                "El usuario no existe, está inactivo o no tiene acceso al módulo semanal.");
        }

        var cacheKey = $"SEMANAL_DIFERENCIAS:{codigoLimpio}";

        if (_cache.TryGetValue<SemanalDiferenciasCache>(cacheKey, out var cache) && cache != null && (limitePorSeccion == 0 || limitePorSeccion <= cache.LimiteDetalle))
        {
            if (!usuario.EsSuperUsuario && (cache.IdUsuarioCarga != idUsuarioConsulta || usuario.IdEntidadFederativa != cache.IdEntidadFederativa))
                return ErrorDiferencias(codigoLimpio, "El usuario no tiene permiso para consultar las diferencias de esta actualización semanal.");

            return PrepararDiferenciasCache(cache.ResponseJson, limitePorSeccion);
        }

        var carga = await _semanalCargaRepository.ObtenerCargaParaDiferenciasAsync(codigoLimpio);

        if (carga == null)
        {
            return ErrorDiferencias(
                codigoLimpio,
                "No se encontró una actualización semanal pendiente con ese código de referencia.");
        }

        if (!usuario.EsSuperUsuario &&
            (carga.IdUsuarioCarga != idUsuarioConsulta ||
             usuario.IdEntidadFederativa != carga.IdEntidadFederativa))
        {
            return ErrorDiferencias(
                codigoLimpio,
                "El usuario no tiene permiso para consultar las diferencias de esta actualización semanal.");
        }

        var datosComparacion = await _semanalCargaRepository.ObtenerDatosComparacionAsync(carga.IdSemanalCarga, carga.IdEntidadFederativa, carga.IdUsuarioCarga);

        var datosConfirmados = new SemanalDatosSemana();
        datosConfirmados.Carpetas.AddRange(datosComparacion.CarpetasConfirmadas.Select(ConvertirFilaComparacion));
        datosConfirmados.Delitos.AddRange(datosComparacion.DelitosConfirmados.Select(ConvertirFilaComparacion));
        datosConfirmados.Victimas.AddRange(datosComparacion.VictimasConfirmadas.Select(ConvertirFilaComparacion));

        var datosNuevos = new SemanalDatosSemana();
        datosNuevos.Carpetas.AddRange(carga.Carpetas.Select(ConvertirFilaComparacion));
        datosNuevos.Delitos.AddRange(carga.Delitos.Select(ConvertirFilaComparacion));
        datosNuevos.Victimas.AddRange(carga.Victimas.Select(ConvertirFilaComparacion));

        var response = new ActualizacionDiferenciasResponse
        {
            EsValido = true,
            CodigoReferencia = codigoLimpio,
            LimitePorSeccion = limitePorSeccion
        };

        response.ResumenCarpetas = AgregarDiferenciasSeccion(
            datosNuevos.Carpetas,
            datosConfirmados.Carpetas,
            new[] { "id_ci" },
            ColumnasCarpetasComparacion,
            response.Carpetas,
            limitePorSeccion,
            soloMuestra);

        response.ResumenDelitos = AgregarDiferenciasSeccion(
            datosNuevos.Delitos,
            datosConfirmados.Delitos,
            new[] { "id_ci", "id_delito" },
            ColumnasDelitosComparacion,
            response.Delitos,
            limitePorSeccion,
            soloMuestra);

        response.ResumenVictimas = AgregarDiferenciasSeccion(
            datosNuevos.Victimas,
            datosConfirmados.Victimas,
            new[] { "id_ci", "id_delito", "id_vicf" },
            ColumnasVictimasComparacion,
            response.Victimas,
            limitePorSeccion,
            soloMuestra);

        response.ResumenTotal = new ActualizacionDiferenciasResumen
        {
            Nuevos = response.ResumenCarpetas.Nuevos + response.ResumenDelitos.Nuevos + response.ResumenVictimas.Nuevos,
            Modificados = response.ResumenCarpetas.Modificados + response.ResumenDelitos.Modificados + response.ResumenVictimas.Modificados,
            Eliminados = response.ResumenCarpetas.Eliminados + response.ResumenDelitos.Eliminados + response.ResumenVictimas.Eliminados
        };

        response.TotalCarpetas = response.ResumenCarpetas.Nuevos + response.ResumenCarpetas.Modificados + response.ResumenCarpetas.Eliminados;
        response.TotalDelitos = response.ResumenDelitos.Nuevos + response.ResumenDelitos.Modificados + response.ResumenDelitos.Eliminados;
        response.TotalVictimas = response.ResumenVictimas.Nuevos + response.ResumenVictimas.Modificados + response.ResumenVictimas.Eliminados;
        response.TotalDiferencias =
            response.TotalCarpetas +
            response.TotalDelitos +
            response.TotalVictimas;

        response.DetalleLimitado = soloMuestra && limitePorSeccion > 0
            ? response.Carpetas.Count >= limitePorSeccion ||
              response.Delitos.Count >= limitePorSeccion ||
              response.Victimas.Count >= limitePorSeccion
            : response.TotalCarpetas > response.Carpetas.Count ||
              response.TotalDelitos > response.Delitos.Count ||
              response.TotalVictimas > response.Victimas.Count;

        response.Mensaje = response.TotalDiferencias == 0
            ? "No se detectaron diferencias entre la actualización y la versión confirmada."
            : "Revise los registros nuevos, modificados y eliminados antes de continuar al informe previo.";

        if (!soloMuestra && limitePorSeccion > 0)
        {
            _cache.Set(cacheKey, new SemanalDiferenciasCache
            {
                IdUsuarioCarga = carga.IdUsuarioCarga,
                IdEntidadFederativa = carga.IdEntidadFederativa,
                LimiteDetalle = limitePorSeccion,
                ResponseJson = JsonSerializer.Serialize(response)
            }, new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromHours(12),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(48)
            });
        }

        return response;
    }

    public Task<ConfirmarCargaResponse> ConfirmarCargaAsync(ConfirmarCargaRequest request, int idUsuarioConfirmacion) => _semanalCargaRepository.ConfirmarCargaAsync(request.CodigoReferencia.Trim(), request.Aceptar, idUsuarioConfirmacion);

    private static SemanalVentanaCarga ObtenerVentanaCarga(DateTime fechaReferencia)
    {
        var fechaActual = fechaReferencia.Date;
        var fechaInicioMesActual = new DateTime(fechaActual.Year, fechaActual.Month, 1);

        return new SemanalVentanaCarga
        {
            FechaMinimaPermitida = fechaInicioMesActual,
            FechaMaximaPermitida = fechaActual.AddDays(-1),
            PermiteMesAnterior = fechaActual.Day <= 7
        };
    }

    private static List<SemanalCargaBloque> ObtenerBloquesCarga(List<ArchivoFila> carpetas, List<ArchivoFila> delitos, List<ArchivoFila> victimas)
    {
        var bloques = new Dictionary<(DateTime FechaInicioSemana, int AnioCorte, int MesCorte), SemanalCargaBloque>();
        var bloquePorCarpeta = new Dictionary<string, SemanalCargaBloque>(StringComparer.OrdinalIgnoreCase);

        foreach (var carpeta in carpetas)
        {
            var idCi = ObtenerValor(carpeta, "id_ci")?.Trim();

            if (string.IsNullOrWhiteSpace(idCi) || !IntentarConvertirFecha(ObtenerValor(carpeta, "fha_de_ini"), out var fechaInicio)) continue;

            var fechaInicioSemana = ObtenerInicioSemana(fechaInicio);
            var fechaFinSemana = fechaInicioSemana.AddDays(6);
            var fechaInicioMes = new DateTime(fechaInicio.Year, fechaInicio.Month, 1);
            var fechaFinMes = fechaInicioMes.AddMonths(1).AddDays(-1);
            var clave = (fechaInicioSemana, fechaInicio.Year, fechaInicio.Month);

            if (!bloques.TryGetValue(clave, out var bloque))
            {
                bloque = new SemanalCargaBloque
                {
                    AnioSemana = ISOWeek.GetYear(fechaInicio),
                    NumeroSemana = ISOWeek.GetWeekOfYear(fechaInicio),
                    FechaInicioSemana = fechaInicioSemana,
                    FechaFinSemana = fechaFinSemana,
                    AnioCorte = fechaInicio.Year,
                    MesCorte = fechaInicio.Month,
                    FechaInicioTramo = fechaInicioSemana > fechaInicioMes ? fechaInicioSemana : fechaInicioMes,
                    FechaFinTramo = fechaFinSemana < fechaFinMes ? fechaFinSemana : fechaFinMes
                };

                bloques[clave] = bloque;
            }

            bloque.TotalCarpetas++;
            bloquePorCarpeta[idCi] = bloque;
        }

        foreach (var delito in delitos)
        {
            var idCi = ObtenerValor(delito, "id_ci")?.Trim();

            if (!string.IsNullOrWhiteSpace(idCi) && bloquePorCarpeta.TryGetValue(idCi, out var bloque)) bloque.TotalDelitos++;
        }

        foreach (var victima in victimas)
        {
            var idCi = ObtenerValor(victima, "id_ci")?.Trim();

            if (!string.IsNullOrWhiteSpace(idCi) && bloquePorCarpeta.TryGetValue(idCi, out var bloque)) bloque.TotalVictimas++;
        }

        return bloques.Values.OrderBy(x => x.FechaInicioSemana).ThenBy(x => x.AnioCorte).ThenBy(x => x.MesCorte).ToList();
    }

    private static void MarcarBloquesParaReemplazo(List<SemanalCargaBloque> bloques, List<SemanalCargaBloqueConfirmado> bloquesConfirmados)
    {
        var clavesConfirmadas = bloquesConfirmados.Select(x => (x.FechaInicioSemana.Date, x.AnioCorte, x.MesCorte)).ToHashSet();

        foreach (var bloque in bloques) bloque.ReemplazaInformacion = clavesConfirmadas.Contains((bloque.FechaInicioSemana.Date, bloque.AnioCorte, bloque.MesCorte));
    }

    private static SemanalPeriodoCarga? ValidarPeriodo(SemanalCargaValidacionRequest request, List<CargaValidacionError> errores)
    {
        var tipoContenido = (request.TipoContenido ?? string.Empty).Trim().ToUpperInvariant();

        if (!TiposContenidoPermitidos.Contains(tipoContenido))
        {
            AgregarErrorGeneral(
                errores,
                "SEMANAL_TIPO_CONTENIDO_INVALIDO",
                "Tipo de contenido inválido",
                "El módulo preliminar únicamente admite la carga acumulada del mes en curso.",
                "tipoContenido",
                request.TipoContenido);

            return null;
        }

        var fechaActual = DateTime.Today;
        var fechaInicioMesActual = new DateTime(fechaActual.Year, fechaActual.Month, 1);
        var fechaInicioSemanaActual = ObtenerInicioSemana(fechaActual);
        var fechaFinSemanaActual = fechaInicioSemanaActual.AddDays(6);

        return new SemanalPeriodoCarga
        {
            TipoContenido = "ACUMULADO_MES",
            AnioSemana = ISOWeek.GetYear(fechaActual),
            NumeroSemana = ISOWeek.GetWeekOfYear(fechaActual),
            FechaInicioSemana = fechaInicioSemanaActual,
            FechaFinSemana = fechaFinSemanaActual,
            FechaInicioTramo = fechaInicioMesActual,
            FechaFinTramo = fechaActual.AddDays(-1),
            MesCorte = fechaActual.Month,
            AnioCorte = fechaActual.Year
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

    private static bool EsFechaMesAnteriorPermitida(DateTime fechaInicio, SemanalVentanaCarga ventana)
    {
        if (!ventana.PermiteMesAnterior) return false;

        var fechaInicioMesAnterior = ventana.FechaMinimaPermitida.AddMonths(-1);

        return fechaInicio.Date >= fechaInicioMesAnterior.Date && fechaInicio.Date < ventana.FechaMinimaPermitida.Date;
    }

    private static void ValidarFechasFueraVentana(List<SemanalArchivoFilaCarga> carpetas, SemanalVentanaCarga ventana, List<CargaValidacionError> errores)
    {
        var fechaActual = ventana.FechaMaximaPermitida.Date.AddDays(1);

        foreach (var item in carpetas.Where(x => !x.Incluido))
        {
            var valorFecha = ObtenerValor(item.Fila, "fha_de_ini");

            if (!IntentarConvertirFecha(valorFecha, out var fechaInicio)) continue;

            if (fechaInicio.Date == fechaActual)
            {
                errores.Add(new CargaValidacionError
                {
                    Archivo = "carpetas",
                    Fila = item.Fila.NumeroFila,
                    Columna = "fha_de_ini",
                    Campo = "fha_de_ini",
                    Valor = valorFecha,
                    Codigo = "SEMANAL_FECHA_DIA_ACTUAL_NO_PERMITIDA",
                    DescripcionResumen = "Registro correspondiente al día en curso",
                    Mensaje = $"La fecha {fechaInicio:dd/MM/yyyy} corresponde al día en curso. La carga semanal únicamente puede contener información hasta el día anterior."
                });

                continue;
            }

            if (fechaInicio.Date > fechaActual)
            {
                errores.Add(new CargaValidacionError
                {
                    Archivo = "carpetas",
                    Fila = item.Fila.NumeroFila,
                    Columna = "fha_de_ini",
                    Campo = "fha_de_ini",
                    Valor = valorFecha,
                    Codigo = "SEMANAL_FECHA_FUTURA_NO_PERMITIDA",
                    DescripcionResumen = "Fecha futura no permitida",
                    Mensaje = $"La fecha {fechaInicio:dd/MM/yyyy} es posterior al día actual. No se permiten fechas futuras."
                });

                continue;
            }

            errores.Add(new CargaValidacionError
            {
                Archivo = "carpetas",
                Fila = item.Fila.NumeroFila,
                Columna = "fha_de_ini",
                Campo = "fha_de_ini",
                Valor = valorFecha,
                Codigo = "SEMANAL_FECHA_FUERA_VENTANA",
                DescripcionResumen = "Fecha fuera de la ventana permitida",
                Mensaje = $"La fecha {fechaInicio:dd/MM/yyyy} está fuera de la ventana permitida, del {ventana.FechaMinimaPermitida:dd/MM/yyyy} al {ventana.FechaMaximaPermitida:dd/MM/yyyy}."
            });
        }
    }

    private static List<SemanalArchivoFilaCarga> EtiquetarCarpetas(List<ArchivoFila> filas, SemanalVentanaCarga ventana)
    {
        return filas.Select(fila =>
        {
            var incluido = false;

            if (IntentarConvertirFecha(ObtenerValor(fila, "fha_de_ini"), out var fechaInicio))
            {
                var fechaMesActualPermitida = fechaInicio.Date >= ventana.FechaMinimaPermitida.Date && fechaInicio.Date <= ventana.FechaMaximaPermitida.Date;
                incluido = fechaMesActualPermitida || EsFechaMesAnteriorPermitida(fechaInicio, ventana);
            }

            return new SemanalArchivoFilaCarga
            {
                Fila = fila,
                Incluido = incluido,
                CodigoExclusion = incluido ? null : CodigoExclusionFueraPeriodo
            };
        }).ToList();
    }

    private static Dictionary<string, string> ObtenerExclusionesPorCarpeta(List<SemanalArchivoFilaCarga> carpetas)
    {
        var exclusiones =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var carpeta in carpetas.Where(x => !x.Incluido))
        {
            var idCi =
                ObtenerValor(carpeta.Fila, "id_ci")?.Trim();

            if (string.IsNullOrWhiteSpace(idCi))
            {
                continue;
            }

            exclusiones[idCi] =
                carpeta.CodigoExclusion ??
                CodigoExclusionFueraPeriodo;
        }

        return exclusiones;
    }

    private static List<SemanalArchivoFilaCarga> EtiquetarPorCarpeta(List<ArchivoFila> filas, Dictionary<string, string> exclusionesPorCarpeta)
    {
        return filas.Select(fila =>
        {
            var idCi =
                ObtenerValor(fila, "id_ci")?.Trim();

            string? codigoExclusion = null;

            var excluido =
                !string.IsNullOrWhiteSpace(idCi) &&
                exclusionesPorCarpeta.TryGetValue(
                    idCi,
                    out codigoExclusion);

            return new SemanalArchivoFilaCarga
            {
                Fila = fila,
                Incluido = !excluido,
                CodigoExclusion = excluido
                    ? codigoExclusion
                    : null
            };
        }).ToList();
    }

    private static ArchivoFila ConvertirFilaComparacion(SemanalFilaComparacion item)
    {
        var columnas =
            JsonSerializer.Deserialize<
                Dictionary<string, string?>>(
                item.Datos) ??
            new Dictionary<string, string?>();

        return new ArchivoFila
        {
            Columnas =
                new Dictionary<string, string?>(
                    columnas,
                    StringComparer.OrdinalIgnoreCase)
        };
    }

    private static DateTime ObtenerInicioSemana(DateTime fecha)
    {
        var diasDesdeLunes = ((int)fecha.DayOfWeek + 6) % 7;

        return fecha.Date.AddDays(-diasDesdeLunes);
    }

    private static List<SemanalCargaBloque> ObtenerBloquesCargaCero(DateTime fechaInicioSemana, DateTime fechaFinSemana)
    {
        var bloques = new List<SemanalCargaBloque>();
        var fechaInicioTramo = fechaInicioSemana.Date;
        var anioSemana = ISOWeek.GetYear(fechaInicioSemana);
        var numeroSemana = ISOWeek.GetWeekOfYear(fechaInicioSemana);

        while (fechaInicioTramo <= fechaFinSemana.Date)
        {
            var fechaFinMes = new DateTime(fechaInicioTramo.Year, fechaInicioTramo.Month, 1).AddMonths(1).AddDays(-1);
            var fechaFinTramo = fechaFinSemana.Date < fechaFinMes ? fechaFinSemana.Date : fechaFinMes;

            bloques.Add(new SemanalCargaBloque
            {
                AnioSemana = anioSemana,
                NumeroSemana = numeroSemana,
                FechaInicioSemana = fechaInicioSemana.Date,
                FechaFinSemana = fechaFinSemana.Date,
                AnioCorte = fechaInicioTramo.Year,
                MesCorte = fechaInicioTramo.Month,
                FechaInicioTramo = fechaInicioTramo,
                FechaFinTramo = fechaFinTramo,
                TotalCarpetas = 0,
                TotalDelitos = 0,
                TotalVictimas = 0,
                ReemplazaInformacion = false
            });

            fechaInicioTramo = fechaFinTramo.AddDays(1);
        }

        return bloques;
    }

    private static string NormalizarValorComparacion(string columna, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return string.Empty;
        }

        valor = valor.Trim();

        if (ColumnasCeroEquivaleVacioComparacion.Contains(columna) && valor.All(caracter => caracter == '0'))
        {
            return string.Empty;
        }

        if (string.Equals(
                columna,
                "edad",
                StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(
                valor,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var edad) &&
            edad == 999)
        {
            return string.Empty;
        }

        if (ColumnasFechaComparacion.Contains(columna) &&
            IntentarConvertirFechaHoraComparacion(
                valor,
                out var fecha))
        {
            return fecha.TimeOfDay == TimeSpan.Zero
                ? fecha.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture)
                : fecha.ToString(
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture);
        }

        if (ColumnasHoraComparacion.Contains(columna) &&
            IntentarConvertirHora(valor, out var hora))
        {
            return hora.ToString(
                @"hh\:mm\:ss",
                CultureInfo.InvariantCulture);
        }

        if (ColumnasEnterasComparacion.Contains(columna) &&
            long.TryParse(
                valor,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var entero))
        {
            return entero.ToString(
                CultureInfo.InvariantCulture);
        }

        if (ColumnasDecimalesComparacion.Contains(columna) &&
            IntentarConvertirDecimal(
                valor,
                out var decimalNormalizado))
        {
            return decimalNormalizado.ToString(
                "0.######",
                CultureInfo.InvariantCulture);
        }

        if (string.Equals(
                columna,
                "cp",
                StringComparison.OrdinalIgnoreCase))
        {
            var digitos =
                new string(
                    valor.Where(char.IsDigit).ToArray());

            if (long.TryParse(
                    digitos,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var codigoPostal))
            {
                return codigoPostal == 0
                    ? string.Empty
                    : codigoPostal
                        .ToString(
                            CultureInfo.InvariantCulture)
                        .PadLeft(5, '0');
            }
        }

        return valor
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .ToUpperInvariant();
    }

    private static bool IntentarConvertirDecimal(string valor, out decimal numero)
    {
        return decimal.TryParse(
                   valor,
                   NumberStyles.Any,
                   CultureInfo.InvariantCulture,
                   out numero)
            || decimal.TryParse(
                   valor,
                   NumberStyles.Any,
                   new CultureInfo("es-MX"),
                   out numero);
    }

    private static bool IntentarConvertirFechaHoraComparacion(string valor, out DateTime fecha)
    {
        var formatos = new[]
        {
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyyMMdd",
        "dd/MM/yyyy HH:mm:ss",
        "d/M/yyyy H:mm:ss",
        "dd-MM-yyyy HH:mm:ss",
        "d-M-yyyy H:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy/MM/dd HH:mm:ss"
    };

        if (DateTime.TryParseExact(
                valor,
                formatos,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out fecha))
        {
            return true;
        }

        if (DateTime.TryParse(
                valor,
                new CultureInfo("es-MX"),
                DateTimeStyles.None,
                out fecha))
        {
            return true;
        }

        if (double.TryParse(
                valor,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var numeroExcel) &&
            numeroExcel > 0 &&
            numeroExcel < 60000)
        {
            try
            {
                fecha =
                    DateTime.FromOADate(numeroExcel);

                return true;
            }
            catch
            {
                fecha = default;
            }
        }

        return false;
    }

    private static bool IntentarConvertirHora(string? valor, out TimeSpan hora)
    {
        hora = default;

        if (string.IsNullOrWhiteSpace(valor))
        {
            return false;
        }

        valor = valor.Trim();

        if (double.TryParse(
                valor,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var fraccionDia) &&
            fraccionDia >= 0 &&
            fraccionDia < 1)
        {
            hora = TimeSpan.FromSeconds(
                Math.Min(
                    86399,
                    Math.Round(fraccionDia * 86400)));

            return true;
        }

        var formatos = new[]
        {
        @"h\:mm",
        @"hh\:mm",
        @"h\:mm\:ss",
        @"hh\:mm\:ss"
    };

        if (TimeSpan.TryParseExact(
                valor,
                formatos,
                CultureInfo.InvariantCulture,
                out hora))
        {
            return true;
        }

        if (DateTime.TryParse(
                valor,
                new CultureInfo("es-MX"),
                DateTimeStyles.None,
                out var fechaHora))
        {
            hora = fechaHora.TimeOfDay;
            return true;
        }

        return false;
    }

    private static ActualizacionDiferenciasResponse PrepararDiferenciasCache(string json, int limitePorSeccion)
    {
        var response = JsonSerializer.Deserialize<ActualizacionDiferenciasResponse>(json) ?? new ActualizacionDiferenciasResponse();
        response.LimitePorSeccion = limitePorSeccion;

        if (limitePorSeccion == 0)
        {
            response.Carpetas.Clear();
            response.Delitos.Clear();
            response.Victimas.Clear();
            response.DetalleLimitado = response.TotalDiferencias > 0;
            return response;
        }

        response.Carpetas = response.Carpetas.Take(limitePorSeccion).ToList();
        response.Delitos = response.Delitos.Take(limitePorSeccion).ToList();
        response.Victimas = response.Victimas.Take(limitePorSeccion).ToList();
        response.DetalleLimitado = response.TotalCarpetas > response.Carpetas.Count || response.TotalDelitos > response.Delitos.Count || response.TotalVictimas > response.Victimas.Count;

        return response;
    }

    private static ActualizacionDiferenciasResumen AgregarDiferenciasSeccion(
        List<ArchivoFila> nuevas,
        List<ArchivoFila> confirmadas,
        IReadOnlyCollection<string> camposIdentificador,
        IReadOnlyCollection<string> columnas,
        List<ActualizacionDiferenciaRegistro> destino,
        int limitePorSeccion,
        bool soloMuestra = false)
    {
        var resumen = new ActualizacionDiferenciasResumen();
        var nuevasPorIdentificador = CrearIndiceDiferencias(nuevas, camposIdentificador);
        var confirmadasPorIdentificador = CrearIndiceDiferencias(confirmadas, camposIdentificador);
        var identificadores = nuevasPorIdentificador.Keys.Union(
            confirmadasPorIdentificador.Keys,
            StringComparer.OrdinalIgnoreCase);
        var campoIdentificador = string.Join("+", camposIdentificador);
        var camposIdentificadorSet = camposIdentificador.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columnasComparacion = columnas
            .Where(columna => !camposIdentificadorSet.Contains(columna))
            .ToArray();

        foreach (var identificador in identificadores)
        {
            if (soloMuestra && limitePorSeccion > 0 && destino.Count >= limitePorSeccion) break;

            nuevasPorIdentificador.TryGetValue(identificador, out var filaNueva);
            confirmadasPorIdentificador.TryGetValue(identificador, out var filaAnterior);

            if (filaAnterior == null && filaNueva != null)
            {
                resumen.Nuevos++;

                if (destino.Count < limitePorSeccion)
                {
                    destino.Add(new ActualizacionDiferenciaRegistro
                    {
                        TipoMovimiento = "NUEVO",
                        CampoIdentificador = campoIdentificador,
                        IdentificadorFiscalia = identificador,
                        CamposModificados = columnas.Select(columna => new ActualizacionCampoDiferencia
                        {
                            Campo = columna,
                            ValorAnterior = null,
                            ValorNuevo = ObtenerValor(filaNueva, columna)
                        }).ToList()
                    });
                }

                continue;
            }

            if (filaAnterior != null && filaNueva == null)
            {
                resumen.Eliminados++;

                if (destino.Count < limitePorSeccion)
                {
                    destino.Add(new ActualizacionDiferenciaRegistro
                    {
                        TipoMovimiento = "ELIMINADO",
                        CampoIdentificador = campoIdentificador,
                        IdentificadorFiscalia = identificador,
                        CamposModificados = columnas.Select(columna => new ActualizacionCampoDiferencia
                        {
                            Campo = columna,
                            ValorAnterior = ObtenerValor(filaAnterior, columna),
                            ValorNuevo = null
                        }).ToList()
                    });
                }

                continue;
            }

            if (filaAnterior == null || filaNueva == null) continue;

            var incluirDetalle = destino.Count < limitePorSeccion;
            var modificado = false;
            var camposModificados = incluirDetalle
                ? new List<ActualizacionCampoDiferencia>()
                : null;

            foreach (var columna in columnasComparacion)
            {
                var valorAnterior = ObtenerValor(filaAnterior, columna);
                var valorNuevo = ObtenerValor(filaNueva, columna);
                var comparacionAnterior = NormalizarValorComparacion(columna, valorAnterior);
                var comparacionNueva = NormalizarValorComparacion(columna, valorNuevo);

                if (string.Equals(comparacionAnterior, comparacionNueva, StringComparison.Ordinal)) continue;

                modificado = true;

                if (incluirDetalle)
                {
                    camposModificados!.Add(new ActualizacionCampoDiferencia
                    {
                        Campo = columna,
                        ValorAnterior = valorAnterior,
                        ValorNuevo = valorNuevo
                    });
                }
            }

            if (!modificado) continue;

            resumen.Modificados++;

            if (incluirDetalle)
            {
                destino.Add(new ActualizacionDiferenciaRegistro
                {
                    TipoMovimiento = "MODIFICADO",
                    CampoIdentificador = campoIdentificador,
                    IdentificadorFiscalia = identificador,
                    CamposModificados = camposModificados!
                });
            }
        }

        return resumen;
    }

    private static Dictionary<string, ArchivoFila> CrearIndiceDiferencias(IEnumerable<ArchivoFila> filas, IReadOnlyCollection<string> camposIdentificador)
    {
        return filas
            .Select(fila => new
            {
                Identificador = CrearIdentificadorDiferencias(fila, camposIdentificador),
                Fila = fila
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Identificador))
            .GroupBy(item => item.Identificador, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.First().Fila, StringComparer.OrdinalIgnoreCase);
    }

    private static string CrearIdentificadorDiferencias(ArchivoFila fila, IEnumerable<string> camposIdentificador)
    {
        return string.Join("|", camposIdentificador.Select(campo => ObtenerValor(fila, campo)?.Trim() ?? string.Empty));
    }

    private static ActualizacionDiferenciasResponse ErrorDiferencias(string codigoReferencia, string mensaje)
    {
        return new ActualizacionDiferenciasResponse
        {
            EsValido = false,
            CodigoReferencia = codigoReferencia,
            Mensaje = mensaje
        };
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

    private static void ValidarRoboVehiculoFormaAccion(List<ArchivoFila> filasDelitos, List<CargaValidacionError> errores)
    {
        var filasRoboVehiculo = filasDelitos
            .Where(fila => ClavesRoboVehiculo.Contains(ObtenerValor(fila, "clasf_de_dto")?.Trim() ?? string.Empty))
            .ToList();

        if (filasRoboVehiculo.Count == 0 ||
            !filasRoboVehiculo.All(fila =>
                int.TryParse(ObtenerValor(fila, "forma_acc")?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var formaAccion) &&
                formaAccion == 3))
        {
            return;
        }

        errores.Add(new CargaValidacionError
        {
            Archivo = "delitos",
            Fila = null,
            Columna = "forma_acc",
            Campo = "forma_acc",
            Valor = "3",
            Codigo = "SEMANAL_ROBO_VEHICULO_FORMA_ACCION_NO_IDENTIFICADA",
            DescripcionResumen = "Robo de vehículo sin forma de acción identificada",
            Mensaje = "Todos los registros de robo de vehículo contienen la clave 3, No identificado, en FORMA_ACC. Debe existir al menos un registro con clave 1, Con violencia, o clave 2, Sin violencia."
        });
    }

    private static void ValidarLesionesDolosasElementoComision(List<ArchivoFila> filasDelitos, List<CargaValidacionError> advertencias)
    {
        var filasLesionesDolosas = filasDelitos
            .Where(fila => string.Equals(ObtenerValor(fila, "clasf_de_dto")?.Trim(), "1.02.01", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filasLesionesDolosas.Count == 0 ||
            !filasLesionesDolosas.All(fila =>
                int.TryParse(ObtenerValor(fila, "emto_com_dto")?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var elementoComision) &&
                ElementosComisionLesionesDolosasAdvertencia.Contains(elementoComision)))
        {
            return;
        }

        advertencias.Add(new CargaValidacionError
        {
            Archivo = "delitos",
            Fila = null,
            Columna = "emto_com_dto",
            Campo = "emto_com_dto",
            Valor = "6, 7 y/o 8",
            Codigo = "SEMANAL_LESIONES_DOLOSAS_ELEMENTO_COMISION_ADVERTENCIA",
            DescripcionResumen = "Lesiones dolosas sin elemento de comisión específico",
            Mensaje = "Todos los registros de lesiones dolosas contienen como elemento de comisión Con otro instrumento, No identificado o No aplica. La carga puede continuar, pero debe revisar la información."
        });
    }

    private static void ValidarHomicidioTentativa(List<ArchivoFila> filasDelitos, List<CargaValidacionError> errores)
    {
        foreach (var fila in filasDelitos)
        {
            var claveModalidad = ObtenerValor(fila, "clasf_de_dto")?.Trim();
            var valorGradoConsumacion = ObtenerValor(fila, "grdo_cons")?.Trim();
            var esHomicidio = !string.IsNullOrWhiteSpace(claveModalidad) &&
                (string.Equals(claveModalidad, "1.01", StringComparison.OrdinalIgnoreCase) ||
                 claveModalidad.StartsWith("1.01.", StringComparison.OrdinalIgnoreCase));

            if (!esHomicidio ||
                !int.TryParse(valorGradoConsumacion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gradoConsumacion) ||
                gradoConsumacion != 2)
            {
                continue;
            }

            errores.Add(new CargaValidacionError
            {
                Archivo = "delitos",
                Fila = fila.NumeroFila,
                Columna = "grdo_cons",
                Campo = "grdo_cons",
                Valor = valorGradoConsumacion,
                Codigo = "SEMANAL_HOMICIDIO_TENTATIVA_NO_PERMITIDO",
                DescripcionResumen = "Homicidio en grado de tentativa no permitido",
                Mensaje = "El módulo preliminar no admite registros de ningún tipo de homicidio en grado de tentativa."
            });
        }
    }

    private static void ValidarHomicidioDolosoGradoConsumacion(List<ArchivoFila> filasDelitos, List<CargaValidacionError> errores)
    {
        foreach (var fila in filasDelitos.Where(EsHomicidioDoloso))
        {
            var valor = ObtenerValor(fila, "grdo_cons")?.Trim();

            if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gradoConsumacion) || gradoConsumacion != 3) continue;

            errores.Add(new CargaValidacionError
            {
                Archivo = "delitos",
                Fila = fila.NumeroFila,
                Columna = "grdo_cons",
                Campo = "grdo_cons",
                Valor = valor,
                Codigo = "SEMANAL_HOMICIDIO_DOLOSO_GRADO_CONSUMACION_NO_IDENTIFICADO",
                DescripcionResumen = "Homicidio doloso con grado de consumación no identificado",
                Mensaje = "Para homicidio doloso el campo GRDO_CONS solo permite la clave 1, Consumado. La clave 3, No identificado, impide la carga."
            });
        }
    }

    private static void ValidarHomicidioDolosoTipoVictima(List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas, List<CargaValidacionError> errores)
    {
        var delitosHomicidioDoloso = filasDelitos.Where(EsHomicidioDoloso).Select(CrearLlaveDelito).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var fila in filasVictimas.Where(fila => delitosHomicidioDoloso.Contains(CrearLlaveDelito(fila))))
        {
            var valor = ObtenerValor(fila, "id_tv")?.Trim();

            if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tipoVictima) || tipoVictima == 1) continue;

            errores.Add(new CargaValidacionError
            {
                Archivo = "victimas",
                Fila = fila.NumeroFila,
                Columna = "id_tv",
                Campo = "id_tv",
                Valor = valor,
                Codigo = "SEMANAL_HOMICIDIO_DOLOSO_TIPO_VICTIMA_NO_PERMITIDO",
                DescripcionResumen = "Tipo de víctima no permitido para homicidio doloso",
                Mensaje = "Para víctimas relacionadas con homicidio doloso el campo ID_TV solo permite la clave 1, Persona física."
            });
        }
    }

    private static void ValidarHomicidioDolosoResumenHechos(List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<CargaValidacionError> advertencias)
    {
        var carpetasHomicidioDoloso = filasDelitos.Where(EsHomicidioDoloso).Select(fila => ObtenerValor(fila, "id_ci")?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var fila in filasCarpetas.Where(fila => carpetasHomicidioDoloso.Contains(ObtenerValor(fila, "id_ci")?.Trim() ?? string.Empty) && string.IsNullOrWhiteSpace(ObtenerValor(fila, "rmen_de_hchos"))))
        {
            advertencias.Add(new CargaValidacionError
            {
                Archivo = "carpetas",
                Fila = fila.NumeroFila,
                Columna = "rmen_de_hchos",
                Campo = "rmen_de_hchos",
                Valor = ObtenerValor(fila, "rmen_de_hchos"),
                Codigo = "SEMANAL_HOMICIDIO_DOLOSO_RESUMEN_HECHOS_VACIO_ADVERTENCIA",
                DescripcionResumen = "Homicidio doloso sin resumen de hechos",
                Mensaje = "El campo RMEN_DE_HCHOS está vacío en una carpeta relacionada con homicidio doloso. La carga puede continuar, pero debe revisar la información."
            });
        }
    }

    private static bool EsHomicidioDoloso(ArchivoFila fila) => string.Equals(ObtenerValor(fila, "clasf_de_dto")?.Trim(), "1.01.01", StringComparison.OrdinalIgnoreCase);

    private static string CrearLlaveDelito(ArchivoFila fila)
    {
        var idCi = ObtenerValor(fila, "id_ci")?.Trim();
        var idDelito = ObtenerValor(fila, "id_delito")?.Trim();
        return string.IsNullOrWhiteSpace(idCi) || string.IsNullOrWhiteSpace(idDelito) ? string.Empty : $"{idCi}|{idDelito}";
    }

    private static void NormalizarColumnasExtorsion(List<ArchivoFila> filasCarpetas)
    {
        foreach (var fila in filasCarpetas)
        {
            CopiarColumnaAlias(fila, "denuncia_anonima", "se_inicio_por_denuncia_anonima");
            CopiarColumnaAlias(fila, "denuncia_anonima_089", "se_inicio_por_denuncia_anonima_a_travez_del_0_89", "se_inicio_por_denuncia_anonima_a_traves_del_0_89", "se_inicio_por_denuncia_anonima_a_travez_del_089", "se_inicio_por_denuncia_anonima_a_traves_del_089");
            CopiarColumnaAlias(fila, "denuncia_anonima_otro_medio", "o_se_inicio_por_otro_medio_denuncia_anonima_especifique", "se_inicio_por_otro_medio_denuncia_anonima_especifique");

            foreach (var columna in ColumnasDenunciaAnonima)
            {
                if (!fila.Columnas.TryGetValue(columna, out var valor) || string.IsNullOrWhiteSpace(valor)) continue;
                fila.Columnas[columna] = NormalizarValorDenunciaAnonima(valor);
            }
        }
    }

    private static void CopiarColumnaAlias(ArchivoFila fila, string columnaDestino, params string[] alias)
    {
        if (fila.Columnas.ContainsKey(columnaDestino)) return;

        foreach (var columnaAlias in alias)
        {
            if (!fila.Columnas.TryGetValue(columnaAlias, out var valor)) continue;
            fila.Columnas[columnaDestino] = valor;
            return;
        }
    }

    private static string NormalizarValorDenunciaAnonima(string valor)
    {
        var valorLimpio = valor.Trim();

        if (string.Equals(valorLimpio, "SI", StringComparison.OrdinalIgnoreCase) || string.Equals(valorLimpio, "SÍ", StringComparison.OrdinalIgnoreCase)) return "SI";
        if (string.Equals(valorLimpio, "NO", StringComparison.OrdinalIgnoreCase)) return "NO";
        if (string.Equals(valorLimpio, "SE DESCONOCE", StringComparison.OrdinalIgnoreCase)) return "SE DESCONOCE";

        return valorLimpio;
    }

    private static void NormalizarGradoConsumacionExtorsion(List<ArchivoFila> filasDelitos)
    {
        foreach (var fila in filasDelitos.Where(EsDelitoExtorsion))
        {
            if (string.IsNullOrWhiteSpace(ObtenerValor(fila, "grdo_cons"))) fila.Columnas["grdo_cons"] = "3";
        }
    }

    private static void ValidarExtorsionDenunciaAnonima(List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<CargaValidacionError> errores)
    {
        var idCarpetasExtorsion = filasDelitos
            .Where(EsDelitoExtorsion)
            .Select(fila => ObtenerValor(fila, "id_ci")?.Trim())
            .Where(idCi => !string.IsNullOrWhiteSpace(idCi))
            .Select(idCi => idCi!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (idCarpetasExtorsion.Count == 0) return;

        var columnasArchivo = filasCarpetas.SelectMany(fila => fila.Columnas.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var columnasFaltantes = ColumnasDenunciaAnonima.Where(columna => !columnasArchivo.Contains(columna)).ToList();

        foreach (var columna in columnasFaltantes)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = "carpetas",
                Fila = 1,
                Columna = columna,
                Campo = columna,
                Codigo = "SEMANAL_EXTORSION_COLUMNA_OBLIGATORIA_NO_ENCONTRADA",
                DescripcionResumen = "Columna de extorsión no encontrada",
                Mensaje = $"El archivo de carpetas contiene delitos de extorsión y no incluye la columna obligatoria \"{columna}\"."
            });
        }

        if (columnasFaltantes.Count > 0) return;

        var carpetasPorId = filasCarpetas
            .Select(fila => new { Fila = fila, IdCi = ObtenerValor(fila, "id_ci")?.Trim() })
            .Where(item => !string.IsNullOrWhiteSpace(item.IdCi))
            .GroupBy(item => item.IdCi!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.First().Fila, StringComparer.OrdinalIgnoreCase);

        foreach (var idCi in idCarpetasExtorsion.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            if (!carpetasPorId.TryGetValue(idCi, out var carpeta)) continue;

            var denunciaAnonima = ObtenerValor(carpeta, "denuncia_anonima")?.Trim();
            var denunciaAnonima089 = ObtenerValor(carpeta, "denuncia_anonima_089")?.Trim();
            var denunciaAnonimaOtroMedio = ObtenerValor(carpeta, "denuncia_anonima_otro_medio")?.Trim();
            var valores = new Dictionary<string, string?>
            {
                ["denuncia_anonima"] = denunciaAnonima,
                ["denuncia_anonima_089"] = denunciaAnonima089,
                ["denuncia_anonima_otro_medio"] = denunciaAnonimaOtroMedio
            };

            foreach (var valor in valores.Where(item => string.IsNullOrWhiteSpace(item.Value)))
            {
                errores.Add(new CargaValidacionError
                {
                    Archivo = "carpetas",
                    Fila = carpeta.NumeroFila,
                    Columna = valor.Key,
                    Campo = valor.Key,
                    Valor = valor.Value,
                    Codigo = "SEMANAL_EXTORSION_DENUNCIA_ANONIMA_SIN_INFORMACION",
                    DescripcionResumen = "Variable de denuncia anónima sin información",
                    Mensaje = $"La carpeta {idCi} contiene un delito de extorsión y el campo {valor.Key} es obligatorio."
                });
            }

            if (valores.Any(item => string.IsNullOrWhiteSpace(item.Value))) continue;

            var respuestasCerradasValidas = true;

            foreach (var valor in valores.Where(item => item.Key != "denuncia_anonima_otro_medio" && !ValoresControlDenunciaAnonima.Contains(item.Value!)))
            {
                respuestasCerradasValidas = false;

                errores.Add(new CargaValidacionError
                {
                    Archivo = "carpetas",
                    Fila = carpeta.NumeroFila,
                    Columna = valor.Key,
                    Campo = valor.Key,
                    Valor = valor.Value,
                    Codigo = "SEMANAL_EXTORSION_DENUNCIA_ANONIMA_VALOR_INVALIDO",
                    DescripcionResumen = "Valor de denuncia anónima inválido",
                    Mensaje = $"El campo {valor.Key} únicamente admite SI, NO o SE DESCONOCE."
                });
            }

            if (denunciaAnonimaOtroMedio!.Length > 500)
            {
                errores.Add(new CargaValidacionError
                {
                    Archivo = "carpetas",
                    Fila = carpeta.NumeroFila,
                    Columna = "denuncia_anonima_otro_medio",
                    Campo = "denuncia_anonima_otro_medio",
                    Valor = denunciaAnonimaOtroMedio,
                    Codigo = "SEMANAL_EXTORSION_OTRO_MEDIO_LONGITUD_EXCEDIDA",
                    DescripcionResumen = "Otro medio con longitud excedida",
                    Mensaje = "El campo denuncia_anonima_otro_medio excede la longitud máxima permitida de 500 caracteres."
                });

                continue;
            }

            if (!respuestasCerradasValidas) continue;

            var combinacionValida =
                denunciaAnonima == "SI" && denunciaAnonima089 == "SI" && denunciaAnonimaOtroMedio == "NO" ||
                denunciaAnonima == "SI" && denunciaAnonima089 == "NO" && !ValoresControlDenunciaAnonima.Contains(denunciaAnonimaOtroMedio) ||
                denunciaAnonima == "NO" && denunciaAnonima089 == "NO" && denunciaAnonimaOtroMedio == "NO" ||
                denunciaAnonima == "SE DESCONOCE" && denunciaAnonima089 == "SE DESCONOCE" && denunciaAnonimaOtroMedio == "SE DESCONOCE";

            if (combinacionValida) continue;

            errores.Add(new CargaValidacionError
            {
                Archivo = "carpetas",
                Fila = carpeta.NumeroFila,
                Columna = "denuncia_anonima",
                Campo = "denuncia_anonima",
                Valor = $"{denunciaAnonima} | {denunciaAnonima089} | {denunciaAnonimaOtroMedio}",
                Codigo = "SEMANAL_EXTORSION_DENUNCIA_ANONIMA_COMBINACION_INVALIDA",
                DescripcionResumen = "Combinación de denuncia anónima inválida",
                Mensaje = $"La combinación de denuncia anónima de la carpeta {idCi} no es válida. Use SI/SI/NO, SI/NO/[medio especificado], NO/NO/NO o SE DESCONOCE/SE DESCONOCE/SE DESCONOCE."
            });
        }
    }

    private static bool EsDelitoExtorsion(ArchivoFila fila)
    {
        var clasificacion = ObtenerValor(fila, "clasf_de_dto")?.Trim();
        return !string.IsNullOrWhiteSpace(clasificacion) && ClavesExtorsion.Contains(clasificacion);
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

            if (string.IsNullOrWhiteSpace(identificador) || identificador.Length <= 250) continue;

            errores.Add(new CargaValidacionError
            {
                Archivo = "delitos",
                Fila = fila.NumeroFila,
                Columna = "id_delito",
                Campo = "id_delito",
                Valor = identificador,
                Codigo = "SEMANAL_ID_DELITO_LONGITUD_EXCEDIDA",
                DescripcionResumen = "Identificador de delito con longitud excedida",
                Mensaje = "El campo id_delito excede la longitud máxima semanal de 250 caracteres."
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

        if (entidades.Count == 0)
        {
            AgregarErrorGeneral(errores, "SEMANAL_ENTIDAD_NO_DETECTADA", "No se pudo determinar la entidad de la carga", "No se pudo determinar la entidad federativa a partir del archivo de delitos.", "id_ent_hchos", null);
            return null;
        }

        if (entidades.Count > 1)
        {
            AgregarErrorGeneral(errores, "SEMANAL_ENTIDADES_MULTIPLES", "La carga contiene múltiples entidades", "Una operación semanal solo puede corresponder a una entidad federativa.", "id_ent_hchos", string.Join(", ", entidades.OrderBy(x => x)));
            return null;
        }

        var idEntidadExcel = entidades.First();

        return idEntidadExcel;
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

        resumen.AddRange(response.Errores.Where(x => !string.IsNullOrWhiteSpace(x.Codigo) && !string.IsNullOrWhiteSpace(x.DescripcionResumen)).GroupBy(x => new { x.Archivo, x.Codigo, x.DescripcionResumen }).Select(x => new CargaValidacionResumenItem { Archivo = x.Key.Archivo, Codigo = x.Key.Codigo, Descripcion = x.Key.DescripcionResumen, TotalRegistros = x.Sum(y => y.TotalRegistrosAfectados), EsError = true }).OrderBy(x => x.Archivo).ThenBy(x => x.Codigo));
        resumen.AddRange(response.Advertencias.Where(x => !string.IsNullOrWhiteSpace(x.Codigo) && !string.IsNullOrWhiteSpace(x.DescripcionResumen)).GroupBy(x => new { x.Archivo, x.Codigo, x.DescripcionResumen }).Select(x => new CargaValidacionResumenItem { Archivo = x.Key.Archivo, Codigo = x.Key.Codigo, Descripcion = x.Key.DescripcionResumen, TotalRegistros = x.Sum(y => y.TotalRegistrosAfectados), EsError = false }).OrderBy(x => x.Archivo).ThenBy(x => x.Codigo));
        response.ResumenValidacion = resumen;

        var esActualizacion = string.Equals(response.TipoCarga, "ACTUALIZACION", StringComparison.OrdinalIgnoreCase);
        var operacion = esActualizacion ? "actualización" : "carga";

        response.Mensaje = response.EsValido
            ? response.Advertencias.Count > 0
                ? $"La {operacion} semanal fue validada con advertencias. Revise el detalle antes de continuar."
                : $"La {operacion} semanal fue validada correctamente. Puede confirmarla."
            : $"La {operacion} semanal contiene errores de validación.";
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
