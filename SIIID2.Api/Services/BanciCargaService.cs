using SIIID2.Api.Models;
using SIIID2.Api.Readers;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;

public class BanciCargaService : IBanciCargaService
{
    private readonly IBanciArchivoReader _archivoReader;
    private readonly IBanciCargaRepository _banciCargaRepository;
    private readonly CarpetasValidator _carpetasValidator;
    private readonly DelitosValidator _delitosValidator;
    private readonly VictimasValidator _victimasValidator;
    private readonly CargaIntegridadValidator _cargaIntegridadValidator;
    private readonly CatalogosValidator _catalogosValidator;
    private readonly BanciMetodologiaValidator _banciMetodologiaValidator;

    private static readonly HashSet<string> ClasificacionesPermitidas =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "2.07.01",
            "2.07.02",
            "2.07.03",
            "2.07.04",
            "2.08.01",
            "2.08.02",
            "2.08.03",
            "2.08.04"
        };

    public BanciCargaService(IBanciArchivoReader archivoReader, IBanciCargaRepository banciCargaRepository, CarpetasValidator carpetasValidator, DelitosValidator delitosValidator, VictimasValidator victimasValidator, CargaIntegridadValidator cargaIntegridadValidator, CatalogosValidator catalogosValidator, BanciMetodologiaValidator banciMetodologiaValidator)
    {
        _archivoReader = archivoReader;
        _banciCargaRepository = banciCargaRepository;
        _carpetasValidator = carpetasValidator;
        _delitosValidator = delitosValidator;
        _victimasValidator = victimasValidator;
        _cargaIntegridadValidator = cargaIntegridadValidator;
        _catalogosValidator = catalogosValidator;
        _banciMetodologiaValidator = banciMetodologiaValidator;
    }

    public async Task<BanciCargaValidacionResponse> ValidarArchivosAsync(BanciCargaArchivosRequest request, int idUsuarioCarga)
    {
        var response =
            new BanciCargaValidacionResponse
            {
                CodigoReferencia =
                    GenerarCodigoReferencia()
            };

        var usuario =
            await _banciCargaRepository
                .ObtenerUsuarioCargaAsync(
                    idUsuarioCarga);

        if (usuario == null)
        {
            response.Errores.Add(
                ErrorGeneral(
                    "BANCI_USUARIO_NO_HABILITADO",
                    "El usuario autenticado no existe, está inactivo o no tiene habilitado el módulo BANCI."));

            response.Mensaje =
                "El usuario no tiene acceso al módulo BANCI.";

            return response;
        }

        var lectura = await _archivoReader.LeerAsync(request);

        CompletarEntidad(lectura, usuario);
        NormalizarHoras(lectura);

        response.ModalidadIngreso = lectura.ModalidadIngreso;

        response.TotalCarpetas = lectura.Carpetas.Count;

        response.TotalDelitos = lectura.Delitos.Count;

        response.TotalVictimas = lectura.Victimas.Count;

        response.Errores.AddRange(lectura.Errores);

        if (response.Errores.Count > 0)
        {
            response.Mensaje =
                "No fue posible leer correctamente la estructura de los archivos BANCI.";

            return response;
        }

        ValidarRegistrosMinimos(lectura, response.Errores);

        AgregarErroresHeredados(response.Errores, _carpetasValidator.Validar(lectura.Carpetas, validarMesInmediatoAnterior: false));
        AgregarErroresHeredados(response.Errores, _delitosValidator.Validar(lectura.Delitos));
        AgregarErroresHeredados(response.Errores, _victimasValidator.ValidarBanci(lectura.Victimas));
        var erroresIntegridad = _cargaIntegridadValidator.Validar(lectura.Carpetas, lectura.Delitos, lectura.Victimas);

        if (usuario.EsSuperUsuario)
        {
            var advertenciasIntegridad = erroresIntegridad
                .Where(x => x.Codigo == "INTEGRIDAD_FECHA_HECHOS_MAYOR_FECHA_INICIO")
                .ToList();

            AgregarErroresHeredados(response.Advertencias, advertenciasIntegridad);

            erroresIntegridad = erroresIntegridad
                .Where(x => x.Codigo != "INTEGRIDAD_FECHA_HECHOS_MAYOR_FECHA_INICIO")
                .ToList();
        }

        AgregarErroresHeredados(response.Errores, erroresIntegridad);

        var erroresCatalogos = await _catalogosValidator.ValidarBanciAsync(lectura.Carpetas, lectura.Delitos, lectura.Victimas);
        AgregarErroresHeredados(response.Errores, erroresCatalogos);

        var validacionMetodologica = _banciMetodologiaValidator.Validar(lectura);
        response.Errores.AddRange(validacionMetodologica.Errores);
        response.Advertencias.AddRange(validacionMetodologica.Advertencias);

        if (response.Errores.Count == 0)
        {
            AgregarErroresHeredados(response.Advertencias, _delitosValidator.ValidarAdvertencias(lectura.Delitos));
            AgregarErroresHeredados(response.Advertencias, _cargaIntegridadValidator.ValidarAdvertencias(lectura.Delitos, lectura.Victimas));
        }

        var idEntidadFederativa =
                            await ResolverEntidadCargaAsync(
                usuario,
                lectura,
                response.Errores);

        if (response.Errores.Count > 0 || !idEntidadFederativa.HasValue)
        {
            response.Mensaje =
                $"Se encontraron {response.Errores.Count} errores en la información BANCI.";

            return response;
        }

        response.IdBanciCarga = await _banciCargaRepository
            .GuardarCargaValidadaAsync(
                idUsuarioCarga,
                idEntidadFederativa.Value,
                response.CodigoReferencia,
                lectura.ModalidadIngreso,
                lectura,
                response);

        response.Estado = "VALIDADO_PENDIENTE";
        response.TotalAdvertencias = response.Advertencias.Count;
        response.Mensaje = "Validación terminada. Revise las advertencias y acepte o rechace la carga. Todavía no se han integrado datos definitivos.";

        return response;
    }

    public Task<IReadOnlyList<BanciCargaValidacionResponse>> ObtenerPendientesAsync(int idUsuario) =>
        _banciCargaRepository.ObtenerPendientesAsync(idUsuario);

    public Task<BanciCargaValidacionResponse?> ObtenerCargaAsync(string codigoReferencia, int idUsuario) =>
        _banciCargaRepository.ObtenerCargaAsync(codigoReferencia, idUsuario);

    public Task<BanciCargaValidacionResponse> ConfirmarCargaAsync(string codigoReferencia, bool aceptar, int idUsuario) =>
        _banciCargaRepository.ConfirmarCargaAsync(codigoReferencia, aceptar, idUsuario);

    private static void CompletarEntidad(BanciLecturaArchivosResultado lectura, BanciUsuarioCargaInfo usuario)
    {
        string? entidad = usuario.IdEntidadFederativa?.ToString();

        if (string.IsNullOrWhiteSpace(entidad) && usuario.EsSuperUsuario)
        {
            var entidades = lectura.Delitos
                .Select(x => Valor(x, "id_ent_hchos"))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => int.TryParse(x, out var idEntidad) ? idEntidad : (int?)null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .Distinct()
                .ToList();

            if (entidades.Count == 1) entidad = entidades[0].ToString();
        }

        if (string.IsNullOrWhiteSpace(entidad)) return;

        foreach (var fila in lectura.Carpetas.Concat(lectura.Delitos).Concat(lectura.Victimas))
        {
            fila.Columnas["entidad"] = entidad;
        }
    }

    private static void NormalizarHoras(BanciLecturaArchivosResultado lectura)
    {
        NormalizarHora(lectura.Carpetas, "hra_de_ini");
        NormalizarHora(lectura.Delitos, "hra_de_hchos");
        NormalizarHora(lectura.Victimas, "hora_ultimo_contacto");
    }

    private static void NormalizarHora(IEnumerable<ArchivoFila> filas, string campo)
    {
        foreach (var fila in filas)
        {
            if (!fila.Columnas.TryGetValue(campo, out var valor) || string.IsNullOrWhiteSpace(valor)) continue;

            var hora = valor.Trim();

            if (hora.EndsWith(':')) hora += "00";

            fila.Columnas[campo] = hora;
        }
    }

    private static void ValidarRegistrosMinimos(BanciLecturaArchivosResultado lectura, List<BanciCargaValidacionError> errores)
    {
        if (lectura.Carpetas.Count == 0) errores.Add(ErrorGeneral("BANCI_SIN_CARPETAS", "No se encontraron registros de carpetas."));
        if (lectura.Delitos.Count == 0) errores.Add(ErrorGeneral("BANCI_SIN_DELITOS", "No se encontraron registros de delitos."));
        if (lectura.Victimas.Count == 0) errores.Add(ErrorGeneral("BANCI_SIN_VICTIMAS", "No se encontraron registros de víctimas."));
    }

    private static string Valor(ArchivoFila fila, string columna)
    {
        return fila.Columnas.TryGetValue(
                columna,
                out var valor)
            ? valor?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static string Llave(params string[] valores)
    {
        return string.Join(
            "\u001F",
            valores.Select(x =>
                x.Trim().ToUpperInvariant()));
    }

    private static void AgregarErroresHeredados(List<BanciCargaValidacionError> destino, IEnumerable<CargaValidacionError> origen)
    {
        destino.AddRange(origen.Select(x => new BanciCargaValidacionError
        {
            Archivo = x.Archivo,
            NumeroFila = x.Fila,
            Campo = string.IsNullOrWhiteSpace(x.Campo) ? x.Columna : x.Campo,
            Valor = x.Valor,
            Codigo = x.Codigo,
            Mensaje = x.Mensaje
        }));
    }

    private async Task<int?> ResolverEntidadCargaAsync(BanciUsuarioCargaInfo usuario, BanciLecturaArchivosResultado lectura, List<BanciCargaValidacionError> errores)
    {
        var valoresEntidad = lectura.Carpetas.Concat(lectura.Delitos).Concat(lectura.Victimas).Select(x => Valor(x, "entidad")).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (valoresEntidad.Count == 0)
        {
            errores.Add(ErrorGeneral("BANCI_ENTIDAD_NO_INFORMADA", "No fue posible determinar la entidad federativa que reporta la información."));
            return null;
        }

        var entidadesResueltas = new HashSet<int>();

        foreach (var valor in valoresEntidad)
        {
            var idEntidad = await _banciCargaRepository.ResolverEntidadFederativaAsync(valor);

            if (!idEntidad.HasValue)
            {
                errores.Add(new BanciCargaValidacionError
                {
                    Archivo = "general",
                    Campo = "entidad",
                    Valor = valor,
                    Codigo = "BANCI_ENTIDAD_NO_RECONOCIDA",
                    Mensaje = $"No fue posible reconocer la entidad federativa \"{valor}\"."
                });

                continue;
            }

            entidadesResueltas.Add(idEntidad.Value);
        }

        if (errores.Count > 0) return null;

        if (entidadesResueltas.Count != 1)
        {
            errores.Add(ErrorGeneral("BANCI_MULTIPLES_ENTIDADES", "Una carga BANCI sólo puede contener información reportada por una entidad federativa."));
            return null;
        }

        var idEntidadCarga = entidadesResueltas.Single();

        if (usuario.IdEntidadFederativa.HasValue && usuario.IdEntidadFederativa.Value != idEntidadCarga)
        {
            errores.Add(ErrorGeneral("BANCI_ENTIDAD_NO_CORRESPONDE_USUARIO", "La entidad informada en los archivos no corresponde a la entidad del usuario autenticado."));
            return null;
        }

        return idEntidadCarga;
    }

    private static string GenerarCodigoReferencia() => $"BANCI-{Guid.NewGuid():N}";

    private static BanciCargaValidacionError ErrorGeneral(string codigo, string mensaje)
    {
        return new BanciCargaValidacionError
        {
            Archivo = "general",
            Codigo = codigo,
            Mensaje = mensaje
        };
    }
}
