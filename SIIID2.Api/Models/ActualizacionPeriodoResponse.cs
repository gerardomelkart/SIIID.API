namespace SIIID2.Api.Models;

public class ActualizacionPeriodoResponse
{
    public bool EsValido { get; set; }

    public bool PuedeActualizar { get; set; }

    public bool TieneCargaConfirmada { get; set; }

    public bool ExisteActualizacionPendiente { get; set; }

    public string? CodigoActualizacionPendiente { get; set; }

    public string? EstadoActualizacionPendiente { get; set; }

    public int? IdEntidadFederativa { get; set; }

    public int MesCorte { get; set; }

    public int AnioCorte { get; set; }

    public string Mensaje { get; set; } = string.Empty;
}