namespace SIIID2.Api.Models;

public class InformeSabanaFirma
{
    public long UltimoIdCarga { get; set; }
    public long TotalCargasConfirmadas { get; set; }
    public long TotalCargasPendientes { get; set; }
    public int? MesUltimoCorte { get; set; }
    public DateTime? UltimaFechaMovimiento { get; set; }
}