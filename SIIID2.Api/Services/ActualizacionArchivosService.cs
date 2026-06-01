using System.Globalization;
using System.Text;
using SIIID2.Api.Models;
using SIIID2.Api.Readers;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;

// Actualización:
// - Se usa /api/actualizaciones/validar.
// - El periodo lo selecciona el usuario en el front.
// - El periodo llega como campos Text dentro del form-data:
//      mesCorte
//      anioCorte
// - Solo procede si ya existe una carga inicial confirmada para ese periodo.
public class ActualizacionArchivosService : IActualizacionArchivosService
{
    private readonly IArchivoReader _archivoReader;
    private readonly CarpetasValidator _carpetasValidator;
    private readonly DelitosValidator _delitosValidator;
    private readonly VictimasValidator _victimasValidator;
    private readonly CargaIntegridadValidator _cargaIntegridadValidator;
    private readonly CatalogosValidator _catalogosValidator;
    private readonly ICargaRepository _cargaRepository;
    private readonly IActualizacionDiferenciasRepository _actualizacionDiferenciasRepository;
    private readonly IActualizacionRepository _actualizacionRepository;
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IActualizacionCargaRepository _actualizacionCargaRepository;

    // Extensiones permitidas para los archivos de actualización.
    private readonly string[] _extensionesPermitidas =
    {
        ".csv",
        ".xlsx"
    };

    // Tamaño máximo permitido por archivo: 50 MB.
    private const long TamanioMaximoBytes = 50 * 1024 * 1024;

    public ActualizacionArchivosService(
        IArchivoReader archivoReader,
        CarpetasValidator carpetasValidator,
        DelitosValidator delitosValidator,
        VictimasValidator victimasValidator,
        CargaIntegridadValidator cargaIntegridadValidator,
        CatalogosValidator catalogosValidator,
        ICargaRepository cargaRepository,
        IActualizacionCargaRepository actualizacionCargaRepository,
        IActualizacionDiferenciasRepository actualizacionDiferenciasRepository,
        IActualizacionRepository actualizacionRepository,
        IUsuarioRepository usuarioRepository)
    {
        _archivoReader = archivoReader;
        _carpetasValidator = carpetasValidator;
        _delitosValidator = delitosValidator;
        _victimasValidator = victimasValidator;
        _cargaIntegridadValidator = cargaIntegridadValidator;
        _catalogosValidator = catalogosValidator;
        _cargaRepository = cargaRepository;
        _actualizacionCargaRepository = actualizacionCargaRepository;
        _actualizacionDiferenciasRepository = actualizacionDiferenciasRepository;
        _actualizacionRepository = actualizacionRepository;
        _usuarioRepository = usuarioRepository;
    }

    public async Task<CargaValidacionResponse> ValidarActualizacionAsync(IFormCollection form, int idUsuarioCarga)
    {
        // Los archivos y campos adicionales vienen en multipart/form-data.
        var archivos = form.Files;

        // El usuario viene del token.
        // Se consulta en base para validar que exista, esté activo y tenga permisos.
        var usuarioCarga = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioCarga);

        if (usuarioCarga == null)
        {
            var responseUsuario = new CargaValidacionResponse
            {
                CodigoReferencia = GenerarCodigoReferencia()
            };

            responseUsuario.Errores.Add(new CargaValidacionError
            {
                Archivo = "general",
                Fila = null,
                Columna = "idUsuarioCarga",
                Campo = "idUsuarioCarga",
                Valor = idUsuarioCarga.ToString(),
                Codigo = "GENERAL_USUARIO_CARGA_NO_EXISTE",
                DescripcionResumen = "Usuario de carga no existe",
                Mensaje = "El usuario autenticado no existe o no está activo."
            });

            FinalizarRespuesta(responseUsuario, 0, 0, 0);
            return responseUsuario;
        }

        // Para actualización se revisa habilita_modificacion.
        // No se usa habilita_carga porque son permisos distintos.
        if (!usuarioCarga.HabilitaModificacion)
        {
            var responseUsuario = new CargaValidacionResponse
            {
                CodigoReferencia = GenerarCodigoReferencia()
            };

            responseUsuario.Errores.Add(new CargaValidacionError
            {
                Archivo = "general",
                Fila = null,
                Columna = "habilita_modificacion",
                Campo = "habilita_modificacion",
                Valor = usuarioCarga.HabilitaModificacion.ToString(),
                Codigo = "GENERAL_USUARIO_SIN_PERMISO_MODIFICACION",
                DescripcionResumen = "Usuario sin permiso de modificación",
                Mensaje = "El usuario autenticado no tiene habilitada la modificación de información."
            });

            FinalizarRespuesta(responseUsuario, 0, 0, 0);
            return responseUsuario;
        }

        // Se genera un código nuevo por cada intento de actualización.
        // Este código se usará para acuse previo, confirmación y trazabilidad.
        var response = new CargaValidacionResponse
        {
            CodigoReferencia = GenerarCodigoReferencia()
        };

        // En actualización, el periodo no se infiere del Excel.
        // El usuario selecciona mes/año en la pantalla de actualización.
        // Esos valores llegan como campos Text dentro del form-data:
        // - mesCorte
        // - anioCorte
        var periodo = ObtenerPeriodoCorteDesdeForm(form, response.Errores);
        var mesCorte = periodo.MesCorte;
        var anioCorte = periodo.AnioCorte;

        // Validación base: deben llegar los tres archivos.
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

        // Validamos tamaño y extensión de cada archivo.
        foreach (var archivo in listaArchivos)
        {
            ValidarArchivoBase(archivo, response.Errores);
        }

        // Se localizan los archivos por nombre real, no por la key del form-data.
        // Esto permite que el front mande keys simples como carpetas/delitos/victimas,
        // pero el sistema sigue buscando por el nombre del archivo.
        var archivoCarpetas = BuscarArchivoPorNombre(listaArchivos, "carpeta");
        var archivoDelitos = BuscarArchivoPorNombre(listaArchivos, "delito");
        var archivoVictimas = BuscarArchivoPorNombre(listaArchivos, "victima");

        // Validamos que exista un archivo de cada tipo.
        ValidarArchivoEsperado(archivoCarpetas, "carpetas", "carpeta", response.Errores);
        ValidarArchivoEsperado(archivoDelitos, "delitos", "delito", response.Errores);
        ValidarArchivoEsperado(archivoVictimas, "victimas", "victima", response.Errores);

        // Validamos que no manden dos archivos que parezcan ser del mismo tipo.
        ValidarDuplicadosPorTipo(listaArchivos, "carpeta", "carpetas", response.Errores);
        ValidarDuplicadosPorTipo(listaArchivos, "delito", "delitos", response.Errores);
        ValidarDuplicadosPorTipo(listaArchivos, "victima", "victimas", response.Errores);

        // Si hubo errores generales de archivos, no intentamos leerlos.
        if (response.Errores.Count > 0)
        {
            FinalizarRespuesta(response, 0, 0, 0);
            return response;
        }

        // Leemos los tres archivos y los convertimos a filas genéricas.
        var filasCarpetas = await _archivoReader.LeerAsync(archivoCarpetas!);
        var filasDelitos = await _archivoReader.LeerAsync(archivoDelitos!);
        var filasVictimas = await _archivoReader.LeerAsync(archivoVictimas!);

        // Validaciones individuales por archivo.
        response.Errores.AddRange(_carpetasValidator.Validar(filasCarpetas, validarMesInmediatoAnterior: false));
        response.Errores.AddRange(_delitosValidator.Validar(filasDelitos));
        response.Errores.AddRange(_victimasValidator.Validar(filasVictimas));

        // Validamos que el usuario solo actualice información de su entidad.
        // El SUPER_USUARIO puede actualizar cualquier entidad.
        response.Errores.AddRange(ValidarEntidadUsuarioCarga(usuarioCarga, filasDelitos));

        // Si el periodo recibido desde el front fue válido,
        // validamos que las carpetas del Excel pertenezcan al periodo seleccionado.
        //
        // Ejemplo:
        // mesCorte = 1
        // anioCorte = 2026
        //
        // Todas las fha_de_ini de carpetas deben corresponder al mes de información
        // asociado al corte seleccionado
        if (mesCorte.HasValue && anioCorte.HasValue)
        {
            response.Errores.AddRange(ValidarPeriodoCarpetas(
                filasCarpetas,
                mesCorte.Value,
                anioCorte.Value));
        }

        // Las validaciones cruzadas solo se ejecutan si hasta aquí no hay errores.
        // Esto evita errores secundarios cuando la estructura base del archivo está mal.
        var sinErroresInternos = response.Errores.Count == 0;

        if (sinErroresInternos)
        {
            response.Errores.AddRange(_cargaIntegridadValidator.Validar(
                filasCarpetas,
                filasDelitos,
                filasVictimas));
        }

        // Validaciones contra catálogos.
        // Se mantienen igual que en carga normal.
        response.Errores.AddRange(await _catalogosValidator.ValidarAsync(
            filasCarpetas,
            filasDelitos,
            filasVictimas));

        // Obtenemos la entidad real de la actualización.
        // Para usuario normal viene de su usuario.
        // Para SUPER_USUARIO se toma del archivo de delitos.
        var idEntidadFederativaCarga = ObtenerEntidadFederativaCarga(
            usuarioCarga,
            filasDelitos,
            response.Errores);

        // Si no hay errores y ya tenemos entidad/periodo,
        // revisamos que exista una carga inicial confirmada para ese corte.
        if (idEntidadFederativaCarga.HasValue && mesCorte.HasValue && anioCorte.HasValue)
        {
            var existeCargaConfirmada = await _cargaRepository.ExisteCargaConfirmadaAsync(
                idEntidadFederativaCarga.Value,
                mesCorte.Value,
                anioCorte.Value);

            if (!existeCargaConfirmada)
            {
                response.Errores.Add(new CargaValidacionError
                {
                    Archivo = "general",
                    Fila = null,
                    Columna = "",
                    Campo = "",
                    Valor = $"{mesCorte.Value:00}/{anioCorte.Value}",
                    Codigo = "ACTUALIZACION_SIN_CARGA_CONFIRMADA",
                    DescripcionResumen = "No existe carga confirmada",
                    Mensaje = $"No existe una carga confirmada asociada al periodo {mesCorte.Value:00}/{anioCorte.Value} para la entidad {idEntidadFederativaCarga.Value}. Primero debe existir una carga inicial confirmada para ese mes/año."
                });
            }
            else
            {
                var codigoActualizacionPendiente = await _cargaRepository.ObtenerCodigoActualizacionPendienteAsync(
                    idEntidadFederativaCarga.Value,
                    mesCorte.Value,
                    anioCorte.Value);

                if (!string.IsNullOrWhiteSpace(codigoActualizacionPendiente))
                {
                    response.Errores.Add(new CargaValidacionError
                    {
                        Archivo = "general",
                        Fila = null,
                        Columna = "",
                        Campo = "",
                        Valor = codigoActualizacionPendiente,
                        Codigo = "ACTUALIZACION_PENDIENTE_EXISTENTE",
                        DescripcionResumen = "Ya existe actualización pendiente",
                        Mensaje = $"Ya existe una actualización validada pendiente de confirmar para la entidad {idEntidadFederativaCarga.Value} y periodo {mesCorte.Value:00}/{anioCorte.Value}. Código de referencia pendiente: {codigoActualizacionPendiente}. Debe confirmar o rechazar esa actualización antes de enviar una nueva."
                    });
                }
            }
        }

        // Construimos resumen y mensaje final.
        FinalizarRespuesta(
            response,
            filasCarpetas.Count,
            filasDelitos.Count,
            filasVictimas.Count);

        // Estos errores bloquean el intento de actualización.
        // No se guarda fila en carga ni staging para evitar ensuciar la base.
        //
        // Si el periodo viene inválido, no se debe guardar la carga con mes/anio en 0.
        // Si no existe carga confirmada o ya hay una actualización pendiente,
        // tampoco se guarda staging.
        if (response.Errores.Any(x =>
                x.Codigo == "GENERAL_USUARIO_SIN_ENTIDAD" ||
                x.Codigo == "DELITOS_ENTIDAD_NO_CORRESPONDE_USUARIO" ||
                x.Codigo == "ACTUALIZACION_MES_CORTE_OBLIGATORIO" ||
                x.Codigo == "ACTUALIZACION_MES_CORTE_INVALIDO" ||
                x.Codigo == "ACTUALIZACION_ANIO_CORTE_OBLIGATORIO" ||
                x.Codigo == "ACTUALIZACION_ANIO_CORTE_INVALIDO" ||
                x.Codigo == "ACTUALIZACION_SIN_CARGA_CONFIRMADA" ||
                x.Codigo == "ACTUALIZACION_PENDIENTE_EXISTENTE"))
        {
            return response;
        }

        // Si la validación fue correcta, queda pendiente de confirmación.
        // Si tuvo errores normales de validación, se guarda como rechazado de validación.
        var estadoCarga = response.EsValido
            ? "VALIDADO_PENDIENTE_ACTUALIZACION"
            : "RECHAZADO_VALIDACION_ACTUALIZACION";

        var mensajeError = response.EsValido
            ? null
            : $"La actualización contiene errores de validación. Total de errores: {response.Errores.Count}.";

        // Guardamos el intento de actualización.
        // Esto crea una fila en carga con tipo_carga = ACTUALIZACION
        // y guarda los tres archivos en staging.
        //
        // Solo se llega aquí si:
        // - no hay error bloqueante de periodo sin carga confirmada
        // - no hay actualización pendiente existente
        var idCargaActualizacion = await _actualizacionCargaRepository.GuardarIntentoActualizacionAsync(
            idUsuarioCarga,
            idEntidadFederativaCarga,
            response.CodigoReferencia,
            mesCorte ?? 0,
            anioCorte ?? 0,
            filasCarpetas.Count,
            filasDelitos.Count,
            filasVictimas.Count,
            estadoCarga,
            mensajeError,
            filasCarpetas,
            filasDelitos,
            filasVictimas);

        if (response.EsValido)
        {
            var resumenDiferencias = await _actualizacionCargaRepository.ObtenerResumenDiferenciasActualizacionAsync(idCargaActualizacion);

            response.ResumenValidacion.AddRange(resumenDiferencias);

            response.Mensaje = "Actualización validada correctamente. Revise las diferencias antes de confirmar la actualización.";
        }

        return response;
    }

    private static (int? MesCorte, int? AnioCorte) ObtenerPeriodoCorteDesdeForm(IFormCollection form, List<CargaValidacionError> errores)
    {
        // Lee mesCorte/anioCorte desde form-data.
        //
        // Se usan nullable int porque si falta o viene inválido,
        // se agrega error pero el flujo puede seguir acumulando más errores.
        int? mesCorte = null;
        int? anioCorte = null;

        var valorMes = form.TryGetValue("mesCorte", out var mesValues)
            ? mesValues.FirstOrDefault()
            : null;

        var valorAnio = form.TryGetValue("anioCorte", out var anioValues)
            ? anioValues.FirstOrDefault()
            : null;

        if (string.IsNullOrWhiteSpace(valorMes))
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = "general",
                Fila = null,
                Columna = "mesCorte",
                Campo = "mesCorte",
                Valor = null,
                Codigo = "ACTUALIZACION_MES_CORTE_OBLIGATORIO",
                DescripcionResumen = "Mes de corte obligatorio",
                Mensaje = "Debe enviar el mes de corte que desea actualizar."
            });
        }
        else if (!int.TryParse(valorMes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mesParseado) ||
                 mesParseado < 1 ||
                 mesParseado > 12)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = "general",
                Fila = null,
                Columna = "mesCorte",
                Campo = "mesCorte",
                Valor = valorMes,
                Codigo = "ACTUALIZACION_MES_CORTE_INVALIDO",
                DescripcionResumen = "Mes de corte inválido",
                Mensaje = "El mes de corte debe ser un número entre 1 y 12."
            });
        }
        else
        {
            mesCorte = mesParseado;
        }

        if (string.IsNullOrWhiteSpace(valorAnio))
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = "general",
                Fila = null,
                Columna = "anioCorte",
                Campo = "anioCorte",
                Valor = null,
                Codigo = "ACTUALIZACION_ANIO_CORTE_OBLIGATORIO",
                DescripcionResumen = "Año de corte obligatorio",
                Mensaje = "Debe enviar el año de corte que desea actualizar."
            });
        }
        else if (!int.TryParse(valorAnio, NumberStyles.Integer, CultureInfo.InvariantCulture, out var anioParseado) ||
                 anioParseado < 2000 ||
                 anioParseado > 2100)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = "general",
                Fila = null,
                Columna = "anioCorte",
                Campo = "anioCorte",
                Valor = valorAnio,
                Codigo = "ACTUALIZACION_ANIO_CORTE_INVALIDO",
                DescripcionResumen = "Año de corte inválido",
                Mensaje = "El año de corte debe ser un número válido entre 2000 y 2100."
            });
        }
        else
        {
            anioCorte = anioParseado;
        }

        return (mesCorte, anioCorte);
    }

    private static List<CargaValidacionError> ValidarPeriodoCarpetas(List<ArchivoFila> filasCarpetas, int mesCorte, int anioCorte)
    {
        var errores = new List<CargaValidacionError>();

        // En actualización, el usuario selecciona el mes/año de corte.
        // Pero los archivos contienen información del mes inmediato anterior.
        //
        // Ejemplo:
        // mesCorte = 5, anioCorte = 2026
        // Corte: mayo 2026
        // Información esperada en el Excel: abril 2026.
        var periodoInformacion = ObtenerPeriodoInformacionDesdeCorte(mesCorte, anioCorte);

        foreach (var fila in filasCarpetas)
        {
            fila.Columnas.TryGetValue("fha_de_ini", out var valorFecha);

            // Si viene vacío, lo reporta CarpetasValidator.
            if (string.IsNullOrWhiteSpace(valorFecha))
            {
                continue;
            }

            // Si viene con formato inválido, lo reporta CarpetasValidator.
            if (!DateTime.TryParse(valorFecha, new CultureInfo("es-MX"), DateTimeStyles.None, out var fecha))
            {
                continue;
            }

            if (fecha.Month == periodoInformacion.Mes && fecha.Year == periodoInformacion.Anio)
            {
                continue;
            }

            errores.Add(new CargaValidacionError
            {
                Archivo = "carpetas",
                Fila = fila.NumeroFila,
                Columna = "fha_de_ini",
                Campo = "fha_de_ini",
                Valor = valorFecha,
                Codigo = "CARPETAS_FECHA_FUERA_PERIODO_ACTUALIZACION",
                DescripcionResumen = "Fecha fuera del periodo de información",
                Mensaje = $"La fecha de inicio de la carpeta ({valorFecha}) no corresponde al periodo de información esperado {periodoInformacion.Mes:00}/{periodoInformacion.Anio} para el corte {mesCorte:00}/{anioCorte}."
            });
        }

        return errores;
    }

    private int? ObtenerEntidadFederativaCarga(UsuarioCargaInfo usuarioCarga, List<ArchivoFila> filasDelitos, List<CargaValidacionError> errores)
    {
        // Para usuarios normales, la entidad de actualización es la entidad asignada al usuario.
        if (!usuarioCarga.EsSuperUsuario)
        {
            return usuarioCarga.IdEntidadFederativa;
        }

        // Para SUPER_USUARIO, se obtiene desde el archivo de delitos.
        // La actualización solo puede corresponder a una entidad.
        var entidades = new HashSet<int>();

        foreach (var fila in filasDelitos)
        {
            fila.Columnas.TryGetValue("id_ent_hchos", out var valorEntidad);

            if (string.IsNullOrWhiteSpace(valorEntidad))
            {
                continue;
            }

            valorEntidad = valorEntidad.Trim();

            if (!int.TryParse(valorEntidad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idEntidad))
            {
                continue;
            }

            entidades.Add(idEntidad);
        }

        if (entidades.Count == 0)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = "delitos",
                Fila = null,
                Columna = "id_ent_hchos",
                Campo = "id_ent_hchos",
                Valor = null,
                Codigo = "DELITOS_ENTIDAD_CARGA_NO_DETECTADA",
                DescripcionResumen = "No se pudo determinar la entidad de la carga",
                Mensaje = "No se pudo determinar la entidad de la carga a partir del archivo de delitos."
            });

            return null;
        }

        if (entidades.Count > 1)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = "delitos",
                Fila = null,
                Columna = "id_ent_hchos",
                Campo = "id_ent_hchos",
                Valor = string.Join(", ", entidades.OrderBy(x => x)),
                Codigo = "DELITOS_ENTIDADES_MULTIPLES_CARGA",
                DescripcionResumen = "La carga contiene múltiples entidades",
                Mensaje = "La actualización contiene delitos de más de una entidad federativa. Una actualización solo puede corresponder a una entidad."
            });

            return null;
        }

        return entidades.First();
    }

    private List<CargaValidacionError> ValidarEntidadUsuarioCarga(UsuarioCargaInfo usuarioCarga, List<ArchivoFila> filasDelitos)
    {
        var errores = new List<CargaValidacionError>();

        // SUPER_USUARIO puede actualizar cualquier entidad.
        if (usuarioCarga.EsSuperUsuario)
        {
            return errores;
        }

        // Usuario normal debe tener entidad asignada.
        if (!usuarioCarga.IdEntidadFederativa.HasValue)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = "general",
                Fila = null,
                Columna = "id_ent_hchos",
                Campo = "id_ent_hchos",
                Valor = null,
                Codigo = "GENERAL_USUARIO_SIN_ENTIDAD",
                DescripcionResumen = "Usuario sin entidad federativa asignada",
                Mensaje = "El usuario no tiene una entidad federativa asignada y no puede realizar actualizaciones."
            });

            return errores;
        }

        // Usuario normal solo puede actualizar delitos de su entidad.
        foreach (var fila in filasDelitos)
        {
            fila.Columnas.TryGetValue("id_ent_hchos", out var valorEntidad);

            // Si viene vacío, lo reporta DelitosValidator.
            if (string.IsNullOrWhiteSpace(valorEntidad))
            {
                continue;
            }

            valorEntidad = valorEntidad.Trim();

            // Si no se puede convertir, lo reportan otros validadores.
            if (!int.TryParse(valorEntidad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idEntidadExcel))
            {
                continue;
            }

            // Acepta valores tipo 9 y 09 porque ambos se convierten a 9.
            if (idEntidadExcel == usuarioCarga.IdEntidadFederativa.Value)
            {
                continue;
            }

            errores.Add(new CargaValidacionError
            {
                Archivo = "delitos",
                Fila = fila.NumeroFila,
                Columna = "id_ent_hchos",
                Campo = "id_ent_hchos",
                Valor = valorEntidad,
                Codigo = "DELITOS_ENTIDAD_NO_CORRESPONDE_USUARIO",
                DescripcionResumen = "Entidad federativa no corresponde al usuario",
                Mensaje = $"La entidad del delito ({valorEntidad}) no corresponde con la entidad asignada al usuario ({usuarioCarga.IdEntidadFederativa.Value})."
            });
        }

        return errores;
    }

    private void ValidarArchivoBase(IFormFile archivo, List<CargaValidacionError> errores)
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

        // Archivo mayor a 50 MB.
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

        // Extensión permitida.
        var extension = Path.GetExtension(archivo.FileName).ToLowerInvariant();

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

    private IFormFile? BuscarArchivoPorNombre(List<IFormFile> archivos, string palabraEsperada)
    {
        var palabraNormalizada = NormalizarTexto(palabraEsperada);

        return archivos.FirstOrDefault(archivo =>
        {
            var nombreSinExtension = Path.GetFileNameWithoutExtension(archivo.FileName);
            var nombreNormalizado = NormalizarTexto(nombreSinExtension);

            return nombreNormalizado.Contains(palabraNormalizada);
        });
    }

    private void ValidarArchivoEsperado(IFormFile? archivo, string tipoArchivo, string palabraEsperada, List<CargaValidacionError> errores)
    {
        if (archivo != null)
        {
            return;
        }

        errores.Add(new CargaValidacionError
        {
            Archivo = tipoArchivo,
            Codigo = $"GENERAL_FALTA_ARCHIVO_{tipoArchivo.ToUpperInvariant()}",
            DescripcionResumen = $"Falta archivo de {tipoArchivo}",
            Mensaje = $"Debe enviar un archivo cuyo nombre contenga la palabra \"{palabraEsperada}\"."
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
            })
            .ToList();

        if (coincidencias.Count <= 1)
        {
            return;
        }

        errores.Add(new CargaValidacionError
        {
            Archivo = tipoArchivo,
            Codigo = $"GENERAL_ARCHIVO_DUPLICADO_{tipoArchivo.ToUpperInvariant()}",
            DescripcionResumen = $"Archivo duplicado de {tipoArchivo}",
            Mensaje = $"Se encontraron {coincidencias.Count} archivos que parecen corresponder a {tipoArchivo}. Solo debe enviar uno."
        });
    }

    private static string NormalizarTexto(string texto)
    {
        texto = texto.ToLowerInvariant().Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder();

        foreach (var caracter in texto)
        {
            var categoria = CharUnicodeInfo.GetUnicodeCategory(caracter);

            if (categoria != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(caracter);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private void FinalizarRespuesta(CargaValidacionResponse response, int totalCarpetas, int totalDelitos, int totalVictimas)
    {
        // EsValido es propiedad calculada en CargaValidacionResponse:
        // true si Errores.Count == 0.
        response.Mensaje = response.EsValido
            ? "Actualización validada correctamente. Puede generar el acuse previo y confirmar la actualización."
            : "La actualización contiene errores de validación.";

        // Resumen base de registros recibidos.
        response.ResumenValidacion = new List<CargaValidacionResumenItem>
        {
            new CargaValidacionResumenItem
            {
                Archivo = "carpetas",
                Codigo = "TOTAL_CARPETAS",
                Descripcion = "Total de registros recibidos en el archivo de carpetas.",
                TotalRegistros = totalCarpetas,
                EsError = false
            },
            new CargaValidacionResumenItem
            {
                Archivo = "delitos",
                Codigo = "TOTAL_DELITOS",
                Descripcion = "Total de registros recibidos en el archivo de delitos.",
                TotalRegistros = totalDelitos,
                EsError = false
            },
            new CargaValidacionResumenItem
            {
                Archivo = "victimas",
                Codigo = "TOTAL_VICTIMAS",
                Descripcion = "Total de registros recibidos en el archivo de víctimas.",
                TotalRegistros = totalVictimas,
                EsError = false
            }
        };

        // Resumen de errores agrupados por archivo.
        var erroresPorArchivo = response.Errores
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Archivo) ? "general" : e.Archivo)
            .Select(g => new CargaValidacionResumenItem
            {
                Archivo = g.Key,
                Codigo = "TOTAL_ERRORES",
                Descripcion = $"Total de errores encontrados en {g.Key}.",
                TotalRegistros = g.Count(),
                EsError = true
            });

        response.ResumenValidacion.AddRange(erroresPorArchivo);
    }

    private static string GenerarCodigoReferencia()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }

    private static (int Mes, int Anio) ObtenerPeriodoInformacionDesdeCorte(int mesCorte, int anioCorte)
    {
        // En el sistema, mesCorte/anioCorte representa el periodo de información reportado.
        // Ejemplo:
        // Si se selecciona abril 2026, los archivos deben contener información de abril 2026.
        // La fecha en que se realiza la actualización se registra aparte en fecha_validacion.
        return (mesCorte, anioCorte);
    }

    public async Task<ActualizacionDiferenciasResponse> ObtenerDetalleDiferenciasAsync(string codigoReferencia, int idUsuarioConsulta)
    {
        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            return new ActualizacionDiferenciasResponse
            {
                EsValido = false,
                CodigoReferencia = codigoReferencia,
                Mensaje = "El usuario autenticado no existe o no está activo."
            };
        }

        var detalle = await _actualizacionDiferenciasRepository.ObtenerDetalleDiferenciasActualizacionAsync(
                    codigoReferencia,
                    usuarioConsulta.IdEntidadFederativa,
                    usuarioConsulta.EsSuperUsuario);

        if (detalle == null)
        {
            return new ActualizacionDiferenciasResponse
            {
                EsValido = false,
                CodigoReferencia = codigoReferencia,
                Mensaje = "No se encontró una actualización pendiente válida para el código de referencia indicado."
            };
        }

        return detalle;
    }

    public async Task<ConfirmarCargaResponse> ConfirmarActualizacionAsync(ConfirmarCargaRequest request, int idUsuarioConfirmacion)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CodigoReferencia))
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                CodigoReferencia = request.CodigoReferencia,
                Estado = "",
                Mensaje = "Debe enviar el código de referencia de la actualización."
            };
        }

        return await _actualizacionRepository.ConfirmarActualizacionAsync(
            request.CodigoReferencia,
            request.Aceptar,
            idUsuarioConfirmacion);
    }

    public async Task<ActualizacionPeriodoResponse> ConsultarPeriodoActualizacionAsync(int mesCorte, int anioCorte, int idUsuarioConsulta, int? idEntidadFederativa = null)
    {
        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            return new ActualizacionPeriodoResponse
            {
                EsValido = false,
                PuedeActualizar = false,
                TieneCargaConfirmada = false,
                ExisteActualizacionPendiente = false,
                MesCorte = mesCorte,
                AnioCorte = anioCorte,
                Mensaje = "El usuario autenticado no existe o no está activo."
            };
        }

        if (!usuarioConsulta.HabilitaModificacion)
        {
            return new ActualizacionPeriodoResponse
            {
                EsValido = false,
                PuedeActualizar = false,
                TieneCargaConfirmada = false,
                ExisteActualizacionPendiente = false,
                IdEntidadFederativa = usuarioConsulta.IdEntidadFederativa,
                MesCorte = mesCorte,
                AnioCorte = anioCorte,
                Mensaje = "El usuario autenticado no tiene habilitada la modificación de información."
            };
        }

        if (mesCorte < 1 || mesCorte > 12)
        {
            return new ActualizacionPeriodoResponse
            {
                EsValido = false,
                PuedeActualizar = false,
                TieneCargaConfirmada = false,
                ExisteActualizacionPendiente = false,
                IdEntidadFederativa = usuarioConsulta.IdEntidadFederativa,
                MesCorte = mesCorte,
                AnioCorte = anioCorte,
                Mensaje = "El mes de corte debe ser un número entre 1 y 12."
            };
        }

        if (anioCorte < 2000 || anioCorte > 2100)
        {
            return new ActualizacionPeriodoResponse
            {
                EsValido = false,
                PuedeActualizar = false,
                TieneCargaConfirmada = false,
                ExisteActualizacionPendiente = false,
                IdEntidadFederativa = usuarioConsulta.IdEntidadFederativa,
                MesCorte = mesCorte,
                AnioCorte = anioCorte,
                Mensaje = "El año de corte debe ser un número válido entre 2000 y 2100."
            };
        }

        int? idEntidadConsulta;

        if (usuarioConsulta.EsSuperUsuario)
        {
            idEntidadConsulta = idEntidadFederativa;

            if (!idEntidadConsulta.HasValue)
            {
                return new ActualizacionPeriodoResponse
                {
                    EsValido = false,
                    PuedeActualizar = false,
                    TieneCargaConfirmada = false,
                    ExisteActualizacionPendiente = false,
                    IdEntidadFederativa = null,
                    MesCorte = mesCorte,
                    AnioCorte = anioCorte,
                    Mensaje = "Debe enviar la entidad federativa que desea consultar."
                };
            }
        }
        else
        {
            idEntidadConsulta = usuarioConsulta.IdEntidadFederativa;

            if (!idEntidadConsulta.HasValue)
            {
                return new ActualizacionPeriodoResponse
                {
                    EsValido = false,
                    PuedeActualizar = false,
                    TieneCargaConfirmada = false,
                    ExisteActualizacionPendiente = false,
                    IdEntidadFederativa = null,
                    MesCorte = mesCorte,
                    AnioCorte = anioCorte,
                    Mensaje = "El usuario no tiene una entidad federativa asignada."
                };
            }
        }

        var existeCargaConfirmada = await _cargaRepository.ExisteCargaConfirmadaAsync(
            idEntidadConsulta.Value,
            mesCorte,
            anioCorte);

        if (!existeCargaConfirmada)
        {
            return new ActualizacionPeriodoResponse
            {
                EsValido = true,
                PuedeActualizar = false,
                TieneCargaConfirmada = false,
                ExisteActualizacionPendiente = false,
                IdEntidadFederativa = idEntidadConsulta.Value,
                MesCorte = mesCorte,
                AnioCorte = anioCorte,
                Mensaje = $"No existe una carga inicial confirmada para el periodo {mesCorte:00}/{anioCorte}. No se puede realizar actualización."
            };
        }

        var codigoActualizacionPendiente = await _cargaRepository.ObtenerCodigoActualizacionPendienteAsync(
            idEntidadConsulta.Value,
            mesCorte,
            anioCorte);

        if (!string.IsNullOrWhiteSpace(codigoActualizacionPendiente))
        {
            return new ActualizacionPeriodoResponse
            {
                EsValido = true,
                PuedeActualizar = false,
                TieneCargaConfirmada = true,
                ExisteActualizacionPendiente = true,
                CodigoActualizacionPendiente = codigoActualizacionPendiente,
                IdEntidadFederativa = idEntidadConsulta.Value,
                MesCorte = mesCorte,
                AnioCorte = anioCorte,
                Mensaje = $"Ya existe una actualización pendiente para el periodo {mesCorte:00}/{anioCorte}. Código de referencia pendiente: {codigoActualizacionPendiente}."
            };
        }

        return new ActualizacionPeriodoResponse
        {
            EsValido = true,
            PuedeActualizar = true,
            TieneCargaConfirmada = true,
            ExisteActualizacionPendiente = false,
            CodigoActualizacionPendiente = null,
            IdEntidadFederativa = idEntidadConsulta.Value,
            MesCorte = mesCorte,
            AnioCorte = anioCorte,
            Mensaje = $"Existe carga inicial confirmada para el periodo {mesCorte:00}/{anioCorte}. Puede continuar con la actualización."
        };
    }

    public async Task<List<ActualizacionAnioDisponibleItem>> ObtenerPeriodosDisponiblesActualizacionAsync(int idUsuarioConsulta, int? idEntidadFederativa = null)
    {
        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            return new List<ActualizacionAnioDisponibleItem>();
        }

        if (!usuarioConsulta.HabilitaModificacion)
        {
            return new List<ActualizacionAnioDisponibleItem>();
        }

        int? idEntidadConsulta;

        if (usuarioConsulta.EsSuperUsuario)
        {
            idEntidadConsulta = idEntidadFederativa;
        }
        else
        {
            idEntidadConsulta = usuarioConsulta.IdEntidadFederativa;
        }

        if (!idEntidadConsulta.HasValue)
        {
            return new List<ActualizacionAnioDisponibleItem>();
        }

        return await _cargaRepository.ObtenerPeriodosDisponiblesActualizacionAsync(idEntidadConsulta.Value);
    }
}