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

        var duplicados = await _usuarioRepository.ObtenerDuplicadosUsuarioAsync(
            request.Usuario,
            request.CorreoElectronico,
            request.Rfc,
            request.Curp);

        if (duplicados.Count > 0)
        {
            return new CrearUsuarioResponse
            {
                EsValido = false,
                Codigo = "USUARIO_DATOS_DUPLICADOS",
                Mensaje = "Existen datos duplicados. Revise los campos marcados.",
                Errores = duplicados
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

    public async Task<List<UsuarioListadoItem>> ObtenerUsuariosAsync(bool incluirInactivos)
    {
        return await _usuarioRepository.ObtenerUsuariosAsync(incluirInactivos);
    }

    public async Task<UsuarioDetalleResponse> ObtenerUsuarioDetalleAsync(int idUsuario)
    {
        var usuario = await _usuarioRepository.ObtenerUsuarioDetalleAsync(idUsuario);

        if (usuario == null)
        {
            return new UsuarioDetalleResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_EXISTE",
                Mensaje = "El usuario solicitado no existe.",
                Usuario = null
            };
        }

        return new UsuarioDetalleResponse
        {
            EsValido = true,
            Codigo = "USUARIO_ENCONTRADO",
            Mensaje = "Usuario encontrado.",
            Usuario = usuario
        };
    }

    public async Task<UsuarioOperacionResponse> EditarUsuarioAsync(int idUsuario, EditarUsuarioRequest request, int idUsuarioModificacion)
    {
        var usuarioModificacion = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioModificacion);

        if (usuarioModificacion == null)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_MODIFICACION_NO_VALIDO",
                Mensaje = "El usuario autenticado no existe o no está activo.",
                IdUsuario = idUsuario
            };
        }

        if (!usuarioModificacion.EsSuperUsuario)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_MODIFICACION_SIN_PERMISO",
                Mensaje = "Solo un SUPER_USUARIO puede editar usuarios.",
                IdUsuario = idUsuario
            };
        }

        var existeUsuario = await _usuarioRepository.ExisteUsuarioActivoAsync(idUsuario);

        if (!existeUsuario)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_EXISTE",
                Mensaje = "El usuario que intenta editar no existe o no está activo.",
                IdUsuario = idUsuario
            };
        }

        var rol = request.Rol.Trim().ToUpperInvariant();

        var errores = ValidarCamposObligatoriosEdicion(request, rol);

        if (rol != "SUPER_USUARIO")
        {
            if (!request.IdEntidadFederativa.HasValue)
            {
                errores.Add(new UsuarioValidacionError
                {
                    Campo = "idEntidadFederativa",
                    Codigo = "USUARIO_ENTIDAD_OBLIGATORIA",
                    Mensaje = "El usuario debe tener entidad federativa para el rol seleccionado."
                });
            }
            else
            {
                var existeEntidad = await _usuarioRepository.ExisteEntidadActivaAsync(request.IdEntidadFederativa.Value);

                if (!existeEntidad)
                {
                    errores.Add(new UsuarioValidacionError
                    {
                        Campo = "idEntidadFederativa",
                        Codigo = "USUARIO_ENTIDAD_INVALIDA",
                        Mensaje = "La entidad federativa indicada no existe o no está activa."
                    });
                }
            }
        }
        else
        {
            request.IdEntidadFederativa = null;
        }

        if (rol == "CONSULTA")
        {
            request.HabilitaCarga = false;
            request.HabilitaModificacion = false;
        }

        var idRol = await _usuarioRepository.ObtenerIdRolActivoAsync(rol);

        if (!idRol.HasValue)
        {
            errores.Add(new UsuarioValidacionError
            {
                Campo = "rol",
                Codigo = "USUARIO_ROL_INVALIDO",
                Mensaje = $"El rol {rol} no existe o no está activo."
            });
        }

        var duplicados = await _usuarioRepository.ObtenerDuplicadosUsuarioEdicionAsync(
            idUsuario,
            request.Usuario,
            request.CorreoElectronico,
            request.Rfc,
            request.Curp);

        errores.AddRange(duplicados);

        string? passwordHash = null;

        if (!string.IsNullOrWhiteSpace(request.NuevaPassword))
        {
            if (request.NuevaPassword.Length < 8)
            {
                errores.Add(new UsuarioValidacionError
                {
                    Campo = "nuevaPassword",
                    Codigo = "USUARIO_PASSWORD_CORTO",
                    Mensaje = "La nueva contraseña debe tener al menos 8 caracteres."
                });
            }
            else
            {
                passwordHash = BCrypt.Net.BCrypt.HashPassword(request.NuevaPassword, workFactor: 12);
            }
        }

        if (errores.Count > 0)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_DATOS_INVALIDOS",
                Mensaje = "Existen errores en los datos del usuario.",
                IdUsuario = idUsuario,
                Errores = errores
            };
        }

        await _usuarioRepository.EditarUsuarioAsync(
            idUsuario,
            request,
            idRol!.Value,
            passwordHash,
            idUsuarioModificacion);

        _logger.LogInformation(
            "Usuario editado correctamente. IdUsuario: {IdUsuario}, UsuarioModificacion: {IdUsuarioModificacion}",
            idUsuario,
            idUsuarioModificacion);

        return new UsuarioOperacionResponse
        {
            EsValido = true,
            Codigo = "USUARIO_EDITADO",
            Mensaje = "Usuario editado correctamente.",
            IdUsuario = idUsuario
        };
    }

    public async Task<UsuarioOperacionResponse> DesactivarUsuarioAsync(int idUsuario, int idUsuarioModificacion)
    {
        var usuarioModificacion = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioModificacion);

        if (usuarioModificacion == null)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_MODIFICACION_NO_VALIDO",
                Mensaje = "El usuario autenticado no existe o no está activo.",
                IdUsuario = idUsuario
            };
        }

        if (!usuarioModificacion.EsSuperUsuario)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_ELIMINACION_SIN_PERMISO",
                Mensaje = "Solo un SUPER_USUARIO puede eliminar usuarios.",
                IdUsuario = idUsuario
            };
        }

        if (idUsuario == idUsuarioModificacion)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_PUEDE_ELIMINARSE_A_SI_MISMO",
                Mensaje = "No puede eliminar su propio usuario.",
                IdUsuario = idUsuario
            };
        }

        var existeUsuario = await _usuarioRepository.ExisteUsuarioActivoAsync(idUsuario);

        if (!existeUsuario)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_EXISTE",
                Mensaje = "El usuario que intenta eliminar no existe o ya está inactivo.",
                IdUsuario = idUsuario
            };
        }

        await _usuarioRepository.DesactivarUsuarioAsync(
            idUsuario,
            idUsuarioModificacion);

        _logger.LogInformation(
            "Usuario desactivado correctamente. IdUsuario: {IdUsuario}, UsuarioModificacion: {IdUsuarioModificacion}",
            idUsuario,
            idUsuarioModificacion);

        return new UsuarioOperacionResponse
        {
            EsValido = true,
            Codigo = "USUARIO_DESACTIVADO",
            Mensaje = "Usuario desactivado correctamente.",
            IdUsuario = idUsuario
        };
    }

    public async Task<UsuarioOperacionResponse> ActualizarPermisosGlobalesAsync(PermisosGlobalesUsuariosRequest request, int idUsuarioModificacion)
    {
        var usuarioModificacion = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioModificacion);

        if (usuarioModificacion == null)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_MODIFICACION_NO_VALIDO",
                Mensaje = "El usuario autenticado no existe o no está activo."
            };
        }

        if (!usuarioModificacion.EsSuperUsuario)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_PERMISOS_GLOBALES_SIN_PERMISO",
                Mensaje = "Solo un SUPER_USUARIO puede actualizar permisos globales."
            };
        }

        var totalActualizados = await _usuarioRepository.ActualizarPermisosGlobalesAsync(
            request.HabilitaCarga,
            request.HabilitaModificacion);

        _logger.LogInformation(
            "Permisos globales actualizados. HabilitaCarga: {HabilitaCarga}, HabilitaModificacion: {HabilitaModificacion}, Total: {Total}, UsuarioModificacion: {IdUsuarioModificacion}",
            request.HabilitaCarga,
            request.HabilitaModificacion,
            totalActualizados,
            idUsuarioModificacion);

        return new UsuarioOperacionResponse
        {
            EsValido = true,
            Codigo = "USUARIOS_PERMISOS_GLOBALES_ACTUALIZADOS",
            Mensaje = $"Permisos globales actualizados correctamente. Usuarios afectados: {totalActualizados}."
        };
    }

    private static List<UsuarioValidacionError> ValidarCamposObligatoriosEdicion(EditarUsuarioRequest request, string rol)
    {
        var errores = new List<UsuarioValidacionError>();

        if (string.IsNullOrWhiteSpace(request.Usuario))
        {
            errores.Add(ErrorUsuario("usuario", "USUARIO_CAMPO_OBLIGATORIO", "Debe enviar el usuario."));
        }

        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            errores.Add(ErrorUsuario("nombre", "USUARIO_NOMBRE_OBLIGATORIO", "Debe enviar el nombre."));
        }

        if (string.IsNullOrWhiteSpace(request.PrimerApellido))
        {
            errores.Add(ErrorUsuario("primerApellido", "USUARIO_PRIMER_APELLIDO_OBLIGATORIO", "Debe enviar el primer apellido."));
        }

        if (string.IsNullOrWhiteSpace(request.CorreoElectronico))
        {
            errores.Add(ErrorUsuario("correoElectronico", "USUARIO_CORREO_OBLIGATORIO", "Debe enviar el correo electrónico."));
        }

        if (string.IsNullOrWhiteSpace(request.Rfc))
        {
            errores.Add(ErrorUsuario("rfc", "USUARIO_RFC_OBLIGATORIO", "Debe enviar el RFC."));
        }

        if (string.IsNullOrWhiteSpace(request.Curp))
        {
            errores.Add(ErrorUsuario("curp", "USUARIO_CURP_OBLIGATORIO", "Debe enviar la CURP."));
        }

        if (string.IsNullOrWhiteSpace(rol))
        {
            errores.Add(ErrorUsuario("rol", "USUARIO_ROL_OBLIGATORIO", "Debe enviar el rol."));
        }
        else if (rol != "SUPER_USUARIO" && rol != "ENLACE_ESTATAL" && rol != "CONSULTA")
        {
            errores.Add(ErrorUsuario("rol", "USUARIO_ROL_NO_PERMITIDO", "El rol permitido debe ser SUPER_USUARIO, ENLACE_ESTATAL o CONSULTA."));
        }

        return errores;
    }

    private static UsuarioValidacionError ErrorUsuario(string campo, string codigo, string mensaje)
    {
        return new UsuarioValidacionError
        {
            Campo = campo,
            Codigo = codigo,
            Mensaje = mensaje
        };
    }

    public async Task<UsuarioOperacionResponse> ReactivarUsuarioAsync(int idUsuario, ReactivarUsuarioRequest request, int idUsuarioModificacion)
    {
        var usuarioModificacion = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuarioModificacion);

        if (usuarioModificacion == null)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_MODIFICACION_NO_VALIDO",
                Mensaje = "El usuario autenticado no existe o no está activo.",
                IdUsuario = idUsuario
            };
        }

        if (!usuarioModificacion.EsSuperUsuario)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_REACTIVACION_SIN_PERMISO",
                Mensaje = "Solo un SUPER_USUARIO puede reactivar usuarios.",
                IdUsuario = idUsuario
            };
        }

        if (idUsuario == idUsuarioModificacion)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_PUEDE_REACTIVARSE_A_SI_MISMO",
                Mensaje = "No es necesario reactivar su propio usuario autenticado.",
                IdUsuario = idUsuario
            };
        }

        var existeUsuario = await _usuarioRepository.ExisteUsuarioAsync(idUsuario);

        if (!existeUsuario)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_EXISTE",
                Mensaje = "El usuario que intenta reactivar no existe.",
                IdUsuario = idUsuario
            };
        }

        await _usuarioRepository.ReactivarUsuarioAsync(
            idUsuario,
            request,
            idUsuarioModificacion);

        _logger.LogInformation(
            "Usuario reactivado correctamente. IdUsuario: {IdUsuario}, HabilitaCarga: {HabilitaCarga}, HabilitaModificacion: {HabilitaModificacion}, UsuarioModificacion: {IdUsuarioModificacion}",
            idUsuario,
            request.HabilitaCarga,
            request.HabilitaModificacion,
            idUsuarioModificacion);

        return new UsuarioOperacionResponse
        {
            EsValido = true,
            Codigo = "USUARIO_REACTIVADO",
            Mensaje = "Usuario reactivado correctamente.",
            IdUsuario = idUsuario
        };
    }
}