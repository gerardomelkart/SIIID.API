namespace SIIID2.Api.Models;

public class ActualizacionAnioDisponibleItem
{
    public int AnioCorte { get; set; }

    public List<ActualizacionMesDisponibleItem> Meses { get; set; } = new();
}