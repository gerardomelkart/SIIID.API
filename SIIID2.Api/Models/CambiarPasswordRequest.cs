namespace SIIID2.Api.Models;

public class CambiarPasswordRequest
{
    public string NuevaPassword { get; set; } = string.Empty;
    public string ConfirmarPassword { get; set; } = string.Empty;
}