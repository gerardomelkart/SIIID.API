using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class AdministracionCargasService : IAdministracionCargasService
{
    private readonly IAdministracionCargasRepository _administracionRepository;

    private readonly IUsuarioRepository _usuarioRepository;

    private readonly ICargaRepository _cargaRepository;

    private readonly IActualizacionRepository _actualizacionRepository;

    public AdministracionCargasService(IAdministracionCargasRepository administracionRepository, IUsuarioRepository usuarioRepository, ICargaRepository cargaRepository, IActualizacionRepository actualizacionRepository)
    {
        _administracionRepository = administracionRepository;

        _usuarioRepository = usuarioRepository;

        _cargaRepository = cargaRepository;

        _actualizacionRepository =  actualizacionRepository;
    }

    public async Task<List<CargaPendienteAdministracionItem>> ObtenerPendientesAsync(int idUsuario)
    {
        await ValidarSuperUsuarioAsync(idUsuario);

        return await _administracionRepository.ObtenerPendientesAsync();
    }

    public async Task<CargaPendienteAdministracionDetalle?> ObtenerDetalleAsync(int idUsuario, string codigoReferencia)
    {
        await ValidarSuperUsuarioAsync(idUsuario);

        return await _administracionRepository.ObtenerDetalleAsync(codigoReferencia);
    }

    public async Task<ConfirmarCargaResponse> AprobarAsync(int idUsuario, string codigoReferencia)
    {
        await ValidarSuperUsuarioAsync(idUsuario);

        var carga = await _administracionRepository.ObtenerReferenciaAsync(codigoReferencia);

        if (carga == null)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                CodigoReferencia = codigoReferencia,
                Estado = "NO_ENCONTRADA",
                Mensaje =
                    "No se encontro una carga con ese codigo de referencia."
            };
        }

        if (!string.Equals(
                carga.Estado,
                "PENDIENTE_APROBACION",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                CodigoReferencia = codigoReferencia,
                Estado = carga.Estado,
                Mensaje =
                    $"La carga ya fue resuelta o no esta pendiente de aprobacion. Estado actual: {carga.Estado}."
            };
        }

        if (string.Equals(
                carga.TipoCarga,
                "CARGA_INICIAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return await _cargaRepository
                .AprobarCargaPendienteAsync(
                    codigoReferencia,
                    idUsuario);
        }

        if (string.Equals(
                carga.TipoCarga,
                "ACTUALIZACION",
                StringComparison.OrdinalIgnoreCase))
        {
            return await _actualizacionRepository
                .AprobarActualizacionPendienteAsync(
                    codigoReferencia,
                    idUsuario);
        }

        return new ConfirmarCargaResponse
        {
            EsValido = false,
            CodigoReferencia = codigoReferencia,
            Estado = "TIPO_CARGA_INVALIDO",
            Mensaje = "El tipo de carga no es valido."
        };
    }

    public async Task<ConfirmarCargaResponse> RechazarAsync(int idUsuario, string codigoReferencia, string motivo)
    {
        await ValidarSuperUsuarioAsync(idUsuario);

        var motivoLimpio = motivo?.Trim() ?? string.Empty;

        if (motivoLimpio.Length < 5)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                CodigoReferencia = codigoReferencia,
                Estado = "MOTIVO_INVALIDO",
                Mensaje =
                    "Debe capturar un motivo de rechazo valido."
            };
        }

        var carga = await _administracionRepository
            .ObtenerReferenciaAsync(codigoReferencia);

        if (carga == null)
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                CodigoReferencia = codigoReferencia,
                Estado = "NO_ENCONTRADA",
                Mensaje =
                    "No se encontro una carga con ese codigo de referencia."
            };
        }

        if (!string.Equals(
                carga.Estado,
                "PENDIENTE_APROBACION",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ConfirmarCargaResponse
            {
                EsValido = false,
                CodigoReferencia = codigoReferencia,
                Estado = carga.Estado,
                Mensaje =
                    $"La carga ya fue resuelta o no esta pendiente de aprobacion. Estado actual: {carga.Estado}."
            };
        }

        if (string.Equals(
                carga.TipoCarga,
                "CARGA_INICIAL",
                StringComparison.OrdinalIgnoreCase))
        {
            return await _cargaRepository
                .RechazarCargaPendienteAsync(
                    codigoReferencia,
                    idUsuario,
                    motivoLimpio);
        }

        if (string.Equals(
                carga.TipoCarga,
                "ACTUALIZACION",
                StringComparison.OrdinalIgnoreCase))
        {
            return await _actualizacionRepository
                .RechazarActualizacionPendienteAsync(
                    codigoReferencia,
                    idUsuario,
                    motivoLimpio);
        }

        return new ConfirmarCargaResponse
        {
            EsValido = false,
            CodigoReferencia = codigoReferencia,
            Estado = "TIPO_CARGA_INVALIDO",
            Mensaje = "El tipo de carga no es valido."
        };
    }

    private async Task ValidarSuperUsuarioAsync(int idUsuario)
    {
        var usuario = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuario);

        if (usuario == null || !usuario.EsSuperUsuario)
        {
            throw new UnauthorizedAccessException("Solo un superusuario puede revisar y resolver cargas pendientes.");
        }
    }

    public async Task<CargaReferenciaAdministracionInfo?>ObtenerReferenciaAsync(int idUsuario, string codigoReferencia)
    {
        await ValidarSuperUsuarioAsync(idUsuario);
        return await _administracionRepository.ObtenerReferenciaAsync(codigoReferencia);
    }
}