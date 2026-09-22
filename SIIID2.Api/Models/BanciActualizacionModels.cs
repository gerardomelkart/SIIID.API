using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace SIIID2.Api.Models;

public class BanciActualizacionRequest
{
    [Range(1, 32)] public int? IdEntidadFederativa { get; set; }
    [Required] public Dictionary<string, string?> Datos { get; set; } = [];
}

public class BanciActualizacionArchivoRequest
{
    [Range(1, 32)] public int? IdEntidadFederativa { get; set; }
    [Required] public IFormFile Archivo { get; set; } = null!;
}

public class BanciActualizacionConfirmacion
{
    [Required] public bool? Aceptar { get; set; }
    [StringLength(64)] public string? HuellaVistaPrevia { get; set; }
    public bool AceptarAdvertencias { get; set; }
}

public class BanciActualizacionResultado
{
    public bool EsValido => Errores.Count == 0;
    public Guid? CodigoReferencia { get; set; }
    public string Estado { get; set; } = "NO_VALIDADA";
    public string? Huella { get; set; }
    public JsonElement Cambios { get; set; } = JsonSerializer.SerializeToElement(Array.Empty<object>());
    public JsonElement DatosPropuestos { get; set; } = JsonSerializer.SerializeToElement(Array.Empty<object>());
    public List<BanciCargaValidacionError> Errores { get; set; } = [];
    public List<BanciCargaValidacionError> Advertencias { get; set; } = [];
}

internal class BanciActualizacionSql
{
    public Guid CodigoReferencia { get; set; }
    public string Estado { get; set; } = "";
    public string Huella { get; set; } = "";
    public string CambiosJson { get; set; } = "[]";
    public string DatosPropuestosJson { get; set; } = "[]";
    public string AdvertenciasJson { get; set; } = "[]";
    public BanciActualizacionResultado Resultado() => new() { CodigoReferencia = CodigoReferencia, Estado = Estado, Huella = Huella, Cambios = JsonSerializer.Deserialize<JsonElement>(CambiosJson), DatosPropuestos = JsonSerializer.Deserialize<JsonElement>(DatosPropuestosJson), Advertencias = JsonSerializer.Deserialize<List<BanciCargaValidacionError>>(AdvertenciasJson) ?? [] };
}

public class BanciVictimaIdentificada
{
    public int Indice { get; set; }
    public string NoBanci { get; set; } = "";
    public string IdDelito { get; set; } = "";
    public string IdVicf { get; set; } = "";
    public string? Curp { get; set; }
    public string? FolioRnpdno { get; set; }
}
