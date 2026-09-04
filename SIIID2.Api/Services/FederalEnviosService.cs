using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class FederalEnviosService : IFederalEnviosService
{
    private readonly IFederalEnviosRepository _federalEnviosRepository;
    private readonly IFederalCargaRepository _federalCargaRepository;

    public FederalEnviosService(IFederalEnviosRepository federalEnviosRepository, IFederalCargaRepository federalCargaRepository)
    {
        _federalEnviosRepository = federalEnviosRepository;
        _federalCargaRepository = federalCargaRepository;
    }

    public async Task<List<InformePeriodoItem>> ObtenerPeriodosAsync(int idUsuarioConsulta)
    {
        var usuario = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);
        if (usuario == null) return [];

        return await _federalEnviosRepository.ObtenerPeriodosAsync();
    }

    public async Task<List<InformeEnvioItem>> ObtenerEnviosAsync(int idUsuarioConsulta, int? mesCorte, int? anioCorte)
    {
        var usuario = await _federalCargaRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);
        if (usuario == null) return [];

        return await _federalEnviosRepository.ObtenerEnviosAsync(mesCorte, anioCorte);
    }
}