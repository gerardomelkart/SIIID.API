using System.Globalization;
using System.Text;
using SIIID2.Api.Models;
using SIIID2.Api.Readers;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;

public class ActualizacionArchivosService : IActualizacionArchivosService
{
    private readonly IArchivoReader _archivoReader;
    private readonly CarpetasValidator _carpetasValidator;
    private readonly DelitosValidator _delitosValidator;
    private readonly VictimasValidator _victimasValidator;
    private readonly CargaIntegridadValidator _cargaIntegridadValidator;
    private readonly CatalogosValidator _catalogosValidator;
    private readonly ICargaRepository _cargaRepository;
    private readonly IUsuarioRepository _usuarioRepository;

    private readonly string[] _extensionesPermitidas =
    {
        ".csv",
        ".xlsx"
    };

    private const long TamanioMaximoBytes = 50 * 1024 * 1024;

    public ActualizacionArchivosService(
        IArchivoReader archivoReader,
        CarpetasValidator carpetasValidator,
        DelitosValidator delitosValidator,
        VictimasValidator victimasValidator,
        CargaIntegridadValidator cargaIntegridadValidator,
        CatalogosValidator catalogosValidator,
        ICargaRepository cargaRepository,
        IUsuarioRepository usuarioRepository)
    {
        _archivoReader = archivoReader;
        _carpetasValidator = carpetasValidator;
        _delitosValidator = delitosValidator;
        _victimasValidator = victimasValidator;
        _cargaIntegridadValidator = cargaIntegridadValidator;
        _catalogosValidator = catalogosValidator;
        _cargaRepository = cargaRepository;
        _usuarioRepository = usuarioRepository;
    }

    public async Task<CargaValidacionResponse> ValidarActualizacionAsync(IFormCollection form, int idUsuarioCarga)
    {
        var archivos = form.Files;

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

        var response = new CargaValidacionResponse
        {
            CodigoReferencia = GenerarCodigoReferencia()
        };

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

        foreach (var archivo in listaArchivos)
        {
            ValidarArchivoBase(archivo, response.Errores);
        }

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

        var filasCarpetas = await _archivoReader.LeerAsync(archivoCarpetas!);
        var filasDelitos = await _archivoReader.LeerAsync(archivoDelitos!);
        var filasVictimas = await _archivoReader.LeerAsync(archivoVictimas!);

        response.Errores.AddRange(_carpetasValidator.Validar(filasCarpetas));
        response.Errores.AddRange(_delitosValidator.Validar(filasDelitos));
        response.Errores.AddRange(_victimasValidator.Validar(filasVictimas));

        response.Errores.AddRange(ValidarEntidadUsuarioCarga(usuarioCarga, filasDelitos));

        var sinErroresInternos = response.Errores.Count == 0;

        if (sinErroresInternos)
        {
            response.Errores.AddRange(_cargaIntegridadValidator.Validar(
                filasCarpetas,
                filasDelitos,
                filasVictimas));
        }

        response.Errores.AddRange(await _catalogosValidator.ValidarAsync(
            filasCarpetas,
            filasDelitos,
            filasVictimas));

        var mesCorte = ObtenerMesCorteDesdeCarpetas(filasCarpetas);
        var anioCorte = ObtenerAnioCorteDesdeCarpetas(filasCarpetas);

        var idEntidadFederativaCarga = ObtenerEntidadFederativaCarga(
            usuarioCarga,
            filasDelitos,
            response.Errores);

        if (idEntidadFederativaCarga.HasValue && response.Errores.Count == 0)
        {
            var existeCargaConfirmada = await _cargaRepository.ExisteCargaConfirmadaAsync(
                idEntidadFederativaCarga.Value,
                mesCorte,
                anioCorte);

            if (!existeCargaConfirmada)
            {
                response.Errores.Add(new CargaValidacionError
                {
                    Archivo = "general",
                    Fila = null,
                    Columna = "",
                    Campo = "",
                    Valor = null,
                    Codigo = "ACTUALIZACION_SIN_CARGA_CONFIRMADA",
                    DescripcionResumen = "No existe carga confirmada",
                    Mensaje = $"No existe información confirmada para la entidad {idEntidadFederativaCarga.Value} y periodo {mesCorte:00}/{anioCorte}. Para continuar debe usar el flujo de carga nueva."
                });
            }
            else
            {
                var codigoActualizacionPendiente = await _cargaRepository.ObtenerCodigoActualizacionPendienteAsync(
                    idEntidadFederativaCarga.Value,
                    mesCorte,
                    anioCorte);

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
                        Mensaje = $"Ya existe una actualización validada pendiente de confirmar para la entidad {idEntidadFederativaCarga.Value} y periodo {mesCorte:00}/{anioCorte}. Código de referencia pendiente: {codigoActualizacionPendiente}. Debe confirmar o rechazar esa actualización antes de enviar una nueva."
                    });
                }
            }
        }

        FinalizarRespuesta(
            response,
            filasCarpetas.Count,
            filasDelitos.Count,
            filasVictimas.Count);

        var estadoCarga = response.EsValido
            ? "VALIDADO_PENDIENTE_ACTUALIZACION"
            : "RECHAZADO_VALIDACION_ACTUALIZACION";

        var mensajeError = response.EsValido
            ? null
            : $"La actualización contiene errores de validación. Total de errores: {response.Errores.Count}.";

        await _cargaRepository.GuardarIntentoActualizacionAsync(
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
            filasCarpetas,
            filasDelitos,
            filasVictimas);

        return response;
    }

    private int? ObtenerEntidadFederativaCarga(UsuarioCargaInfo usuarioCarga, List<ArchivoFila> filasDelitos, List<CargaValidacionError> errores)
    {
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
                Mensaje = "La actualización contiene delitos de más de una entidad federativa. Una actualización solo puede corresponder a una entidad."
            });

            return null;
        }

        return entidades.First();
    }

    private List<CargaValidacionError> ValidarEntidadUsuarioCarga(UsuarioCargaInfo usuarioCarga, List<ArchivoFila> filasDelitos)
    {
        var errores = new List<CargaValidacionError>();

        if (usuarioCarga.EsSuperUsuario)
        {
            return errores;
        }

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

        foreach (var fila in filasDelitos)
        {
            fila.Columnas.TryGetValue("id_ent_hchos", out var valorEntidad);

            if (string.IsNullOrWhiteSpace(valorEntidad))
            {
                continue;
            }

            valorEntidad = valorEntidad.Trim();

            if (!int.TryParse(valorEntidad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idEntidadExcel))
            {
                continue;
            }

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

    private int ObtenerMesCorteDesdeCarpetas(List<ArchivoFila> filasCarpetas)
    {
        foreach (var fila in filasCarpetas)
        {
            fila.Columnas.TryGetValue("fha_de_ini", out var valorFecha);

            if (DateTime.TryParse(valorFecha, out var fecha))
            {
                return fecha.Month;
            }
        }

        return 0;
    }

    private int ObtenerAnioCorteDesdeCarpetas(List<ArchivoFila> filasCarpetas)
    {
        foreach (var fila in filasCarpetas)
        {
            fila.Columnas.TryGetValue("fha_de_ini", out var valorFecha);

            if (DateTime.TryParse(valorFecha, out var fecha))
            {
                return fecha.Year;
            }
        }

        return 0;
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
        response.Mensaje = response.EsValido
            ? "Actualización validada correctamente. Puede generar el acuse previo y confirmar la actualización."
            : "La actualización contiene errores de validación.";

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
}