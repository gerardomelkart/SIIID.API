namespace SIIID2.Api.Models;

public class InformeArchivoCargaInfo
{
    public long IdCarga { get; set; }

    public string CodigoReferencia { get; set; } = string.Empty;

    public string TipoCarga { get; set; } = string.Empty;

    public string Estado { get; set; } = string.Empty;

    public int IdEntidadFederativa { get; set; }

    public int MesCorte { get; set; }

    public int AnioCorte { get; set; }

    public string EntidadFederativa { get; set; } = string.Empty;
}