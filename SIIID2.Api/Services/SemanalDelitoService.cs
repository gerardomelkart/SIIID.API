using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class SemanalDelitoService : ISemanalDelitoService
{
    private const string ClaveExtorsion = "4.04";

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

        var delitos = await _semanalDelitoRepository.ObtenerConfiguracionAsync();

        return RespuestaCorrecta(delitos, "Configuración semanal consultada correctamente.");
    }

    public async Task<ConfiguracionDelitosSemanalesResponse> GuardarConfiguracionAsync(ActualizarConfiguracionDelitosSemanalesRequest request, int idUsuario)
    {
        if (!await _semanalDelitoRepository.PuedeAdministrarDelitosAsync(idUsuario)) return SinPermiso();

        var catalogo = await _semanalDelitoRepository.ObtenerConfiguracionAsync();
        var extorsiones = catalogo.Where(x => x.Clave == ClaveExtorsion).ToList();

        if (extorsiones.Count != 1) return Error("SEMANAL_EXTORSION_NO_ENCONTRADA", "No se encontró una única definición activa de Extorsión con clave 4.04.");

        var extorsion = extorsiones[0];
        var solicitudes = request.Delitos ?? new List<ConfiguracionDelitoSemanalRequest>();
        var duplicados = solicitudes.GroupBy(x => x.IdDelito).Where(x => x.Count() > 1).Select(x => x.Key).ToList();

        if (duplicados.Count > 0) return Error("SEMANAL_DELITOS_DUPLICADOS", "La solicitud contiene delitos repetidos.");

        var catalogoPorId = catalogo.ToDictionary(x => x.IdDelito);

        if (solicitudes.Any(x => !catalogoPorId.ContainsKey(x.IdDelito))) return Error("SEMANAL_DELITO_INVALIDO", "La solicitud contiene un delito inexistente o inactivo.");

        var seleccionados = solicitudes
            .Where(x => x.Seleccionado && x.IdDelito != extorsion.IdDelito)
            .OrderBy(x => x.Orden <= 0 ? short.MaxValue : x.Orden)
            .ThenBy(x => catalogoPorId[x.IdDelito].Clave)
            .Select(x => new ConfiguracionDelitoSemanalItem
            {
                IdDelito = x.IdDelito,
                Clave = catalogoPorId[x.IdDelito].Clave,
                Delito = catalogoPorId[x.IdDelito].Delito,
                BienJuridico = catalogoPorId[x.IdDelito].BienJuridico,
                Seleccionado = true,
                EsObligatorio = false,
                ConservarEntrePeriodos = false
            })
            .ToList();

        extorsion.Seleccionado = true;
        extorsion.EsObligatorio = true;
        extorsion.ConservarEntrePeriodos = true;
        extorsion.Orden = 1;

        for (var indice = 0; indice < seleccionados.Count; indice++) seleccionados[indice].Orden = checked((short)(indice + 2));

        seleccionados.Insert(0, extorsion);

        await _semanalDelitoRepository.GuardarConfiguracionAsync(seleccionados, idUsuario);

        _logger.LogInformation("Configuración semanal de delitos actualizada. Total: {Total}, UsuarioModificacion: {IdUsuario}", seleccionados.Count, idUsuario);

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

    private static ConfiguracionDelitosSemanalesResponse RespuestaCorrecta(List<ConfiguracionDelitoSemanalItem> delitos, string mensaje) => new()
    {
        EsValido = true,
        Codigo = "SEMANAL_CONFIGURACION_DELITOS_OK",
        Mensaje = mensaje,
        TotalSeleccionados = delitos.Count(x => x.Seleccionado),
        Delitos = delitos
    };
}