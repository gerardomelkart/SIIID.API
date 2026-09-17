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
        var usuario = await ObtenerUsuarioCapturaAsync(idUsuarioCarga);
        var lectura = await _archivoReader.LeerAsync(request);
        return await ValidarLecturaAsync(lectura, usuario);
    }

    public async Task<BanciFormularioOpciones> ObtenerFormularioOpcionesAsync(int idUsuario)
    {
        var usuario = await ObtenerUsuarioCapturaAsync(idUsuario);
        return new BanciFormularioOpciones { EsSuperUsuario = usuario.EsSuperUsuario, IdEntidadFederativa = usuario.IdEntidadFederativa, Catalogos = await _banciCargaRepository.ObtenerFormularioCatalogosAsync() };
    }

    private async Task<BanciUsuarioCargaInfo> ObtenerUsuarioCapturaAsync(int idUsuario)
    {
        var usuario = await _banciCargaRepository.ObtenerUsuarioCargaAsync(idUsuario);
        if (usuario == null || (!usuario.EsSuperUsuario && (!string.Equals(usuario.Rol, "ENLACE_ESTATAL", StringComparison.OrdinalIgnoreCase) || usuario.IdEntidadFederativa is not (>= 1 and <= 32))))
            throw new UnauthorizedAccessException("El usuario no tiene permiso para capturar información BANCI.");
        return usuario;
    }

    public async Task<BanciCargaValidacionResponse> ValidarFormularioAsync(BanciFormularioRequest request, int idUsuario)
    {
        var usuario = await ObtenerUsuarioCapturaAsync(idUsuario);
        var lectura = new BanciLecturaArchivosResultado { ModalidadIngreso = "FORMULARIO" };
        var entidad = usuario.EsSuperUsuario ? request.IdEntidadFederativa : usuario.IdEntidadFederativa;
        if (!usuario.EsSuperUsuario && request.IdEntidadFederativa.HasValue && request.IdEntidadFederativa != entidad)
            throw new UnauthorizedAccessException("Sólo puede capturar información de su entidad federativa.");
        if (entidad is not (>= 1 and <= 32) || await _banciCargaRepository.ResolverEntidadFederativaAsync(entidad.Value.ToString()) != entidad)
            throw new ArgumentException("Seleccione una entidad federativa válida.");
        if (request.Carpeta == null || request.Delitos == null || request.Delitos.Count is < 1 or > 100 || request.Delitos.Any(d => d == null || d.Datos == null || d.Victimas == null || d.Victimas.Count is < 1 or > 500 || d.Victimas.Any(v => v == null)) || request.Delitos.Sum(d => d.Victimas.Count) > 1000)
            throw new ArgumentException("Capture una carpeta, de 1 a 100 delitos y al menos una víctima por delito (máximo 1000 víctimas por formulario).");

        var carpeta = CrearFilaFormulario(request.Carpeta, BanciArchivoReader.ColumnasCarpetas, 1, "entidad");
        lectura.Carpetas.Add(carpeta);
        var numeroVictima = 0;
        for (var i = 0; i < request.Delitos.Count; i++)
        {
            var captura = request.Delitos[i];
            var delito = CrearFilaFormulario(captura.Datos, BanciArchivoReader.ColumnasDelitos, i + 1, "entidad", "id_ci");
            delito.Columnas["id_ci"] = Valor(carpeta, "id_ci");
            lectura.Delitos.Add(delito);
            foreach (var datosVictima in captura.Victimas)
            {
                var victima = CrearFilaFormulario(datosVictima, BanciArchivoReader.ColumnasVictimas, ++numeroVictima, "entidad", "id_ci", "id_delito", "no_banci");
                victima.Columnas["id_ci"] = Valor(carpeta, "id_ci");
                victima.Columnas["id_delito"] = Valor(delito, "id_delito");
                lectura.Victimas.Add(victima);
            }
        }
        if (lectura.Delitos.Select(d => Valor(d, "id_delito")).Distinct(StringComparer.OrdinalIgnoreCase).Count() != lectura.Delitos.Count)
            lectura.Errores.Add(ErrorGeneral("BANCI_DELITO_DUPLICADO", "No repita el ID_DELITO dentro de la carpeta."));
        if (lectura.Victimas.Select(v => Llave(Valor(v, "id_delito"), Valor(v, "id_vicf"))).Distinct().Count() != lectura.Victimas.Count)
            lectura.Errores.Add(ErrorGeneral("BANCI_VICTIMA_DUPLICADA", "No repita el ID_VICF dentro de un mismo delito."));
        if (await _banciCargaRepository.ExisteCarpetaAsync(entidad.Value, Valor(carpeta, "id_ci")))
            lectura.Errores.Add(ErrorGeneral("BANCI_CARPETA_EXISTENTE", "Ya existe ese ID_CI en su entidad. El formulario registra carpetas nuevas; no cambie la fecha para intentar registrarla otra vez."));

        // La entidad de reporte no procede de campos editables de cada fila.
        var contexto = new BanciUsuarioCargaInfo { IdUsuario = usuario.IdUsuario, Rol = usuario.Rol, IdEntidadFederativa = entidad };
        return await ValidarLecturaAsync(lectura, contexto);
    }

    private static readonly Dictionary<string, int> LongitudesFormulario = new(StringComparer.OrdinalIgnoreCase)
    {
        ["id_ci"] = 250,
        ["ntra_ci"] = 250,
        ["id_delito"] = 250,
        ["dto"] = 500,
        ["moda_dto"] = 2000,
        ["clasf_de_dto"] = 10,
        ["nom_ent_hchos"] = 100,
        ["nom_mun_hchos"] = 250,
        ["id_mun_hchos"] = 5,
        ["nom_loc_hchos"] = 500,
        ["id_loc_hchos"] = 250,
        ["nom_col_hchos"] = 500,
        ["id_col_hchos"] = 250,
        ["cp"] = 10,
        ["id_vicf"] = 250,
        ["nacional"] = 5,
        ["folio_fotovolante"] = 250,
        ["folio_rnpdno"] = 250,
        ["pro_apellido"] = 250,
        ["sdo_apellido"] = 250,
        ["nomb"] = 500,
        ["entidad_nacimiento"] = 250,
        ["estado_migratorio"] = 500,
        ["curp"] = 50,
        ["rfc"] = 13,
        ["entidad_visto"] = 250,
        ["municipio_visto"] = 250,
        ["delito"] = 1000,
    };

    private static ArchivoFila CrearFilaFormulario(Dictionary<string, string?> datos, string[] columnas, int numeroFila, params string[] administradas)
    {
        var permitidas = columnas.Except(administradas).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fila = new ArchivoFila { NumeroFila = numeroFila };
        foreach (var columna in columnas) fila.Columnas[columna] = null;
        var recibidas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dato in datos)
        {
            if (!permitidas.Contains(dato.Key) || !recibidas.Add(dato.Key)) throw new ArgumentException($"El campo {dato.Key} no se admite o está repetido en el formulario.");
            if (LongitudesFormulario.TryGetValue(dato.Key, out var maximo) && dato.Value?.Length > maximo) throw new ArgumentException($"El campo {dato.Key} excede {maximo} caracteres.");
            if (dato.Value?.Length > 20000) throw new ArgumentException($"El campo {dato.Key} excede 20000 caracteres.");
            fila.Columnas[dato.Key] = dato.Value?.Trim();
        }
        if (int.TryParse(Valor(fila, "dic"), out var dic) && dic > 255) throw new ArgumentException("El campo dic no puede ser mayor a 255.");
        return fila;
    }

    private async Task<BanciCargaValidacionResponse> ValidarLecturaAsync(BanciLecturaArchivosResultado lectura, BanciUsuarioCargaInfo usuario)
    {
        var idUsuarioCarga = usuario.IdUsuario;
        var response = new BanciCargaValidacionResponse { CodigoReferencia = GenerarCodigoReferencia() };
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
                "Revise los errores de la información BANCI antes de continuar.";

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
