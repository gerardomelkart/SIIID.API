using System.Text.Json;

namespace SIIID2.Api.Services;

public class UltimosArchivosEntidadService : IUltimosArchivosEntidadService
{
    private readonly IWebHostEnvironment _environment;

    public UltimosArchivosEntidadService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task GuardarAsync(int idEntidadFederativa, string codigoReferencia, string tipoMovimiento, int mesCorte, int anioCorte, IFormFile archivoCarpetas, IFormFile archivoDelitos, IFormFile archivoVictimas)
    {
        var rutaBase = Path.Combine(_environment.ContentRootPath, "UltimosArchivosEntidad", $"entidad-{idEntidadFederativa:00}");

        Directory.CreateDirectory(rutaBase);

        LimpiarArchivosAnteriores(rutaBase);

        await GuardarArchivoOriginalAsync(archivoCarpetas, rutaBase);
        await GuardarArchivoOriginalAsync(archivoDelitos, rutaBase);
        await GuardarArchivoOriginalAsync(archivoVictimas, rutaBase);

        var metadata = new
        {
            idEntidadFederativa,
            codigoReferencia,
            tipoMovimiento,
            mesCorte,
            anioCorte,
            fechaGuardado = DateTimeOffset.Now,
            archivoCarpetasOriginal = archivoCarpetas.FileName,
            archivoDelitosOriginal = archivoDelitos.FileName,
            archivoVictimasOriginal = archivoVictimas.FileName
        };

        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(Path.Combine(rutaBase, "metadata.json"), metadataJson);
    }

    private static void LimpiarArchivosAnteriores(string rutaBase)
    {
        foreach (var archivoAnterior in Directory.GetFiles(rutaBase))
        {
            File.Delete(archivoAnterior);
        }
    }

    private static async Task GuardarArchivoOriginalAsync(IFormFile archivo, string rutaBase)
    {
        var nombreArchivo = Path.GetFileName(archivo.FileName);
        var rutaDestino = Path.Combine(rutaBase, nombreArchivo);

        await using var origen = archivo.OpenReadStream();

        if (origen.CanSeek)
        {
            origen.Position = 0;
        }

        await using var destino = new FileStream(rutaDestino, FileMode.Create, FileAccess.Write, FileShare.None);

        await origen.CopyToAsync(destino);
    }
}