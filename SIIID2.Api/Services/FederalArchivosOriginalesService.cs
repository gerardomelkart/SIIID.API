using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public class FederalArchivosOriginalesService : IFederalArchivosOriginalesService
{
    private readonly string _rutaRaiz;
    private static readonly SemaphoreSlim AccesoArchivos = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };

    public FederalArchivosOriginalesService(IWebHostEnvironment environment)
    {
        _rutaRaiz = Path.Combine(environment.ContentRootPath, "UltimosArchivosFederal");
    }

    public async Task GuardarAsync(int idUsuarioCarga, string codigoReferencia, string tipoMovimiento, int mesCorte, int anioCorte, IFormFile archivoCarpetas, IFormFile archivoDelitos, IFormFile archivoVictimas)
    {
        await AccesoArchivos.WaitAsync();
        var rutaActual = Path.Combine(_rutaRaiz, "fgr");
        var rutaNueva = Path.Combine(_rutaRaiz, $"temporal-{Guid.NewGuid():N}");
        var rutaAnterior = Path.Combine(_rutaRaiz, $"anterior-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(rutaNueva);
            var metadata = new FederalArchivosOriginalesResumen
            {
                IdUsuarioCarga = idUsuarioCarga,
                CodigoReferencia = codigoReferencia,
                TipoMovimiento = tipoMovimiento,
                MesCorte = mesCorte,
                AnioCorte = anioCorte,
                FechaGuardado = DateTimeOffset.Now,
                Archivos =
                [
                    await GuardarArchivoOriginalAsync(archivoCarpetas, rutaNueva, "carpetas"),
                    await GuardarArchivoOriginalAsync(archivoDelitos, rutaNueva, "delitos"),
                    await GuardarArchivoOriginalAsync(archivoVictimas, rutaNueva, "victimas")
                ]
            };
            await File.WriteAllTextAsync(Path.Combine(rutaNueva, "metadata.json"), JsonSerializer.Serialize(metadata, JsonOptions));

            // Publicar únicamente cuando los tres originales y su metadata estén completos.
            if (Directory.Exists(rutaActual)) Directory.Move(rutaActual, rutaAnterior);
            try
            {
                Directory.Move(rutaNueva, rutaActual);
            }
            catch
            {
                if (Directory.Exists(rutaAnterior)) Directory.Move(rutaAnterior, rutaActual);
                throw;
            }

            if (Directory.Exists(rutaAnterior)) Directory.Delete(rutaAnterior, recursive: true);
        }
        finally
        {
            try
            {
                if (Directory.Exists(rutaNueva)) Directory.Delete(rutaNueva, recursive: true);
            }
            finally
            {
                AccesoArchivos.Release();
            }
        }
    }

    public async Task<List<FederalArchivosOriginalesResumen>> ObtenerResumenAsync()
    {
        await AccesoArchivos.WaitAsync();
        try
        {
            var metadata = await LeerMetadataAsync(Path.Combine(_rutaRaiz, "fgr"));
            return metadata == null ? [] : [metadata];
        }
        finally
        {
            AccesoArchivos.Release();
        }
    }

    public async Task<InformeArchivoZipResponse> DescargarAsync()
    {
        await AccesoArchivos.WaitAsync();
        try
        {
            var rutaBase = Path.Combine(_rutaRaiz, "fgr");
            var metadata = await LeerMetadataAsync(rutaBase);
            if (metadata == null || metadata.Archivos.Count != 3) throw new InvalidOperationException("No existe un juego completo de archivos originales federales guardados.");

            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var archivo in metadata.Archivos)
                {
                    var rutaArchivo = ObtenerRutaArchivoSegura(rutaBase, archivo.RutaRelativa);
                    if (!File.Exists(rutaArchivo)) throw new InvalidOperationException($"No se encontró el archivo original: {archivo.NombreOriginal}.");
                    archive.CreateEntryFromFile(rutaArchivo, archivo.RutaRelativa.Replace('\\', '/'), CompressionLevel.Fastest);
                }
                archive.CreateEntryFromFile(Path.Combine(rutaBase, "metadata.json"), "metadata.json", CompressionLevel.Fastest);
            }

            return new InformeArchivoZipResponse { Archivo = zipStream.ToArray(), NombreArchivo = $"ARCHIVOS_ORIGINALES_FEDERAL_FGR_{metadata.AnioCorte}_{metadata.MesCorte:00}.zip" };
        }
        finally
        {
            AccesoArchivos.Release();
        }
    }

    private static async Task<UltimosArchivosEntidadArchivo> GuardarArchivoOriginalAsync(IFormFile archivo, string rutaBase, string tipo)
    {
        var nombreArchivo = Path.GetFileName(archivo.FileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(nombreArchivo) || nombreArchivo is "." or "..") throw new InvalidOperationException("El nombre del archivo original no es válido.");
        var rutaTipo = Path.Combine(rutaBase, tipo);
        Directory.CreateDirectory(rutaTipo);
        var rutaDestino = Path.Combine(rutaTipo, nombreArchivo);

        await using (var origen = archivo.OpenReadStream())
        {
            if (origen.CanSeek) origen.Position = 0;
            await using var destino = new FileStream(rutaDestino, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await origen.CopyToAsync(destino);
        }

        var info = new FileInfo(rutaDestino);
        if (info.Length != archivo.Length) throw new InvalidOperationException($"No fue posible guardar completo el archivo original: {nombreArchivo}.");
        await using var stream = File.OpenRead(rutaDestino);
        var hash = await SHA256.HashDataAsync(stream);
        return new UltimosArchivosEntidadArchivo { Tipo = tipo, NombreOriginal = nombreArchivo, RutaRelativa = $"{tipo}/{nombreArchivo}", TamanioBytes = info.Length, Sha256 = Convert.ToHexString(hash).ToLowerInvariant() };
    }

    private static async Task<FederalArchivosOriginalesResumen?> LeerMetadataAsync(string rutaBase)
    {
        var rutaMetadata = Path.Combine(rutaBase, "metadata.json");
        if (!File.Exists(rutaMetadata)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(rutaMetadata);
            return JsonSerializer.Deserialize<FederalArchivosOriginalesResumen>(json, JsonOptions) ?? throw new InvalidOperationException("La metadata de los archivos originales federales no es válida.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("La metadata de los archivos originales federales no es válida.", ex);
        }
    }

    private static string ObtenerRutaArchivoSegura(string rutaBase, string rutaRelativa)
    {
        if (string.IsNullOrWhiteSpace(rutaRelativa)) throw new InvalidOperationException("La ruta del archivo original no es válida.");
        var baseCompleta = Path.GetFullPath(rutaBase) + Path.DirectorySeparatorChar;
        var rutaCompleta = Path.GetFullPath(Path.Combine(rutaBase, rutaRelativa.Replace('\\', '/')));
        var comparacion = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!rutaCompleta.StartsWith(baseCompleta, comparacion)) throw new InvalidOperationException("La ruta del archivo original no es válida.");
        return rutaCompleta;
    }
}
