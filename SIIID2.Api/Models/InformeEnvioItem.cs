namespace SIIID2.Api.Models;

public class InformeEnvioItem
{
    public long IdCarga { get; set; }

    public string CodigoReferencia { get; set; } = string.Empty;

    public string TipoCarga { get; set; } = string.Empty;

    public string Estado { get; set; } = string.Empty;

    public string EstadoTexto { get; set; } = string.Empty;

    public bool EsConfirmado { get; set; }

    public string? CodigoReferenciaConfirmada { get; set; }

    public string? TipoCargaConfirmada { get; set; }

    public int IdEntidadFederativa { get; set; }

    public string EntidadFederativa { get; set; } = string.Empty;

    public string ClaveEntidad { get; set; } = string.Empty;

    public DateTime FechaEnvio { get; set; }

    public string FechaEnvioTexto { get; set; } = string.Empty;

    public int MesCorte { get; set; }

    public int AnioCorte { get; set; }

    public string Corte { get; set; } = string.Empty;

    public string UsuarioEnvio { get; set; } = string.Empty;

    public string EndpointAcuse { get; set; } = string.Empty;

    public string EndpointExcel { get; set; } = string.Empty;
}