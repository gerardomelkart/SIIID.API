using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class BanciConsultaService : IBanciConsultaService
{
    private readonly IBanciConsultaRepository _consultaRepository;
    private readonly IBanciCargaRepository _cargaRepository;

    public BanciConsultaService(IBanciConsultaRepository consultaRepository, IBanciCargaRepository cargaRepository)
    {
        _consultaRepository = consultaRepository;
        _cargaRepository = cargaRepository;
    }

    private async Task<int?> ObtenerAlcanceAsync(int idUsuario)
    {
        // ObtenerUsuarioCargaAsync verifica usuario/rol activos y acceso vigente MENSUAL/BANCI.
        var usuario = await _cargaRepository.ObtenerUsuarioCargaAsync(idUsuario);
        if (usuario == null) throw new UnauthorizedAccessException();

        if (usuario.EsSuperUsuario) return null;
        if (usuario.Rol == "CONSULTA" && !usuario.IdEntidadFederativa.HasValue)
            return null; // Mantiene la regla de consulta nacional existente en SIIID2.

        if (usuario.Rol is not ("ENLACE_ESTATAL" or "CONSULTA") ||
            usuario.IdEntidadFederativa is not (>= 1 and <= 32))
            throw new UnauthorizedAccessException();

        return usuario.IdEntidadFederativa.Value;
    }

    public async Task<BanciConsultaOpciones> ObtenerOpcionesAsync(int idUsuario)
    {
        var alcance = await ObtenerAlcanceAsync(idUsuario);
        return await _consultaRepository.ObtenerOpcionesAsync(alcance);
    }

    public async Task<BanciConsultaResultado> ConsultarAsync(int idUsuario, BanciConsultaFiltro filtro)
    {
        var alcance = await ObtenerAlcanceAsync(idUsuario);
        if (alcance.HasValue && filtro.IdEntidadFederativa.HasValue && filtro.IdEntidadFederativa != alcance)
            throw new UnauthorizedAccessException();

        // El alcance autenticado es independiente del filtro que envía el navegador.
        return await _consultaRepository.ConsultarAsync(filtro, alcance);
    }

    public async Task<BanciConsultaDetalle?> ObtenerDetalleAsync(int idUsuario, long idCarpeta)
    {
        var alcance = await ObtenerAlcanceAsync(idUsuario);
        return await _consultaRepository.ObtenerDetalleAsync(idCarpeta, alcance);
    }
}
