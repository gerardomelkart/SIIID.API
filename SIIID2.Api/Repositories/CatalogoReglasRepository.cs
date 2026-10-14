using System.Globalization;
using Dapper;
using SIIID2.Api.Data;

namespace SIIID2.Api.Repositories;
public interface ICatalogoReglasRepository
{
    Task CargarAsync();
    string Clave(string catalogo, string codigo);
    int ClaveNumerica(string catalogo, string codigo);
    bool Contiene(string catalogo, string codigo, string? clave);
}

/// <summary>Resuelve el significado de una regla con la opción activa de BD, sin depender de IDs ni etiquetas.</summary>
public sealed class CatalogoReglasRepository(IDbConnectionFactory factory) : ICatalogoReglasRepository
{
    private List<Opcion>? _opciones;
    public async Task CargarAsync()
    {
        if(_opciones is not null)
            return;
        using var connection = factory.CrearConexion();
        _opciones = (await connection.QueryAsync<Opcion>("SELECT Catalogo, Clave, CodigoRegla FROM dbo.vw_catalogos_reglas WHERE CodigoRegla IS NOT NULL;")).AsList();
    }

    public string Clave(string catalogo, string codigo)
    {
        if(_opciones is null)
            throw new InvalidOperationException("Cargue los significados de catálogo antes de validar.");
        var opciones = _opciones.Where(o => o.Catalogo == catalogo && o.CodigoRegla == codigo).ToList();
        if(opciones.Count != 1)
            throw new InvalidOperationException($"El catálogo {catalogo} debe tener una única opción activa para {codigo}.");
        return opciones[0].Clave;
    }

    public bool Contiene(string catalogo, string codigo, string? clave)
    {
        if(_opciones is null)
            throw new InvalidOperationException("Cargue los significados de catálogo antes de validar.");
        var opciones = _opciones.Where(o => o.Catalogo == catalogo && o.CodigoRegla == codigo).ToList();
        if(opciones.Count == 0)
            throw new InvalidOperationException($"Falta el significado {codigo} activo en {catalogo}.");
        return opciones.Any(o => string.Equals(o.Clave, clave?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public int ClaveNumerica(string catalogo, string codigo) => int.Parse(Clave(catalogo, codigo), CultureInfo.InvariantCulture);
    private sealed class Opcion
    {
        public string Catalogo { get; set; } = "";
        public string Clave { get; set; } = "";
        public string CodigoRegla { get; set; } = "";
    }
}
