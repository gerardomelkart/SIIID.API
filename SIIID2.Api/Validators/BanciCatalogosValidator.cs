using System.Globalization;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Validators;

public class BanciCatalogosValidator
{
    private readonly ICatalogoRepository _catalogoRepository;

    public BanciCatalogosValidator(ICatalogoRepository catalogoRepository)
    {
        _catalogoRepository = catalogoRepository;
    }

    public async Task<List<BanciCargaValidacionError>> ValidarAsync(BanciLecturaArchivosResultado lectura)
    {
        var errores = new List<BanciCargaValidacionError>();

        var formasAccion = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_forma_accion", "clave");
        var instrumentos = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_instrumento_comision", "clave");
        var grados = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_grado_consumacion", "clave");
        var sexos = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_sexo", "clave");
        var generos = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_genero", "clave");
        var poblacion = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_pertenece_poblacion_indigena", "clave");
        var discapacidad = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_presenta_discapacidad", "clave");
        var nacionalidades = await _catalogoRepository.ObtenerClavesTextoActivasAsync("catalogo_nacionalidad", "clave");

        var clasificaciones = await _catalogoRepository.ObtenerClavesTextoActivasAsync("banci_catalogo_clasificacion_delito", "clave");
        var localizacion = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("banci_catalogo_localizacion", "clave");
        var condicionVida = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("banci_catalogo_condicion_vida", "clave");
        var voluntaria = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("banci_catalogo_voluntaria", "clave");
        var fueDelito = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("banci_catalogo_fue_delito", "clave");

        var entidades = await _catalogoRepository.ObtenerClavesNumericasActivasAsync("catalogo_entidad_federativa", "id_entidad_federativa");
        var municipios = await _catalogoRepository.ObtenerMunicipiosPorEntidadActivosAsync();

        foreach (var fila in lectura.Delitos)
        {
            ValidarNumerico(fila, "delitos", "forma_acc", formasAccion, errores);
            ValidarNumerico(fila, "delitos", "emto_com_dto", instrumentos, errores);
            ValidarNumerico(fila, "delitos", "grdo_cons", grados, errores);
            ValidarTexto(fila, "delitos", "clasf_de_dto", clasificaciones, errores);
            ValidarEntidadMunicipio(fila, entidades, municipios, errores);
        }

        foreach (var fila in lectura.Victimas)
        {
            ValidarNumerico(fila, "victimas", "sexo", sexos, errores);
            ValidarNumerico(fila, "victimas", "genero", generos, errores);
            ValidarNumerico(fila, "victimas", "pob", poblacion, errores);
            ValidarNumerico(fila, "victimas", "disc", discapacidad, errores);
            ValidarTextoNumerico(fila, "victimas", "nacional", nacionalidades, errores);
            ValidarNumerico(fila, "victimas", "localizado_o_no_localizado", localizacion, errores);
            ValidarNumerico(fila, "victimas", "con_o_sin_vida", condicionVida, errores);
            ValidarNumerico(fila, "victimas", "voluntaria", voluntaria, errores);
            ValidarNumerico(fila, "victimas", "fue_delito", fueDelito, errores);
        }

        return errores;
    }

    private static void ValidarEntidadMunicipio(ArchivoFila fila, HashSet<int> entidades, HashSet<string> municipios, List<BanciCargaValidacionError> errores)
    {
        var valorEntidad = Valor(fila, "id_ent_hchos");
        var valorMunicipio = Valor(fila, "id_mun_hchos");

        if (EsSinInformacion(valorEntidad) || EsSinInformacion(valorMunicipio)) return;

        if (!int.TryParse(valorEntidad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entidad) || !entidades.Contains(entidad))
        {
            AgregarError(errores, "delitos", fila, "id_ent_hchos", "BANCI_ID_ENT_HCHOS_NO_EXISTE", "La entidad de los hechos no existe o no está activa en el catálogo.");
            return;
        }

        if (!int.TryParse(valorMunicipio, NumberStyles.Integer, CultureInfo.InvariantCulture, out var municipio))
        {
            AgregarError(errores, "delitos", fila, "id_mun_hchos", "BANCI_ID_MUN_HCHOS_INVALIDO", "El municipio de los hechos debe contener una clave numérica válida.");
            return;
        }

        var llave = $"{entidad}|{municipio:000}";

        if (!municipios.Contains(llave)) AgregarError(errores, "delitos", fila, "id_mun_hchos", "BANCI_ID_MUN_HCHOS_NO_EXISTE", "El municipio de los hechos no corresponde con la entidad informada.");
    }

    private static void ValidarNumerico(ArchivoFila fila, string archivo, string campo, HashSet<int> catalogo, List<BanciCargaValidacionError> errores)
    {
        var valor = Valor(fila, campo);
        if (EsSinInformacion(valor)) return;

        if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clave) || !catalogo.Contains(clave))
        {
            AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_NO_EXISTE_CATALOGO", $"El valor \"{valor}\" no existe o no está activo en el catálogo de {campo}.");
        }
    }

    private static void ValidarTexto(ArchivoFila fila, string archivo, string campo, HashSet<string> catalogo, List<BanciCargaValidacionError> errores)
    {
        var valor = Valor(fila, campo);
        if (EsSinInformacion(valor)) return;

        if (!catalogo.Contains(valor)) AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_NO_EXISTE_CATALOGO", $"El valor \"{valor}\" no existe o no está activo en el catálogo de {campo}.");
    }

    private static void ValidarTextoNumerico(ArchivoFila fila, string archivo, string campo, HashSet<string> catalogo, List<BanciCargaValidacionError> errores)
    {
        var valor = Valor(fila, campo);
        if (EsSinInformacion(valor)) return;

        var normalizado = NormalizarNumeroTexto(valor);
        var existe = catalogo.Any(x => NormalizarNumeroTexto(x) == normalizado);

        if (!existe) AgregarError(errores, archivo, fila, campo, $"BANCI_{campo.ToUpperInvariant()}_NO_EXISTE_CATALOGO", $"El valor \"{valor}\" no existe o no está activo en el catálogo de {campo}.");
    }

    private static string NormalizarNumeroTexto(string valor)
    {
        valor = valor.Trim();
        return long.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero) ? numero.ToString(CultureInfo.InvariantCulture) : valor.ToUpperInvariant();
    }

    private static bool EsSinInformacion(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return true;
        return valor.Trim().ToUpperInvariant() is "NO DISPONIBLE" or "N/D" or "ND";
    }

    private static string Valor(ArchivoFila fila, string campo) => fila.Columnas.TryGetValue(campo, out var valor) ? valor?.Trim() ?? string.Empty : string.Empty;

    private static void AgregarError(List<BanciCargaValidacionError> errores, string archivo, ArchivoFila fila, string campo, string codigo, string mensaje)
    {
        errores.Add(new BanciCargaValidacionError { Archivo = archivo, NumeroFila = fila.NumeroFila, Campo = campo, Valor = Valor(fila, campo), Codigo = codigo, Mensaje = mensaje });
    }
}   