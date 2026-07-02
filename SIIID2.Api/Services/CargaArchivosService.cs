using System.Globalization;
using System.Text;
using SIIID2.Api.Models;
using SIIID2.Api.Readers;
using SIIID2.Api.Validators;
using SIIID2.Api.Repositories;

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
    private readonly CatalogosValidator _catalogosValidator;
    private readonly ICargaRepository _cargaRepository;
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IUltimosArchivosEntidadService _ultimosArchivosEntidadService;

    // Extensiones permitidas para los archivos de carga.
    private readonly string[] _extensionesPermitidas =
    {
        ".csv",
        ".xlsx"
    };

    // Tamaño máximo permitido por archivo: 50 MB.
    private const long TamanioMaximoBytes = 50 * 1024 * 1024;

    public CargaArchivosService(IArchivoReader archivoReader, CarpetasValidator carpetasValidator, DelitosValidator delitosValidator, VictimasValidator victimasValidator, CargaIntegridadValidator cargaIntegridadValidator, CatalogosValidator catalogosValidator, ICargaRepository cargaRepository, IUsuarioRepository usuarioRepository, IUltimosArchivosEntidadService ultimosArchivosEntidadService)
    {
        _archivoReader = archivoReader;
        _carpetasValidator = carpetasValidator;
        _delitosValidator = delitosValidator;
        _victimasValidator = victimasValidator;
        _cargaIntegridadValidator = cargaIntegridadValidator;
        _catalogosValidator = catalogosValidator;
        _cargaRepository = cargaRepository;
        _usuarioRepository = usuarioRepository;
        _ultimosArchivosEntidadService = ultimosArchivosEntidadService;
    }

    public async Task<CargaValidacionResponse> ValidarArchivosAsync(IFormCollection form, int idUsuarioCarga)
    {
        // Los archivos vienen dentro del form-data.
        var archivos = form.Files;

        // El usuario viene del token.
        // Aquí se consulta base para validar que exista, esté activo, tenga rol activo y permisos.
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

        // El usuario debe tener habilitada la carga.
        // Esto se controla desde habilita_carga_modificacion.
        if (!usuarioCarga.HabilitaCarga)
        {
            var responseUsuario = new CargaValidacionResponse
            {
                CodigoReferencia = GenerarCodigoReferencia()
            };

            responseUsuario.Errores.Add(new CargaValidacionError
            {
                Archivo = "general",
                Fila = null,
                Columna = "habilita_carga",
                Campo = "habilita_carga",
                Valor = usuarioCarga.HabilitaCarga.ToString(),
                Codigo = "GENERAL_USUARIO_SIN_PERMISO_CARGA",
                DescripcionResumen = "Usuario sin permiso de carga",
                Mensaje = "El usuario autenticado no tiene habilitada la carga de información."
            });

            FinalizarRespuesta(responseUsuario, 0, 0, 0);
            return responseUsuario;
        }

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

        // Validación estructural temprana.
        // Si falta una columna obligatoria, no leemos filas completas,
        // no corremos más validaciones y no guardamos temporales.
        var erroresEstructura = await ValidarEstructuraArchivosAsync(
            archivoCarpetas!,
            archivoDelitos!,
            archivoVictimas!);

        if (erroresEstructura.Count > 0)
        {
            response.Errores.AddRange(erroresEstructura);

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

        // Validamos que el usuario solo cargue información de su entidad.
        response.Errores.AddRange(ValidarEntidadUsuarioCarga(usuarioCarga, filasDelitos));

        // Guardamos si las validaciones internas pasaron limpias.
        var sinErroresInternos = response.Errores.Count == 0;

        var advertenciasPendientes = new List<CargaValidacionError>();

        // Las validaciones cruzadas se ejecutan solo si las validaciones internas pasaron.
        // Esto evita errores repetidos o confusos cuando falta estructura básica.
        if (sinErroresInternos)
        {
            var erroresIntegridad = _cargaIntegridadValidator.Validar(
                filasCarpetas,
                filasDelitos,
                filasVictimas);

            if (usuarioCarga.EsSuperUsuario)
            {
                var advertenciasFechaHechos = erroresIntegridad
                    .Where(e => e.Codigo == "INTEGRIDAD_FECHA_HECHOS_MAYOR_FECHA_INICIO")
                    .ToList();

                advertenciasPendientes.AddRange(advertenciasFechaHechos);

                erroresIntegridad = erroresIntegridad
                    .Where(e => e.Codigo != "INTEGRIDAD_FECHA_HECHOS_MAYOR_FECHA_INICIO")
                    .ToList();
            }

            response.Errores.AddRange(erroresIntegridad);
        }

        // Las validaciones contra catálogos sí pueden ejecutarse aunque existan errores internos.
        // La primera se reporta como formato incorrecto; la segunda se reporta como clave inexistente.
        response.Errores.AddRange(await _catalogosValidator.ValidarAsync(
            filasCarpetas,
            filasDelitos,
            filasVictimas));


        // El mes/año de corte corresponde al periodo de información reportado.
        // Ejemplo:
        // Si la información cargada es de abril 2026, entonces:
        // mes_corte = 4
        // anio_corte = 2026.
        // La fecha en que se carga o confirma queda registrada aparte en fecha_validacion / fecha_confirmacion.
        var mesCorte = ObtenerMesCorteDesdeCarpetas(filasCarpetas);
        var anioCorte = ObtenerAnioCorteDesdeCarpetas(filasCarpetas);

        // Obtenemos la entidad real de la carga.
        // Para usuario normal viene de su usuario.
        // Para SUPER_USUARIO se toma del Excel de delitos.
        var idEntidadFederativaCarga = ObtenerEntidadFederativaCarga(
            usuarioCarga,
            filasDelitos,
            response.Errores);

        // Si no hay errores hasta este punto, revisamos si ya existe una carga
        // confirmada o pendiente para la misma entidad y periodo.
        if (idEntidadFederativaCarga.HasValue && response.Errores.Count == 0)
        {
            var existeCargaConfirmada = await _cargaRepository.ExisteCargaConfirmadaAsync(
                idEntidadFederativaCarga.Value,
                mesCorte,
                anioCorte);

            if (existeCargaConfirmada)
            {
                response.Errores.Add(new CargaValidacionError
                {
                    Archivo = "general",
                    Fila = null,
                    Columna = "",
                    Campo = "",
                    Valor = null,
                    Codigo = "CARGA_PERIODO_YA_CONFIRMADO",
                    DescripcionResumen = "Ya existe carga confirmada",
                    Mensaje = $"Ya existe información confirmada para la entidad {idEntidadFederativaCarga.Value} y periodo {mesCorte:00}/{anioCorte}. Para continuar debe usar el flujo de actualización."
                });
            }
            else
            {
                var cargaPendiente = await _cargaRepository.ObtenerCodigoCargaPendienteAsync(idEntidadFederativaCarga.Value, mesCorte, anioCorte);

                if (cargaPendiente != null)
                {
                    var enRevisionAdministrativa = string.Equals(cargaPendiente.Estado, "PENDIENTE_APROBACION", StringComparison.OrdinalIgnoreCase);

                    response.Errores.Add(new CargaValidacionError
                    {
                        Archivo = "general",
                        Fila = null,
                        Columna = "",
                        Campo = "",
                        Valor = cargaPendiente.CodigoReferencia,
                        Codigo = enRevisionAdministrativa ? "CARGA_PENDIENTE_APROBACION" : "CARGA_PENDIENTE_EXISTENTE",
                        DescripcionResumen = enRevisionAdministrativa ? "Carga en revisión administrativa" : "Ya existe carga pendiente",
                        Mensaje = enRevisionAdministrativa
                            ? $"Ya existe una carga en revisión administrativa para la entidad {idEntidadFederativaCarga.Value} y periodo {mesCorte:00}/{anioCorte}. Código de referencia pendiente: {cargaPendiente.CodigoReferencia}. Debe esperar la resolución del administrador."
                            : $"Ya existe una carga validada pendiente de confirmar para la entidad {idEntidadFederativaCarga.Value} y periodo {mesCorte:00}/{anioCorte}. Código de referencia pendiente: {cargaPendiente.CodigoReferencia}. Debe aceptar o rechazar esa carga antes de enviar una nueva."
                    });
                }
            }
        }

        // Si no hay errores después de TODAS las validaciones,
        // ahora sí agregamos advertencias de decisión.
        if (response.Errores.Count == 0)
        {
            response.Advertencias.AddRange(advertenciasPendientes);

            response.Advertencias.AddRange(_cargaIntegridadValidator.ValidarAdvertencias(
                filasDelitos,
                filasVictimas));
        }

        // Construimos resumen y mensaje final.
        FinalizarRespuesta(
            response,
            filasCarpetas.Count,
            filasDelitos.Count,
            filasVictimas.Count);

        // Errores bloqueantes.
        // No se guarda fila en carga ni staging para evitar ensuciar la base.
        //
        // Casos bloqueantes:
        // - el usuario no tiene entidad asignada
        // - el Excel contiene una entidad distinta a la del usuario
        // - ya existe carga confirmada
        // - ya existe carga pendiente
        if (response.Errores.Any(x =>
                x.Codigo == "GENERAL_USUARIO_SIN_ENTIDAD" ||
                x.Codigo == "DELITOS_ENTIDAD_NO_CORRESPONDE_USUARIO" ||
                x.Codigo == "CARGA_PERIODO_YA_CONFIRMADO" ||
                x.Codigo == "CARGA_PENDIENTE_EXISTENTE" ||
                x.Codigo == "CARGA_PENDIENTE_APROBACION"))
        {
            return response;
        }

        // Determinamos el estado inicial del intento de carga.
        var estadoCarga = response.EsValido
            ? "VALIDADO_PENDIENTE"
            : "RECHAZADO_VALIDACION";

        // Si hubo errores, guardamos solo mensaje general.
        // El detalle completo se devuelve en la respuesta de la API.
        var mensajeError = response.EsValido
            ? null
            : $"La información contiene errores de validación. Total de errores: {response.Errores.Count}.";

        // Se guarda el intento completo de carga.
        // Incluye registro en carga y staging de los tres archivos.
        // Todo se ejecuta dentro de una transacción en el repository.
        var idCarga = await _cargaRepository.GuardarIntentoCargaAsync(
            idUsuarioCarga,
            idEntidadFederativaCarga,
            response.CodigoReferencia,
            mesCorte,
            anioCorte,
            filasCarpetas.Count,
            filasDelitos.Count,
            filasVictimas.Count,
            estadoCarga,
            mensajeError,
            response.Advertencias,
            filasCarpetas,
            filasDelitos,
            filasVictimas);

        if (idEntidadFederativaCarga.HasValue)
        {
            await _ultimosArchivosEntidadService.GuardarAsync(idEntidadFederativaCarga.Value, response.CodigoReferencia, "CARGA_INICIAL", mesCorte, anioCorte, archivoCarpetas!, archivoDelitos!, archivoVictimas!);
        }

        return response;
    }

    private int? ObtenerEntidadFederativaCarga(UsuarioCargaInfo usuarioCarga, List<ArchivoFila> filasDelitos, List<CargaValidacionError> errores)
    {
        // Para usuarios normales, la entidad de la carga es la entidad asignada al usuario.
        if (!usuarioCarga.EsSuperUsuario)
        {
            return usuarioCarga.IdEntidadFederativa;
        }

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
                Mensaje = "La carga contiene delitos de más de una entidad federativa. Una carga solo puede corresponder a una entidad."
            });

            return null;
        }

        var idEntidadExcel = entidades.First();

        return entidades.First();
    }

    private List<CargaValidacionError> ValidarEntidadUsuarioCarga(UsuarioCargaInfo usuarioCarga, List<ArchivoFila> filasDelitos)
    {
        var errores = new List<CargaValidacionError>();

        // El SUPER_USUARIO puede cargar información de cualquier entidad.
        if (usuarioCarga.EsSuperUsuario)
        {
            return errores;
        }

        // Los usuarios normales deben tener entidad asignada.
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
                Mensaje = "El usuario no tiene una entidad federativa asignada y no puede realizar cargas."
            });

            return errores;
        }

        foreach (var fila in filasDelitos)
        {
            fila.Columnas.TryGetValue("id_ent_hchos", out var valorEntidad);

            // Si viene vacío, lo reporta el DelitosValidator.
            if (string.IsNullOrWhiteSpace(valorEntidad))
            {
                continue;
            }

            valorEntidad = valorEntidad.Trim();

            // Si no se puede convertir, lo reportan otras validaciones.
            if (!int.TryParse(valorEntidad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idEntidadExcel))
            {
                continue;
            }

            // Acepta valores como 9 y 09 porque ambos se convierten a 9.
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
            Mensaje = $"Se recibió más de un archivo para {tipoArchivo}. Solo debe enviarse uno."
        });
    }

    private void FinalizarRespuesta(CargaValidacionResponse response, int totalCarpetas, int totalDelitos, int totalVictimas)
    {
        // Armamos el resumen que puede alimentar una vista tipo tabla.
        response.ResumenValidacion = ConstruirResumenValidacion(
            response.Errores.Concat(response.Advertencias).ToList(),
            totalCarpetas,
            totalDelitos,
            totalVictimas);

        if (response.EsValido && response.Advertencias.Count > 0)
        {
            response.Mensaje = "La información fue validada con advertencias. Revise las advertencias antes de continuar.";
        }
        else
        {
            response.Mensaje = response.EsValido
                ? "La información fue validada correctamente. Puede continuar con el acuse previo."
                : "La información contiene errores de validación.";
        }
    }

    private List<CargaValidacionResumenItem> ConstruirResumenValidacion(List<CargaValidacionError> errores, int totalCarpetas, int totalDelitos, int totalVictimas)
    {
        // Totales principales de los tres archivos.
        var resumen = new List<CargaValidacionResumenItem>
        {
            new CargaValidacionResumenItem
            {
                Archivo = "carpetas",
                Codigo = "CARPETAS_TOTAL_REGISTROS",
                Descripcion = "Total de registros en el archivo de carpetas",
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

    private int ObtenerMesCorteDesdeCarpetas(List<ArchivoFila> filasCarpetas)
    {
        var fecha = ObtenerPrimeraFechaInicioValida(filasCarpetas);

        if (fecha.HasValue)
        {
            return fecha.Value.Month;
        }

        // Si no hay fecha válida, usamos mes inmediato anterior.
        return DateTime.Today.AddMonths(-1).Month;
    }

    private int ObtenerAnioCorteDesdeCarpetas(List<ArchivoFila> filasCarpetas)
    {
        var fecha = ObtenerPrimeraFechaInicioValida(filasCarpetas);

        if (fecha.HasValue)
        {
            return fecha.Value.Year;
        }

        // Si no hay fecha válida, usamos el año del mes inmediato anterior.
        return DateTime.Today.AddMonths(-1).Year;
    }

    private DateTime? ObtenerPrimeraFechaInicioValida(List<ArchivoFila> filasCarpetas)
    {
        foreach (var fila in filasCarpetas)
        {
            fila.Columnas.TryGetValue("fha_de_ini", out var valor);

            if (string.IsNullOrWhiteSpace(valor))
            {
                continue;
            }

            // Primero intentamos con cultura mexicana.
            if (DateTime.TryParse(valor, new CultureInfo("es-MX"), DateTimeStyles.None, out var fecha))
            {
                return fecha.Date;
            }

            // Si no se puede, intentamos con cultura invariante.
            if (DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaInvariant))
            {
                return fechaInvariant.Date;
            }
        }

        return null;
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

    public async Task<ConfirmarCargaResponse> CargarMigracionDirectaAsync(IFormCollection form, int idUsuarioCarga)
    {
        // Endpoint exclusivo para migración histórica.
        // Usa el usuario autenticado como usuario de carga, registro y confirmación.
        // No ejecuta validaciones de negocio ni requiere confirmación posterior.

        var usuarioCarga = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioCarga);

        if (usuarioCarga == null)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                Estado = "USUARIO_CARGA_INVALIDO",
                Mensaje = "El usuario autenticado no existe o no está activo."
            };
        }

        if (!usuarioCarga.HabilitaCarga)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                Estado = "USUARIO_SIN_PERMISO_CARGA",
                Mensaje = "El usuario autenticado no tiene habilitada la carga de información."
            };
        }

        if (!usuarioCarga.IdEntidadFederativa.HasValue)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                Estado = "USUARIO_SIN_ENTIDAD",
                Mensaje = "El usuario autenticado no tiene una entidad federativa asignada."
            };
        }

        if (!TryObtenerEnteroForm(form, "mesCorte", out var mesCorte) || mesCorte < 1 || mesCorte > 12)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                Estado = "SOLICITUD_INVALIDA",
                Mensaje = "Debe enviar mesCorte como número entre 1 y 12."
            };
        }

        if (!TryObtenerEnteroForm(form, "anioCorte", out var anioCorte) || anioCorte < 2000 || anioCorte > 2100)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                Estado = "SOLICITUD_INVALIDA",
                Mensaje = "Debe enviar anioCorte como año válido."
            };
        }

        var archivos = form.Files;

        if (archivos == null || archivos.Count == 0)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                Estado = "SOLICITUD_INVALIDA",
                Mensaje = "Debe enviar los archivos de carpetas, delitos y víctimas."
            };
        }

        var listaArchivos = archivos.ToList();

        var archivoCarpetas = BuscarArchivoPorNombre(listaArchivos, "carpeta");
        var archivoDelitos = BuscarArchivoPorNombre(listaArchivos, "delito");
        var archivoVictimas = BuscarArchivoPorNombre(listaArchivos, "victima");

        if (archivoCarpetas == null || archivoDelitos == null || archivoVictimas == null)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                Estado = "SOLICITUD_INVALIDA",
                Mensaje = "Debe enviar un archivo de carpetas, uno de delitos y uno de víctimas. Los nombres deben contener carpeta, delito y victima."
            };
        }

        // Solo lectura. No se ejecutan validadores de estructura, catálogo, integridad,
        // entidad del Excel, periodo confirmado ni carga pendiente.
        var filasCarpetas = await _archivoReader.LeerAsync(archivoCarpetas);
        var filasDelitos = await _archivoReader.LeerAsync(archivoDelitos);
        var filasVictimas = await _archivoReader.LeerAsync(archivoVictimas);

        var idEntidadFederativa = usuarioCarga.IdEntidadFederativa.Value;

        var codigoReferencia =
            $"MIGRACION-{idEntidadFederativa}-{anioCorte}{mesCorte:00}-{DateTime.Now:yyyyMMddHHmmss}";

        return await _cargaRepository.GuardarYConfirmarCargaDirectaAsync(
            idUsuarioCarga,
            idEntidadFederativa,
            codigoReferencia,
            mesCorte,
            anioCorte,
            filasCarpetas,
            filasDelitos,
            filasVictimas);
    }

    private static bool TryObtenerEnteroForm(IFormCollection form, string campo, out int valor)
    {
        valor = 0;

        var texto = form.TryGetValue(campo, out var values)
            ? values.FirstOrDefault()
            : null;

        return int.TryParse(
            texto,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out valor);
    }

    private async Task<List<CargaValidacionError>> ValidarEstructuraArchivosAsync(IFormFile archivoCarpetas, IFormFile archivoDelitos, IFormFile archivoVictimas)
    {
        var errores = new List<CargaValidacionError>();

        await ValidarColumnasObligatoriasArchivoAsync(
            archivoCarpetas,
            "carpetas",
            _carpetasValidator.ColumnasObligatorias,
            errores);

        await ValidarColumnasObligatoriasArchivoAsync(
            archivoDelitos,
            "delitos",
            _delitosValidator.ColumnasObligatorias,
            errores);

        await ValidarColumnasObligatoriasArchivoAsync(
            archivoVictimas,
            "victimas",
            _victimasValidator.ColumnasObligatorias,
            errores);

        return errores;
    }

    private async Task ValidarColumnasObligatoriasArchivoAsync(IFormFile archivo, string nombreArchivo, IReadOnlyCollection<string> columnasObligatorias, List<CargaValidacionError> errores)
    {
        var encabezados = await _archivoReader.LeerEncabezadosAsync(archivo);

        var columnasArchivo = encabezados
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var columna in columnasObligatorias)
        {
            if (columnasArchivo.Contains(columna))
            {
                continue;
            }

            errores.Add(new CargaValidacionError
            {
                Archivo = nombreArchivo,
                Fila = 1,
                Columna = columna,
                Campo = columna,
                Valor = null,
                Codigo = $"{nombreArchivo.ToUpperInvariant()}_COLUMNA_OBLIGATORIA_NO_ENCONTRADA",
                DescripcionResumen = "Columna obligatoria no encontrada",
                Mensaje = $"El archivo de {nombreArchivo} no contiene la columna obligatoria \"{columna}\"."
            });
        }
    }
}