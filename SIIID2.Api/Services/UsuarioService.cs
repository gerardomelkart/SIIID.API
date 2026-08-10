using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using System.Text.RegularExpressions;

namespace SIIID2.Api.Services;

public class UsuarioService : IUsuarioService
{
    // Expresión regular básica para validar formato de correo electrónico.
    // No valida existencia del dominio; solo estructura general.
    private const string RegexCorreoElectronico = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    // RFC persona física o moral con homoclave.
    // Persona física: 4 letras + fecha + homoclave = 13 caracteres.
    // Persona moral: 3 letras + fecha + homoclave = 12 caracteres.
    private const string RegexRfc = @"^[A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3}$";

    // CURP estándar de 18 caracteres.
    // Posteriormente esta validación se podrá complementar con un servicio externo.
    private const string RegexCurp = @"^[A-Z][AEIOUX][A-Z]{2}\d{2}(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])[HM](AS|BC|BS|CC|CL|CM|CS|CH|DF|DG|GT|GR|HG|JC|MC|MN|MS|NT|NL|OC|PL|QT|QR|SP|SL|SR|TC|TS|TL|VZ|YN|ZS|NE)[B-DF-HJ-NP-TV-Z]{3}[A-Z0-9]\d$";

    private readonly IUsuarioRepository _usuarioRepository;
    private readonly ILogger<UsuarioService> _logger;

    public UsuarioService(IUsuarioRepository usuarioRepository, ILogger<UsuarioService> logger)
    {
        _usuarioRepository = usuarioRepository;
        _logger = logger;
    }

    public async Task<List<UsuarioListadoItem>> ObtenerUsuariosAsync(bool incluirInactivos)
    {
        // Regresa usuarios para la tabla administrativa.
        // incluirInactivos permite mostrar también usuarios dados de baja lógicamente.
        return await _usuarioRepository.ObtenerUsuariosAsync(incluirInactivos);
    }

    public async Task<UsuarioDetalleResponse> ObtenerUsuarioDetalleAsync(int idUsuario)
    {
        // Obtiene la información completa de un usuario para llenar el formulario de edición.
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

    public async Task<CrearUsuarioResponse> CrearUsuarioAsync(CrearUsuarioRequest request, int idUsuarioAlta)
    {
        // Se valida que el usuario autenticado exista y esté activo.
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

        // Por ahora solo SUPER_USUARIO puede crear usuarios.
        if (!usuarioAlta.EsSuperUsuario)
        {
            return new CrearUsuarioResponse
            {
                EsValido = false,
                Codigo = "USUARIO_ALTA_SIN_PERMISO",
                Mensaje = "Solo un SUPER_USUARIO puede registrar usuarios."
            };
        }

        // Se normaliza el rol a mayúsculas para evitar errores por escritura.
        var rol = request.Rol.Trim().ToUpperInvariant();

        // Se validan campos obligatorios y formatos.
        // Esta validación acumula todos los errores encontrados.
        var errores = ValidarCamposCrearUsuario(request, rol);

        // Se valida que el rol exista y esté activo en la tabla roles.
        // Solo consultamos base si el rol trae algún valor para evitar mensajes redundantes.
        int? idRol = null;

        if (!string.IsNullOrWhiteSpace(rol))
        {
            idRol = await _usuarioRepository.ObtenerIdRolActivoAsync(rol);

            if (!idRol.HasValue)
            {
                errores.Add(ErrorUsuario(
                    "rol",
                    "USUARIO_ROL_INVALIDO",
                    $"El rol {rol} no existe o no está activo."));
            }
        }

        // Todos los roles excepto SUPER_USUARIO deben estar ligados a una entidad federativa.
        if (rol != "SUPER_USUARIO")
        {
            if (!request.IdEntidadFederativa.HasValue)
            {
                errores.Add(ErrorUsuario(
                    "idEntidadFederativa",
                    "USUARIO_ENTIDAD_OBLIGATORIA",
                    "El usuario debe tener entidad federativa para el rol seleccionado."));
            }
            else
            {
                // Si se envió entidad, se valida que exista y esté activa.
                var existeEntidad = await _usuarioRepository.ExisteEntidadActivaAsync(request.IdEntidadFederativa.Value);

                if (!existeEntidad)
                {
                    errores.Add(ErrorUsuario(
                        "idEntidadFederativa",
                        "USUARIO_ENTIDAD_INVALIDA",
                        "La entidad federativa indicada no existe o no está activa."));
                }
            }
        }
        else
        {
            // SUPER_USUARIO puede no tener entidad federativa.
            request.IdEntidadFederativa = null;
        }

        // El rol CONSULTA no debe poder cargar ni modificar información.
        // Aunque el front mande true, aquí se fuerza a false.
        if (rol == "CONSULTA")
        {
            request.HabilitaCarga = false;
            request.HabilitaModificacion = false;
            request.HabilitaCargaSemanal = false;
        }

        if (rol != "SUPER_USUARIO")
        {
            request.AdministraDelitosSemanal = false;
        }

        if (!request.HabilitaMensual)
        {
            request.HabilitaCarga = false;
            request.HabilitaModificacion = false;
        }

        if (!request.HabilitaSemanal)
        {
            request.HabilitaCargaSemanal = false;
            request.AdministraDelitosSemanal = false;
        }

        if (!request.HabilitaMensual && !request.HabilitaSemanal)
        {
            errores.Add(ErrorUsuario("modulos", "USUARIO_MODULO_OBLIGATORIO", "Debe habilitar al menos un módulo para el usuario."));
        }

        // Se validan duplicados de usuario, correo, RFC y CURP.
        // Este método regresa todos los duplicados encontrados, no solo el primero.
        var duplicados = await _usuarioRepository.ObtenerDuplicadosUsuarioAsync(
            request.Usuario,
            request.CorreoElectronico,
            request.Rfc,
            request.Curp);

        errores.AddRange(duplicados);

        // Si hubo cualquier error, se regresa una sola respuesta con todos los errores.
        if (errores.Count > 0)
        {
            return new CrearUsuarioResponse
            {
                EsValido = false,
                Codigo = "USUARIO_DATOS_INVALIDOS",
                Mensaje = "Existen errores en los datos del usuario.",
                Errores = errores
            };
        }

        // La contraseña nunca se guarda plana.
        // Se genera hash BCrypt antes de insertar.
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        // Se inserta usuario y permisos en base.
        // El repository lo hace dentro de una transacción.
        var idUsuario = await _usuarioRepository.CrearUsuarioAsync(
            request,
            idRol!.Value,
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

    public async Task<CrearUsuarioResponse> CrearUsuarioSemanalAsync(CrearUsuarioSemanalRequest request, int idUsuarioAlta)
    {
        var requestGeneral = new CrearUsuarioRequest
        {
            Usuario = request.Usuario,
            Password = request.Password,
            Nombre = request.Nombre,
            PrimerApellido = request.PrimerApellido,
            SegundoApellido = request.SegundoApellido,
            CorreoElectronico = request.CorreoElectronico,
            Rfc = request.Rfc,
            Curp = request.Curp,
            TelefonoContacto = request.TelefonoContacto,
            IdEntidadFederativa = request.IdEntidadFederativa,
            Rol = request.Rol,
            HabilitaMensual = false,
            HabilitaCarga = false,
            HabilitaModificacion = false,
            HabilitaSemanal = request.HabilitaSemanal,
            HabilitaCargaSemanal = request.HabilitaCargaSemanal,
            AdministraDelitosSemanal = request.AdministraDelitosSemanal
        };

        return await CrearUsuarioAsync(requestGeneral, idUsuarioAlta);
    }

    public async Task<UsuarioOperacionResponse> EditarUsuarioAsync(int idUsuario, EditarUsuarioRequest request, int idUsuarioModificacion)
    {
        // Se valida que el usuario que modifica exista y esté activo.
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

        // Por ahora solo SUPER_USUARIO puede editar usuarios.
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

        // La edición solo aplica sobre usuarios activos.
        var usuarioExistente = await _usuarioRepository.ObtenerUsuarioDetalleAsync(idUsuario);

        if (usuarioExistente == null || !usuarioExistente.Activo)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_EXISTE",
                Mensaje = "El usuario que intenta editar no existe o no está activo.",
                IdUsuario = idUsuario
            };
        }

        if (idUsuario == idUsuarioModificacion && !request.HabilitaMensual)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_PUEDE_DESHABILITAR_SU_ACCESO_MENSUAL",
                Mensaje = "No puede deshabilitar su propio acceso al módulo consolidado.",
                IdUsuario = idUsuario
            };
        }

        var rol = request.Rol.Trim().ToUpperInvariant();

        // Se validan campos obligatorios y formatos.
        var errores = ValidarCamposObligatoriosEdicion(request, rol);

        // Todos los roles excepto SUPER_USUARIO requieren entidad.
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
            // SUPER_USUARIO puede no tener entidad federativa.
            request.IdEntidadFederativa = null;
        }

        // El rol CONSULTA no puede cargar ni modificar.
        if (rol == "CONSULTA")
        {
            request.HabilitaCarga = false;
            request.HabilitaModificacion = false;
            request.HabilitaCargaSemanal = false;
        }

        if (rol != "SUPER_USUARIO")
        {
            request.AdministraDelitosSemanal = false;
        }

        if (!request.HabilitaMensual)
        {
            request.HabilitaCarga = false;
            request.HabilitaModificacion = false;
        }

        if (request.HabilitaSemanal == false)
        {
            request.HabilitaCargaSemanal = false;
            request.AdministraDelitosSemanal = false;
        }

        if (!request.HabilitaMensual && !(request.HabilitaSemanal ?? usuarioExistente.HabilitaSemanal))
        {
            errores.Add(ErrorUsuario("modulos", "USUARIO_MODULO_OBLIGATORIO", "Debe habilitar al menos un módulo para el usuario."));
        }

        // Se valida que el rol exista en base.
        // Solo consultamos base si el rol trae algún valor para evitar mensajes redundantes.
        int? idRol = null;

        if (!string.IsNullOrWhiteSpace(rol))
        {
            idRol = await _usuarioRepository.ObtenerIdRolActivoAsync(rol);

            if (!idRol.HasValue)
            {
                errores.Add(new UsuarioValidacionError
                {
                    Campo = "rol",
                    Codigo = "USUARIO_ROL_INVALIDO",
                    Mensaje = $"El rol {rol} no existe o no está activo."
                });
            }
        }

        // Se validan duplicados excluyendo al propio usuario editado.
        var duplicados = await _usuarioRepository.ObtenerDuplicadosUsuarioEdicionAsync(
            idUsuario,
            request.Usuario,
            request.CorreoElectronico,
            request.Rfc,
            request.Curp);

        errores.AddRange(duplicados);

        string? passwordHash = null;

        // Si nuevaPassword viene vacía o null, no se cambia la contraseña.
        // Si viene con valor, se valida y se hashea.
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

        // Si hay errores, se regresa todo junto.
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

        // Se actualiza usuario y permisos.
        // El repository lo ejecuta dentro de una transacción.
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

    public async Task<UsuarioOperacionResponse> EditarUsuarioSemanalAsync(int idUsuario, EditarUsuarioSemanalRequest request, int idUsuarioModificacion)
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
                Codigo = "USUARIO_MODIFICACION_SEMANAL_SIN_PERMISO",
                Mensaje = "Solo un SUPER_USUARIO puede editar usuarios desde el módulo semanal.",
                IdUsuario = idUsuario
            };
        }

        var usuarioExistente = await _usuarioRepository.ObtenerUsuarioDetalleAsync(idUsuario);

        if (usuarioExistente == null || !usuarioExistente.Activo)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_EXISTE",
                Mensaje = "El usuario que intenta editar no existe o no está activo.",
                IdUsuario = idUsuario
            };
        }

        if (idUsuario == idUsuarioModificacion && !request.HabilitaSemanal)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_PUEDE_DESHABILITAR_SU_ACCESO_SEMANAL",
                Mensaje = "No puede deshabilitar su propio acceso al módulo semanal.",
                IdUsuario = idUsuario
            };
        }

        if (!usuarioExistente.HabilitaMensual && !request.HabilitaSemanal)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_MODULO_OBLIGATORIO",
                Mensaje = "Debe habilitar al menos un módulo para el usuario.",
                IdUsuario = idUsuario
            };
        }

        var rol = request.Rol.Trim().ToUpperInvariant();

        var requestValidacion = new EditarUsuarioRequest
        {
            Usuario = request.Usuario,
            NuevaPassword = request.NuevaPassword,
            Nombre = request.Nombre,
            PrimerApellido = request.PrimerApellido,
            SegundoApellido = request.SegundoApellido,
            CorreoElectronico = request.CorreoElectronico,
            Rfc = request.Rfc,
            Curp = request.Curp,
            TelefonoContacto = request.TelefonoContacto,
            IdEntidadFederativa = request.IdEntidadFederativa,
            Rol = request.Rol,
            HabilitaMensual = usuarioExistente.HabilitaMensual,
            HabilitaCarga = false,
            HabilitaModificacion = false,
            HabilitaSemanal = request.HabilitaSemanal,
            HabilitaCargaSemanal = request.HabilitaCargaSemanal,
            AdministraDelitosSemanal = request.AdministraDelitosSemanal
        };

        var errores = ValidarCamposObligatoriosEdicion(requestValidacion, rol);

        if (rol != "SUPER_USUARIO")
        {
            if (!request.IdEntidadFederativa.HasValue)
            {
                errores.Add(ErrorUsuario("idEntidadFederativa", "USUARIO_ENTIDAD_OBLIGATORIA", "El usuario debe tener entidad federativa para el rol seleccionado."));
            }
            else
            {
                var existeEntidad = await _usuarioRepository.ExisteEntidadActivaAsync(request.IdEntidadFederativa.Value);

                if (!existeEntidad) errores.Add(ErrorUsuario("idEntidadFederativa", "USUARIO_ENTIDAD_INVALIDA", "La entidad federativa indicada no existe o no está activa."));
            }
        }
        else
        {
            request.IdEntidadFederativa = null;
        }

        if (rol == "CONSULTA")
        {
            request.HabilitaCargaSemanal = false;
        }

        if (rol != "SUPER_USUARIO") request.AdministraDelitosSemanal = false;

        if (!request.HabilitaSemanal)
        {
            request.HabilitaCargaSemanal = false;
            request.AdministraDelitosSemanal = false;
        }

        int? idRol = null;

        if (!string.IsNullOrWhiteSpace(rol))
        {
            idRol = await _usuarioRepository.ObtenerIdRolActivoAsync(rol);

            if (!idRol.HasValue) errores.Add(ErrorUsuario("rol", "USUARIO_ROL_INVALIDO", $"El rol {rol} no existe o no está activo."));
        }

        var duplicados = await _usuarioRepository.ObtenerDuplicadosUsuarioEdicionAsync(idUsuario, request.Usuario, request.CorreoElectronico, request.Rfc, request.Curp);
        errores.AddRange(duplicados);

        string? passwordHash = null;

        if (!string.IsNullOrWhiteSpace(request.NuevaPassword))
        {
            if (request.NuevaPassword.Length < 8)
            {
                errores.Add(ErrorUsuario("nuevaPassword", "USUARIO_PASSWORD_CORTO", "La nueva contraseña debe tener al menos 8 caracteres."));
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

        await _usuarioRepository.EditarUsuarioSemanalAsync(idUsuario, request, idRol!.Value, passwordHash, idUsuarioModificacion);

        _logger.LogInformation("Usuario editado desde el módulo semanal. IdUsuario: {IdUsuario}, UsuarioModificacion: {IdUsuarioModificacion}", idUsuario, idUsuarioModificacion);

        return new UsuarioOperacionResponse
        {
            EsValido = true,
            Codigo = "USUARIO_SEMANAL_EDITADO",
            Mensaje = "Usuario actualizado correctamente.",
            IdUsuario = idUsuario
        };
    }

    public async Task<UsuarioOperacionResponse> ActualizarPermisosSemanalesAsync(int idUsuario, ActualizarPermisosSemanalesRequest request, int idUsuarioModificacion)
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
                Codigo = "USUARIO_PERMISOS_SEMANALES_SIN_PERMISO",
                Mensaje = "Solo un SUPER_USUARIO puede actualizar permisos semanales.",
                IdUsuario = idUsuario
            };
        }

        var usuario = await _usuarioRepository.ObtenerUsuarioDetalleAsync(idUsuario);

        if (usuario == null || !usuario.Activo)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_EXISTE",
                Mensaje = "El usuario solicitado no existe o no está activo.",
                IdUsuario = idUsuario
            };
        }

        if (idUsuario == idUsuarioModificacion && !request.HabilitaSemanal)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_PUEDE_DESHABILITAR_SU_ACCESO_SEMANAL",
                Mensaje = "No puede deshabilitar su propio acceso al módulo semanal.",
                IdUsuario = idUsuario
            };
        }

        if (!usuario.HabilitaMensual && !request.HabilitaSemanal)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_MODULO_OBLIGATORIO",
                Mensaje = "Debe habilitar al menos un módulo para el usuario.",
                IdUsuario = idUsuario
            };
        }

        var rol = usuario.Rol.Trim().ToUpperInvariant();

        if (rol == "CONSULTA")
        {
            request.HabilitaCargaSemanal = false;
        }

        if (rol != "SUPER_USUARIO")
        {
            request.AdministraDelitosSemanal = false;
        }

        if (!request.HabilitaSemanal)
        {
            request.HabilitaCargaSemanal = false;
            request.AdministraDelitosSemanal = false;
        }

        await _usuarioRepository.ActualizarPermisosSemanalesAsync(idUsuario, request, idUsuarioModificacion);

        _logger.LogInformation("Permisos semanales actualizados. IdUsuario: {IdUsuario}, HabilitaSemanal: {HabilitaSemanal}, HabilitaCargaSemanal: {HabilitaCargaSemanal}, AdministraDelitosSemanal: {AdministraDelitosSemanal}, UsuarioModificacion: {IdUsuarioModificacion}", idUsuario, request.HabilitaSemanal, request.HabilitaCargaSemanal, request.AdministraDelitosSemanal, idUsuarioModificacion);

        return new UsuarioOperacionResponse
        {
            EsValido = true,
            Codigo = "USUARIO_PERMISOS_SEMANALES_ACTUALIZADOS",
            Mensaje = "Permisos semanales actualizados correctamente.",
            IdUsuario = idUsuario
        };
    }

    public async Task<UsuarioOperacionResponse> DesactivarUsuarioAsync(int idUsuario, int idUsuarioModificacion)
    {
        // Se valida que el usuario que solicita la baja exista y esté activo.
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

        // Por ahora solo SUPER_USUARIO puede dar de baja usuarios.
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

        // Evita que el usuario autenticado se elimine a sí mismo.
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

        // La baja lógica solo aplica si el usuario sigue activo.
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

        var usuario = await _usuarioRepository.ObtenerUsuarioDetalleAsync(idUsuario);

        if (usuario?.Rol.Trim().ToUpperInvariant() == "SUPER_USUARIO")
        {
            var totalSuperUsuariosActivos = await _usuarioRepository.ContarSuperUsuariosActivosAsync();

            if (totalSuperUsuariosActivos <= 1)
            {
                return new UsuarioOperacionResponse
                {
                    EsValido = false,
                    Codigo = "USUARIO_UNICO_SUPER_USUARIO_ACTIVO",
                    Mensaje = "No puede desactivar al único superusuario activo.",
                    IdUsuario = idUsuario
                };
            }
        }


        // No elimina físicamente.
        // Solo marca usuario y permisos como inactivos.
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

        var totalActualizados = await _usuarioRepository.ActualizarPermisosGlobalesAsync(request.HabilitaCarga, request.HabilitaModificacion);

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

    public async Task<UsuarioOperacionResponse> ReactivarUsuarioAsync(int idUsuario, ReactivarUsuarioRequest request, int idUsuarioModificacion)
    {
        // Se valida que el usuario que solicita la reactivación exista y esté activo.
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

        // Por ahora solo SUPER_USUARIO puede reactivar usuarios.
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

        // Evita una operación innecesaria sobre el propio usuario autenticado.
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

        // Aquí se valida existencia sin importar si está activo o inactivo.
        var usuario = await _usuarioRepository.ObtenerUsuarioDetalleAsync(idUsuario);

        if (usuario == null)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_EXISTE",
                Mensaje = "El usuario que intenta reactivar no existe.",
                IdUsuario = idUsuario
            };
        }

        if (!request.HabilitaMensual && !usuario.HabilitaSemanal)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_MODULO_OBLIGATORIO",
                Mensaje = "Debe habilitar al menos un módulo para el usuario.",
                IdUsuario = idUsuario
            };
        }

        if (!request.HabilitaMensual)
        {
            request.HabilitaCarga = false;
            request.HabilitaModificacion = false;
        }

        // Reactiva usuario y define permisos de carga/modificación.
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

    public async Task<UsuarioOperacionResponse> ReactivarUsuarioSemanalAsync(int idUsuario, ReactivarUsuarioSemanalRequest request, int idUsuarioModificacion)
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
                Codigo = "USUARIO_REACTIVACION_SEMANAL_SIN_PERMISO",
                Mensaje = "Solo un SUPER_USUARIO puede reactivar usuarios desde el módulo semanal.",
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

        var usuario = await _usuarioRepository.ObtenerUsuarioDetalleAsync(idUsuario);

        if (usuario == null)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_NO_EXISTE",
                Mensaje = "El usuario que intenta reactivar no existe.",
                IdUsuario = idUsuario
            };
        }

        if (usuario.Activo)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_YA_ACTIVO",
                Mensaje = "El usuario ya se encuentra activo.",
                IdUsuario = idUsuario
            };
        }

        if (!usuario.HabilitaMensual && !request.HabilitaSemanal)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_MODULO_OBLIGATORIO",
                Mensaje = "Debe habilitar al menos un módulo para el usuario.",
                IdUsuario = idUsuario
            };
        }

        var rol = usuario.Rol.Trim().ToUpperInvariant();

        if (rol == "CONSULTA")
        {
            request.HabilitaCargaSemanal = false;
        }

        if (rol != "SUPER_USUARIO") request.AdministraDelitosSemanal = false;

        if (!request.HabilitaSemanal)
        {
            request.HabilitaCargaSemanal = false;
            request.AdministraDelitosSemanal = false;
        }

        await _usuarioRepository.ReactivarUsuarioSemanalAsync(idUsuario, request, idUsuarioModificacion);

        _logger.LogInformation("Usuario reactivado desde el módulo semanal. IdUsuario: {IdUsuario}, UsuarioModificacion: {IdUsuarioModificacion}", idUsuario, idUsuarioModificacion);

        return new UsuarioOperacionResponse
        {
            EsValido = true,
            Codigo = "USUARIO_SEMANAL_REACTIVADO",
            Mensaje = "Usuario reactivado correctamente.",
            IdUsuario = idUsuario
        };
    }

    private static List<UsuarioValidacionError> ValidarCamposCrearUsuario(CrearUsuarioRequest request, string rol)
    {
        var errores = new List<UsuarioValidacionError>();

        // Usuario de acceso al sistema.
        if (string.IsNullOrWhiteSpace(request.Usuario))
        {
            errores.Add(ErrorUsuario("usuario", "USUARIO_CAMPO_OBLIGATORIO", "Debe enviar el usuario."));
        }

        // Contraseña inicial.
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errores.Add(ErrorUsuario("password", "USUARIO_PASSWORD_OBLIGATORIO", "Debe enviar la contraseña."));
        }
        else if (request.Password.Length < 8)
        {
            errores.Add(ErrorUsuario("password", "USUARIO_PASSWORD_CORTO", "La contraseña debe tener al menos 8 caracteres."));
        }

        // Datos personales mínimos.
        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            errores.Add(ErrorUsuario("nombre", "USUARIO_NOMBRE_OBLIGATORIO", "Debe enviar el nombre."));
        }

        if (string.IsNullOrWhiteSpace(request.PrimerApellido))
        {
            errores.Add(ErrorUsuario("primerApellido", "USUARIO_PRIMER_APELLIDO_OBLIGATORIO", "Debe enviar el primer apellido."));
        }

        // Correo electrónico.
        if (string.IsNullOrWhiteSpace(request.CorreoElectronico))
        {
            errores.Add(ErrorUsuario("correoElectronico", "USUARIO_CORREO_OBLIGATORIO", "Debe enviar el correo electrónico."));
        }
        else if (!Regex.IsMatch(request.CorreoElectronico.Trim(), RegexCorreoElectronico, RegexOptions.IgnoreCase))
        {
            errores.Add(ErrorUsuario("correoElectronico", "USUARIO_CORREO_FORMATO_INVALIDO", "El correo electrónico no tiene un formato válido."));
        }

        // RFC opcional; si se captura, se valida el formato.
        if (!string.IsNullOrWhiteSpace(request.Rfc) && !Regex.IsMatch(request.Rfc.Trim().ToUpperInvariant(), RegexRfc))
        {
            errores.Add(ErrorUsuario("rfc", "USUARIO_RFC_FORMATO_INVALIDO", "El RFC no tiene un formato válido."));
        }

        // CURP opcional; si se captura, se valida el formato.
        if (!string.IsNullOrWhiteSpace(request.Curp) && !Regex.IsMatch(request.Curp.Trim().ToUpperInvariant(), RegexCurp))
        {
            errores.Add(ErrorUsuario("curp", "USUARIO_CURP_FORMATO_INVALIDO", "La CURP no tiene un formato válido."));
        }

        // Rol.
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

    private static List<UsuarioValidacionError> ValidarCamposObligatoriosEdicion(EditarUsuarioRequest request, string rol)
    {
        var errores = new List<UsuarioValidacionError>();

        // Usuario de acceso.
        if (string.IsNullOrWhiteSpace(request.Usuario))
        {
            errores.Add(ErrorUsuario("usuario", "USUARIO_CAMPO_OBLIGATORIO", "Debe enviar el usuario."));
        }

        // Datos personales mínimos.
        if (string.IsNullOrWhiteSpace(request.Nombre))
        {
            errores.Add(ErrorUsuario("nombre", "USUARIO_NOMBRE_OBLIGATORIO", "Debe enviar el nombre."));
        }

        if (string.IsNullOrWhiteSpace(request.PrimerApellido))
        {
            errores.Add(ErrorUsuario("primerApellido", "USUARIO_PRIMER_APELLIDO_OBLIGATORIO", "Debe enviar el primer apellido."));
        }

        // Correo electrónico.
        if (string.IsNullOrWhiteSpace(request.CorreoElectronico))
        {
            errores.Add(ErrorUsuario("correoElectronico", "USUARIO_CORREO_OBLIGATORIO", "Debe enviar el correo electrónico."));
        }
        else if (!Regex.IsMatch(request.CorreoElectronico.Trim(), RegexCorreoElectronico, RegexOptions.IgnoreCase))
        {
            errores.Add(ErrorUsuario("correoElectronico", "USUARIO_CORREO_FORMATO_INVALIDO", "El correo electrónico no tiene un formato válido."));
        }

        // RFC opcional; si se captura, se valida el formato.
        if (!string.IsNullOrWhiteSpace(request.Rfc) && !Regex.IsMatch(request.Rfc.Trim().ToUpperInvariant(), RegexRfc))
        {
            errores.Add(ErrorUsuario("rfc", "USUARIO_RFC_FORMATO_INVALIDO", "El RFC no tiene un formato válido."));
        }

        // CURP opcional; si se captura, se valida el formato.
        if (!string.IsNullOrWhiteSpace(request.Curp) && !Regex.IsMatch(request.Curp.Trim().ToUpperInvariant(), RegexCurp))
        {
            errores.Add(ErrorUsuario("curp", "USUARIO_CURP_FORMATO_INVALIDO", "La CURP no tiene un formato válido."));
        }

        // Rol.
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

    public async Task<UsuarioOperacionResponse> ValidarSuperUsuarioAsync(int idUsuario)
    {
        var usuario = await _usuarioRepository.ObtenerUsuarioCargaAsync(idUsuario);

        if (usuario == null)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_AUTENTICADO_NO_VALIDO",
                Mensaje = "El usuario autenticado no existe o no está activo.",
                IdUsuario = idUsuario
            };
        }

        if (!usuario.EsSuperUsuario)
        {
            return new UsuarioOperacionResponse
            {
                EsValido = false,
                Codigo = "USUARIO_SIN_PERMISO_ADMINISTRACION",
                Mensaje = "Solo un SUPER_USUARIO puede consultar la administración de usuarios.",
                IdUsuario = idUsuario
            };
        }

        return new UsuarioOperacionResponse
        {
            EsValido = true,
            Codigo = "USUARIO_AUTORIZADO",
            Mensaje = "Usuario autorizado.",
            IdUsuario = idUsuario
        };
    }
}