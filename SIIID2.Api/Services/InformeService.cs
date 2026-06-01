using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class InformeService : IInformeService
{
    private readonly IInformeRepository _informeRepository;
    private readonly IUsuarioRepository _usuarioRepository;

    public InformeService(IInformeRepository informeRepository, IUsuarioRepository usuarioRepository)
    {
        _informeRepository = informeRepository;
        _usuarioRepository = usuarioRepository;
    }

    public async Task<List<InformeEnvioItem>> ObtenerEnviosAsync(int idUsuarioConsulta, int? idEntidadFederativa, int? mesCorte, int? anioCorte)
    {
        var usuarioConsulta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioConsulta);

        if (usuarioConsulta == null)
        {
            return new List<InformeEnvioItem>();
        }

        if (!usuarioConsulta.EsSuperUsuario && !usuarioConsulta.IdEntidadFederativa.HasValue)
        {
            return new List<InformeEnvioItem>();
        }

        return await _informeRepository.ObtenerEnviosAsync(
            usuarioConsulta.EsSuperUsuario,
            usuarioConsulta.IdEntidadFederativa,
            idEntidadFederativa,
            mesCorte,
            anioCorte);
    }
}