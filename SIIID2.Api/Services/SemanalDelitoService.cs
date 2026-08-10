using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class SemanalDelitoService : ISemanalDelitoService
{
    private const string ClaveDelitoExtorsion = "4.04";
    private readonly ISemanalDelitoRepository _semanalDelitoRepository;
    private readonly ILogger<SemanalDelitoService> _logger;

    public SemanalDelitoService(ISemanalDelitoRepository semanalDelitoRepository, ILogger<SemanalDelitoService> logger)
    {
        _semanalDelitoRepository = semanalDelitoRepository;
        _logger = logger;
    }

    public async Task<ConfiguracionDelitosSemanalesResponse> ObtenerConfiguracionAsync(int idUsuario)
    {
        if (!await _semanalDelitoRepository.PuedeAdministrarDelitosAsync(idUsuario)) return SinPermiso();

        var modalidades = await _semanalDelitoRepository.ObtenerConfiguracionAsync();

        return RespuestaCorrecta(modalidades, "Configuración semanal consultada correctamente.");
    }

    public async Task<DelitosSemanalesHabilitadosResponse> ObtenerDelitosHabilitadosAsync()
    {
        var delitos = await _semanalDelitoRepository.ObtenerDelitosHabilitadosAsync();

        return new DelitosSemanalesHabilitadosResponse
        {
            EsValido = true,
            Codigo = "SEMANAL_DELITOS_HABILITADOS_OK",
            Mensaje = "Delitos habilitados consultados correctamente.",
            Delitos = delitos
        };
    }

    public async Task<ConfiguracionDelitosSemanalesResponse> GuardarConfiguracionAsync(ActualizarConfiguracionDelitosSemanalesRequest request, int idUsuario)
    {
        if (!await _semanalDelitoRepository.PuedeAdministrarDelitosAsync(idUsuario)) return SinPermiso();

        var catalogo = await _semanalDelitoRepository.ObtenerConfiguracionAsync();
        var extorsiones = catalogo.Where(x => x.ClaveDelito == ClaveDelitoExtorsion).ToList();

        if (extorsiones.Count == 0) return Error("SEMANAL_EXTORSION_NO_ENCONTRADA", "No se encontraron modalidades activas de Extorsión para la clave 4.04.");

        var solicitudes = request.Modalidades ?? new List<ConfiguracionModalidadSemanalRequest>();
        var duplicados = solicitudes.GroupBy(x => x.IdModalidadDelito).Where(x => x.Count() > 1).Select(x => x.Key).ToList();

        if (duplicados.Count > 0) return Error("SEMANAL_MODALIDADES_DUPLICADAS", "La solicitud contiene modalidades repetidas.");

        var catalogoPorId = catalogo.ToDictionary(x => x.IdModalidadDelito);

        if (solicitudes.Any(x => !catalogoPorId.ContainsKey(x.IdModalidadDelito))) return Error("SEMANAL_MODALIDAD_INVALIDA", "La solicitud contiene una modalidad inexistente o inactiva.");

        var idsExtorsion = extorsiones.Select(x => x.IdModalidadDelito).ToHashSet();
        var seleccionadas = solicitudes.Where(x => x.Seleccionado && !idsExtorsion.Contains(x.IdModalidadDelito)).Select(x => catalogoPorId[x.IdModalidadDelito]).OrderBy(x => x.ClaveModalidad).ToList();

        foreach (var modalidad in seleccionadas)
        {
            modalidad.Seleccionado = true;
            modalidad.EsObligatorio = false;
            modalidad.ConservarEntrePeriodos = false;
        }

        foreach (var extorsion in extorsiones)
        {
            extorsion.Seleccionado = true;
            extorsion.EsObligatorio = true;
            extorsion.ConservarEntrePeriodos = true;
        }

        seleccionadas.AddRange(extorsiones);
        seleccionadas = seleccionadas.OrderBy(x => x.ClaveModalidad).ToList();

        for (var indice = 0; indice < seleccionadas.Count; indice++) seleccionadas[indice].Orden = checked((short)(indice + 1));

        await _semanalDelitoRepository.GuardarConfiguracionAsync(seleccionadas, idUsuario);

        _logger.LogInformation("Configuración semanal de modalidades actualizada. Total: {Total}, UsuarioModificacion: {IdUsuario}", seleccionadas.Count, idUsuario);

        var resultado = await _semanalDelitoRepository.ObtenerConfiguracionAsync();

        return RespuestaCorrecta(resultado, "Configuración semanal guardada correctamente.");
    }

    private static ConfiguracionDelitosSemanalesResponse SinPermiso() => Error("SEMANAL_ADMINISTRACION_DELITOS_SIN_PERMISO", "El usuario no tiene permiso para administrar los delitos del módulo semanal.");

    private static ConfiguracionDelitosSemanalesResponse Error(string codigo, string mensaje) => new()
    {
        EsValido = false,
        Codigo = codigo,
        Mensaje = mensaje
    };

    private static ConfiguracionDelitosSemanalesResponse RespuestaCorrecta(List<ConfiguracionModalidadSemanalItem> modalidades, string mensaje) => new()
    {
        EsValido = true,
        Codigo = "SEMANAL_CONFIGURACION_DELITOS_OK",
        Mensaje = mensaje,
        TotalSeleccionados = modalidades.Count(x => x.Seleccionado),
        Modalidades = modalidades
    };
}