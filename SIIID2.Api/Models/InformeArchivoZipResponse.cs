namespace SIIID2.Api.Models;

public class InformeArchivoZipResponse
{
    public byte[] Archivo { get; set; } = Array.Empty<byte>();

    public string NombreArchivo { get; set; } = string.Empty;
}