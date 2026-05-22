using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class UsuarioService : IUsuarioService
{
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly ILogger<UsuarioService> _logger;

    public UsuarioService(IUsuarioRepository usuarioRepository, ILogger<UsuarioService> logger)
    {
        _usuarioRepository = usuarioRepository;
        _logger = logger;
    }

    public async Task<CrearUsuarioResponse> CrearUsuarioAsync(CrearUsuarioRequest request, int idUsuarioAlta)
    {
        var usuarioAlta = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioAlta);

        if (usuarioAlta == null)
        {
            return new CrearUsuarioResponse
            {
                EsValido = false,
                Codigo = "USUARIO_ALTA_NO_VALIDO",
                Mensaje = "El usuario autenticado no existe o no está activo."
            };
        }

        if (!usuarioAlta.EsSuperUsuario)
        {
            return new CrearUsuarioResponse
            {
                EsValido = false,
                Codigo = "USUARIO_ALTA_SIN_PERMISO",
                Mensaje = "Solo un SUPER_USUARIO puede registrar usuarios."
            };
        }

        var rol = request.Rol.Trim().ToUpperInvariant();

        var errorCampos = ValidarCamposObligatorios(request, rol);

        if (errorCampos != null)
        {
            return errorCampos;
        }

        var idRol = await _usuarioRepository.ObtenerIdRolActivoAsync(rol);

        if (!idRol.HasValue)
        {
            return new CrearUsuarioResponse
            {
                EsValido = false,
                Codigo = "USUARIO_ROL_INVALIDO",
                Mensaje = $"El rol {rol} no existe o no está activo."
            };
        }

        if (rol != "SUPER_USUARIO")
        {
            if (!request.IdEntidadFederativa.HasValue)
            {
                return new CrearUsuarioResponse
                {
                    EsValido = false,
                    Codigo = "USUARIO_ENTIDAD_OBLIGATORIA",
                    Mensaje = "El usuario debe tener entidad federativa para el rol seleccionado."
                };
            }

            var existeEntidad = await _usuarioRepository.ExisteEntidadActivaAsync(request.IdEntidadFederativa.Value);

            if (!existeEntidad)
            {
                return new CrearUsuarioResponse
                {
                    EsValido = false,
                    Codigo = "USUARIO_ENTIDAD_INVALIDA",
                    Mensaje = "La entidad federativa indicada no existe o no está activa."
                };
            }
        }

        if (rol == "CONSULTA")
        {
            request.HabilitaCarga = false;
            request.HabilitaModificacion = false;
        }

        var duplicado = await _usuarioRepository.ObtenerDuplicadoUsuarioAsync(
            request.Usuario,
            request.CorreoElectronico,
            request.Rfc,
            request.Curp);

        if (!string.IsNullOrWhiteSpace(duplicado))
        {
            return new CrearUsuarioResponse
            {
                EsValido = false,
                Codigo = $"USUARIO_{duplicado}_DUPLICADO",
                Mensaje = $"Ya existe un usuario registrado con ese dato: {duplicado}."
            };
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        var idUsuario = await _usuarioRepository.CrearUsuarioAsync(
            request,
            idRol.Value,
            passwordHash,
            idUsuarioAlta);

        _logger.LogInformation(
            "Usuario registrado correctamente. IdUsuario: {IdUsuario}, Usuario: {Usuario}, Rol: {Rol}, UsuarioAlta: {IdUsuarioAlta}",
            idUsuario,
            request.Usuario,
            rol,
            idUsuarioAlta);

        return new CrearUsuarioResponse
        {
            EsValido = true,
            Codigo = "USUARIO_REGISTRADO",
            Mensaje = "Usuario registrado correctamente.",
            IdUsuario = idUsuario
        };
    }

    private static CrearUsuarioResponse? ValidarCamposObligatorios(CrearUsuarioRequest request, string rol)
    {
        if (string.IsNullOrWhiteSpace(request.Usuario))
        {
            return Error("USUARIO_CAMPO_OBLIGATORIO", "Debe enviar el usuario.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Error("USUARIO_PASSWORD_OBLIGATORIO", "Debe enviar la contraseña.");
        }

        if (request.Password.Length < 8)
        {
            return Error("USUARIO_PASSWORD_CORTO", "La contraseña debe tener al menos 8 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            return Error("USUARIO_NOMBRE_OBLIGATORIO", "Debe enviar el nombre.");
        }

        if (string.IsNullOrWhiteSpace(request.PrimerApellido))
        {
            return Error("USUARIO_PRIMER_APELLIDO_OBLIGATORIO", "Debe enviar el primer apellido.");
        }

        if (string.IsNullOrWhiteSpace(request.CorreoElectronico))
        {
            return Error("USUARIO_CORREO_OBLIGATORIO", "Debe enviar el correo electrónico.");
        }

        if (string.IsNullOrWhiteSpace(request.Rfc))
        {
            return Error("USUARIO_RFC_OBLIGATORIO", "Debe enviar el RFC.");
        }

        if (string.IsNullOrWhiteSpace(request.Curp))
        {
            return Error("USUARIO_CURP_OBLIGATORIO", "Debe enviar la CURP.");
        }

        if (string.IsNullOrWhiteSpace(rol))
        {
            return Error("USUARIO_ROL_OBLIGATORIO", "Debe enviar el rol.");
        }

        if (rol != "SUPER_USUARIO" && rol != "ENLACE_ESTATAL" && rol != "CONSULTA")
        {
            return Error("USUARIO_ROL_NO_PERMITIDO", "El rol permitido debe ser SUPER_USUARIO, ENLACE_ESTATAL o CONSULTA.");
        }

        return null;
    }

    private static CrearUsuarioResponse Error(string codigo, string mensaje)
    {
        return new CrearUsuarioResponse
        {
            EsValido = false,
            Codigo = codigo,
            Mensaje = mensaje
        };
    }
}