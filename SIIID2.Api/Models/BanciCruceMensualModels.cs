namespace SIIID2.Api.Models;

public sealed class BanciCrucePreparado
{
    public Guid Referencia { get; set; } = Guid.NewGuid();
    public byte[]? Archivo
    {
        get; set;
    }
    public string NombreArchivo { get; set; } = "";
    public BanciLibro? Origen
    {
        get; set;
    }
    public BanciLibro? Propuesta
    {
        get; set;
    }
    public BanciLibro? Nuevas
    {
        get; set;
    }
    public List<CargaValidacionError> Errores { get; set; } = [];
    public List<CargaValidacionError> Advertencias { get; set; } = [];
    public bool RequiereComplementos
    {
        get; set;
    }
}
public sealed class BanciComplementosSolicitud
{
    public string PlantillaBase64 { get; set; } = "";
    public int Victimas
    {
        get; set;
    }
}

public sealed class BanciCruceResumen
{
    public Guid Referencia
    {
        get; set;
    }
    public int Carpetas
    {
        get; set;
    }
    public int Victimas
    {
        get; set;
    }
    public int Nuevas
    {
        get; set;
    }
    public int Reclasificadas
    {
        get; set;
    }
    public List<BanciCambioVista> Cambios { get; set; } = [];
    public int TotalCambios
    {
        get; set;
    }
}
