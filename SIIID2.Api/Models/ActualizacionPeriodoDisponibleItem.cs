namespace SIIID2.Api.Models;

public class ActualizacionPeriodoDisponibleItem
{
    public int IdEntidadFederativa { get; set; }

    public int MesCorte { get; set; }

    public int AnioCorte { get; set; }

    public string Periodo { get; set; } = string.Empty;

    public bool ExisteActualizacionPendiente { get; set; }

    public string? CodigoActualizacionPendiente { get; set; }
}