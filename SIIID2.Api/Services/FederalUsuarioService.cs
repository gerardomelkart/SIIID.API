using System.Text.RegularExpressions;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Services;

public class FederalUsuarioService : IFederalUsuarioService
{
    private const string RegexCorreoElectronico = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
    private const string RegexRfc = @"^[A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3}$";
    private const string RegexCurp = @"^[A-Z][AEIOUX][A-Z]{2}\d{2}(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])[HM](AS|BC|BS|CC|CL|CM|CS|CH|DF|DG|GT|GR|HG|JC|MC|MN|MS|NT|NL|OC|PL|QT|QR|SP|SL|SR|TC|TS|TL|VZ|YN|ZS|NE)[B-DF-HJ-NP-TV-Z]{3}[A-Z0-9]\d$";
    private readonly IFederalUsuarioRepository _repository;
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IFederalCargaRepository _cargaRepository;
    private readonly ILogger<FederalUsuarioService> _logger;

    public FederalUsuarioService(IFederalUsuarioRepository repository, IUsuarioRepository usuarioRepository, IFederalCargaRepository cargaRepository, ILogger<FederalUsuarioService> logger)
    {
        _repository = repository;
        _usuarioRepository = usuarioRepository;
        _cargaRepository = cargaRepository;
        _logger = logger;
    }

    private async Task ValidarAdministradorAsync(int idAdministrador)
    {
        var usuario = await _cargaRepository.ObtenerUsuarioCargaAsync(idAdministrador);
        if (usuario == null || !usuario.EsSuperUsuario) throw new UnauthorizedAccessException("Se requiere un superusuario con acceso activo al módulo Federal.");
    }

    public async Task<List<FederalUsuarioDetalle>> ObtenerUsuariosAsync(int idAdministrador, bool incluirInactivos)
    {
        await ValidarAdministradorAsync(idAdministrador);
        return await _repository.ObtenerUsuariosAsync(incluirInactivos);
    }

    public async Task<FederalUsuarioDetalleResponse> ObtenerDetalleAsync(int idAdministrador, int idUsuario)
    {
        await ValidarAdministradorAsync(idAdministrador);
        var usuario = await _repository.ObtenerDetalleAsync(idUsuario);
        if (usuario == null) throw new KeyNotFoundException("El usuario no existe.");
        return new FederalUsuarioDetalleResponse { EsValido = true, Codigo = "FEDERAL_USUARIO_ENCONTRADO", Mensaje = "Usuario encontrado.", Usuario = usuario };
    }

    public async Task<UsuarioOperacionResponse> CrearAsync(int idAdministrador, CrearUsuarioFederalRequest request)
    {
        await ValidarAdministradorAsync(idAdministrador);
        return await GuardarDatosAsync(idAdministrador, 0, request, request.Password, true);
    }

    public async Task<UsuarioOperacionResponse> EditarAsync(int idAdministrador, int idUsuario, EditarUsuarioFederalRequest request)
    {
        await ValidarAdministradorAsync(idAdministrador);
        var usuario = await _repository.ObtenerDetalleAsync(idUsuario);
        if (usuario == null) throw new KeyNotFoundException("El usuario no existe.");
        if (!usuario.Activo) throw new InvalidOperationException("El usuario no está activo en Federal.");
        return await GuardarDatosAsync(idAdministrador, idUsuario, request, request.NuevaPassword, false);
    }

    private async Task<UsuarioOperacionResponse> GuardarDatosAsync(int idAdministrador, int idUsuario, FederalUsuarioDatos request, string? password, bool crear)
    {
        Normalizar(request);
        var errores = ValidarCampos(request, request.Rol);
        if (crear && !request.HabilitaFederal) errores.Add(ErrorUsuario("habilitaFederal", "USUARIO_MODULO_OBLIGATORIO", "Debe habilitar el módulo Federal para registrar el usuario."));
        if (crear && string.IsNullOrWhiteSpace(password)) errores.Add(ErrorUsuario("password", "USUARIO_PASSWORD_OBLIGATORIO", "Debe enviar la contraseña."));
        else if (!string.IsNullOrWhiteSpace(password) && password.Length < 8)
            errores.Add(ErrorUsuario(crear ? "password" : "nuevaPassword", "USUARIO_PASSWORD_CORTO", "La contraseña debe tener al menos 8 caracteres."));

        int? idRol = string.IsNullOrWhiteSpace(request.Rol) ? null : await _usuarioRepository.ObtenerIdRolActivoAsync(request.Rol);
        if (!idRol.HasValue && !string.IsNullOrWhiteSpace(request.Rol)) errores.Add(ErrorUsuario("rol", "USUARIO_ROL_INVALIDO", "El rol no existe o no está activo."));
        errores.AddRange(await _usuarioRepository.ObtenerDuplicadosUsuarioEdicionAsync(idUsuario, request.Usuario, request.CorreoElectronico, request.Rfc, request.Curp));
        if (errores.Count > 0) return new UsuarioOperacionResponse { EsValido = false, Codigo = "FEDERAL_USUARIO_DATOS_INVALIDOS", Mensaje = "Existen errores en los datos del usuario.", IdUsuario = crear ? null : idUsuario, Errores = errores };

        var hash = string.IsNullOrWhiteSpace(password) ? null : BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        var id = await _repository.GuardarAsync(crear ? "CREAR" : "EDITAR", idUsuario, request, idRol, hash, idAdministrador);
        return Exito(crear ? "REGISTRADO" : "EDITADO", id, idAdministrador);
    }

    public async Task<UsuarioOperacionResponse> DesactivarAsync(int idAdministrador, int idUsuario)
    {
        await ValidarAdministradorAsync(idAdministrador);
        await _repository.GuardarAsync("DESACTIVAR", idUsuario, new FederalUsuarioDatos { HabilitaFederal = false }, null, null, idAdministrador);
        return Exito("DESACTIVADO", idUsuario, idAdministrador);
    }

    public async Task<UsuarioOperacionResponse> ReactivarAsync(int idAdministrador, int idUsuario, ReactivarUsuarioFederalRequest request)
    {
        await ValidarAdministradorAsync(idAdministrador);
        if (!request.HabilitaFederal) throw new InvalidOperationException("Debe habilitar el acceso Federal para reactivar al usuario.");
        var datos = new FederalUsuarioDatos { HabilitaFederal = request.HabilitaFederal, HabilitaCarga = request.HabilitaCarga, HabilitaModificacion = request.HabilitaModificacion };
        await _repository.GuardarAsync("REACTIVAR", idUsuario, datos, null, null, idAdministrador);
        return Exito("REACTIVADO", idUsuario, idAdministrador);
    }

    private UsuarioOperacionResponse Exito(string operacion, int idUsuario, int idAdministrador)
    {
        _logger.LogInformation("Usuario Federal {Operacion}. IdUsuario={IdUsuario}, IdAdministrador={IdAdministrador}", operacion, idUsuario, idAdministrador);
        var mensaje = operacion switch
        {
            "REGISTRADO" => "Usuario registrado correctamente en Federal.",
            "EDITADO" => "Usuario editado correctamente.",
            "DESACTIVADO" => "Usuario desactivado en Federal.",
            _ => "Usuario reactivado en Federal."
        };
        return new UsuarioOperacionResponse { EsValido = true, Codigo = $"FEDERAL_USUARIO_{operacion}", Mensaje = mensaje, IdUsuario = idUsuario };
    }

    private static void Normalizar(FederalUsuarioDatos request)
    {
        request.Usuario = request.Usuario?.Trim() ?? string.Empty;
        request.Nombre = request.Nombre?.Trim() ?? string.Empty;
        request.PrimerApellido = request.PrimerApellido?.Trim() ?? string.Empty;
        request.SegundoApellido = Opcional(request.SegundoApellido);
        request.CorreoElectronico = request.CorreoElectronico?.Trim() ?? string.Empty;
        request.Rfc = Opcional(request.Rfc)?.ToUpperInvariant();
        request.Curp = Opcional(request.Curp)?.ToUpperInvariant();
        request.TelefonoContacto = Opcional(request.TelefonoContacto);
        request.Rol = request.Rol?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!request.HabilitaFederal || request.Rol == "CONSULTA") { request.HabilitaCarga = false; request.HabilitaModificacion = false; }
    }

    private static string? Opcional(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static List<UsuarioValidacionError> ValidarCampos(FederalUsuarioDatos request, string rol)
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

}
