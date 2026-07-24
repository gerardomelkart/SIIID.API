using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public class UltimosArchivosEntidadService : IUltimosArchivosEntidadService
{
    private readonly IWebHostEnvironment _environment;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public UltimosArchivosEntidadService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task GuardarAsync(int idEntidadFederativa, string codigoReferencia, string tipoMovimiento, int mesCorte, int anioCorte, IFormFile archivoCarpetas, IFormFile archivoDelitos, IFormFile archivoVictimas)
    {
        var rutaBase = ObtenerRutaEntidad(idEntidadFederativa);

        LimpiarCarpetaEntidad(rutaBase);

        var archivos = new List<UltimosArchivosEntidadArchivo>
        {
            await GuardarArchivoOriginalAsync(archivoCarpetas, rutaBase, "carpetas"),
            await GuardarArchivoOriginalAsync(archivoDelitos, rutaBase, "delitos"),
            await GuardarArchivoOriginalAsync(archivoVictimas, rutaBase, "victimas")
        };

        var metadata = new UltimosArchivosEntidadMetadata
        {
            IdEntidadFederativa = idEntidadFederativa,
            CodigoReferencia = codigoReferencia,
            TipoMovimiento = tipoMovimiento,
            MesCorte = mesCorte,
            AnioCorte = anioCorte,
            FechaGuardado = DateTimeOffset.Now,
            Archivos = archivos
        };

        var metadataJson = JsonSerializer.Serialize(metadata, JsonOptions);

        await File.WriteAllTextAsync(Path.Combine(rutaBase, "metadata.json"), metadataJson);
    }

    public async Task<List<UltimosArchivosEntidadResumen>> ObtenerResumenAsync()
    {
        var rutaRaiz = ObtenerRutaRaiz();

        if (!Directory.Exists(rutaRaiz))
        {
            return new List<UltimosArchivosEntidadResumen>();
        }

        var resumen = new List<UltimosArchivosEntidadResumen>();

        foreach (var rutaEntidad in Directory.EnumerateDirectories(rutaRaiz, "entidad-*"))
        {
            var metadata = await LeerMetadataAsync(rutaEntidad);

            if (metadata == null)
            {
                continue;
            }

            resumen.Add(new UltimosArchivosEntidadResumen
            {
                IdEntidadFederativa = metadata.IdEntidadFederativa,
                CodigoReferencia = metadata.CodigoReferencia,
                TipoMovimiento = metadata.TipoMovimiento,
                MesCorte = metadata.MesCorte,
                AnioCorte = metadata.AnioCorte,
                FechaGuardado = metadata.FechaGuardado,
                Archivos = metadata.Archivos
            });
        }

        return resumen
            .OrderBy(x => x.IdEntidadFederativa)
            .ToList();
    }

    public async Task<InformeArchivoZipResponse> DescargarAsync(int idEntidadFederativa)
    {
        var rutaBase = ObtenerRutaEntidad(idEntidadFederativa);

        if (!Directory.Exists(rutaBase))
        {
            throw new InvalidOperationException("No existen archivos originales guardados para la entidad solicitada.");
        }

        var metadata = await LeerMetadataAsync(rutaBase);

        if (metadata == null || metadata.Archivos.Count == 0)
        {
            throw new InvalidOperationException("No existe metadata válida para los archivos originales de la entidad solicitada.");
        }

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var archivo in metadata.Archivos)
            {
                var rutaArchivo = ObtenerRutaArchivoSegura(rutaBase, archivo.RutaRelativa);

                if (!File.Exists(rutaArchivo))
                {
                    throw new InvalidOperationException($"No se encontró el archivo original: {archivo.NombreOriginal}.");
                }

                var rutaZip = archivo.RutaRelativa.Replace('\\', '/');

                archive.CreateEntryFromFile(rutaArchivo, rutaZip, CompressionLevel.Fastest);
            }

            var rutaMetadata = Path.Combine(rutaBase, "metadata.json");

            if (File.Exists(rutaMetadata))
            {
                archive.CreateEntryFromFile(rutaMetadata, "metadata.json", CompressionLevel.Fastest);
            }
        }

        return new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo = $"ARCHIVOS_ORIGINALES_ENTIDAD_{metadata.IdEntidadFederativa:00}_{metadata.AnioCorte}_{metadata.MesCorte:00}.zip"
        };
    }

    public async Task GuardarSemanalAsync(int idEntidadFederativa, string codigoReferencia, string tipoMovimiento, SemanalPeriodoCarga periodo, IFormFile archivoCarpetas, IFormFile archivoDelitos, IFormFile archivoVictimas)
    {
        var rutaBase = ObtenerRutaEntidadSemanal(idEntidadFederativa);

        LimpiarCarpetaEntidad(rutaBase);

        var archivos = new List<UltimosArchivosEntidadArchivo>
        {
            await GuardarArchivoOriginalAsync(archivoCarpetas, rutaBase, "carpetas"),
            await GuardarArchivoOriginalAsync(archivoDelitos, rutaBase, "delitos"),
            await GuardarArchivoOriginalAsync(archivoVictimas, rutaBase, "victimas")
        };

        var metadata = new UltimosArchivosEntidadSemanalMetadata
        {
            IdEntidadFederativa = idEntidadFederativa,
            CodigoReferencia = codigoReferencia,
            TipoMovimiento = tipoMovimiento,
            TipoContenido = periodo.TipoContenido,
            AnioSemana = periodo.AnioSemana,
            NumeroSemana = periodo.NumeroSemana,
            FechaInicioSemana = periodo.FechaInicioSemana,
            FechaFinSemana = periodo.FechaFinSemana,
            FechaInicioTramo = periodo.FechaInicioTramo,
            FechaFinTramo = periodo.FechaFinTramo,
            MesCorte = periodo.MesCorte,
            AnioCorte = periodo.AnioCorte,
            FechaGuardado = DateTimeOffset.Now,
            Archivos = archivos
        };

        var metadataJson = JsonSerializer.Serialize(metadata, JsonOptions);

        await File.WriteAllTextAsync(
            Path.Combine(rutaBase, "metadata.json"),
            metadataJson);
    }

    public async Task<List<UltimosArchivosEntidadSemanalResumen>> ObtenerResumenSemanalAsync()
    {
        var rutaRaiz = ObtenerRutaRaizSemanal();

        if (!Directory.Exists(rutaRaiz))
        {
            return new List<UltimosArchivosEntidadSemanalResumen>();
        }

        var resumen = new List<UltimosArchivosEntidadSemanalResumen>();

        foreach (var rutaEntidad in Directory.EnumerateDirectories(rutaRaiz, "entidad-*"))
        {
            var metadata = await LeerMetadataSemanalAsync(rutaEntidad);

            if (metadata == null)
            {
                continue;
            }

            resumen.Add(new UltimosArchivosEntidadSemanalResumen
            {
                IdEntidadFederativa = metadata.IdEntidadFederativa,
                CodigoReferencia = metadata.CodigoReferencia,
                TipoMovimiento = metadata.TipoMovimiento,
                TipoContenido = metadata.TipoContenido,
                AnioSemana = metadata.AnioSemana,
                NumeroSemana = metadata.NumeroSemana,
                FechaInicioSemana = metadata.FechaInicioSemana,
                FechaFinSemana = metadata.FechaFinSemana,
                FechaInicioTramo = metadata.FechaInicioTramo,
                FechaFinTramo = metadata.FechaFinTramo,
                MesCorte = metadata.MesCorte,
                AnioCorte = metadata.AnioCorte,
                FechaGuardado = metadata.FechaGuardado,
                Archivos = metadata.Archivos
            });
        }

        return resumen
            .OrderBy(x => x.IdEntidadFederativa)
            .ToList();
    }

    public async Task<InformeArchivoZipResponse> DescargarSemanalAsync(int idEntidadFederativa)
    {
        var rutaBase = ObtenerRutaEntidadSemanal(idEntidadFederativa);

        if (!Directory.Exists(rutaBase))
        {
            throw new InvalidOperationException(
                "No existen archivos originales semanales guardados para la entidad solicitada.");
        }

        var metadata = await LeerMetadataSemanalAsync(rutaBase);

        if (metadata == null || metadata.Archivos.Count == 0)
        {
            throw new InvalidOperationException(
                "No existe metadata válida para los archivos originales semanales de la entidad solicitada.");
        }

        using var zipStream = new MemoryStream();

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var archivo in metadata.Archivos)
            {
                var rutaArchivo = ObtenerRutaArchivoSegura(
                    rutaBase,
                    archivo.RutaRelativa);

                if (!File.Exists(rutaArchivo))
                {
                    throw new InvalidOperationException(
                        $"No se encontró el archivo original: {archivo.NombreOriginal}.");
                }

                var rutaZip = archivo.RutaRelativa.Replace('\\', '/');

                archive.CreateEntryFromFile(
                    rutaArchivo,
                    rutaZip,
                    CompressionLevel.Fastest);
            }

            var rutaMetadata = Path.Combine(rutaBase, "metadata.json");

            if (File.Exists(rutaMetadata))
            {
                archive.CreateEntryFromFile(
                    rutaMetadata,
                    "metadata.json",
                    CompressionLevel.Fastest);
            }
        }

        return new InformeArchivoZipResponse
        {
            Archivo = zipStream.ToArray(),
            NombreArchivo =
                $"ARCHIVOS_ORIGINALES_SEMANALES_ENTIDAD_{metadata.IdEntidadFederativa:00}_{metadata.AnioSemana}_SEMANA_{metadata.NumeroSemana:00}.zip"
        };
    }

    private string ObtenerRutaRaiz()
    {
        return Path.Combine(_environment.ContentRootPath, "UltimosArchivosEntidad");
    }

    private string ObtenerRutaEntidad(int idEntidadFederativa)
    {
        return Path.Combine(ObtenerRutaRaiz(), $"entidad-{idEntidadFederativa:00}");
    }

    private static void LimpiarCarpetaEntidad(string rutaBase)
    {
        Directory.CreateDirectory(rutaBase);

        LimpiarSubcarpeta(rutaBase, "carpetas");
        LimpiarSubcarpeta(rutaBase, "delitos");
        LimpiarSubcarpeta(rutaBase, "victimas");

        var rutaMetadata = Path.Combine(rutaBase, "metadata.json");

        if (File.Exists(rutaMetadata))
        {
            File.SetAttributes(rutaMetadata, FileAttributes.Normal);
            File.Delete(rutaMetadata);
        }
    }

    private static void LimpiarSubcarpeta(string rutaBase, string nombreSubcarpeta)
    {
        var rutaSubcarpeta = Path.Combine(rutaBase, nombreSubcarpeta);

        Directory.CreateDirectory(rutaSubcarpeta);

        foreach (var archivo in Directory.GetFiles(rutaSubcarpeta))
        {
            File.SetAttributes(archivo, FileAttributes.Normal);
            File.Delete(archivo);
        }

        foreach (var carpeta in Directory.GetDirectories(rutaSubcarpeta))
        {
            LimpiarDirectorioRecursivo(carpeta);
            Directory.Delete(carpeta, recursive: false);
        }
    }

    private static void LimpiarDirectorioRecursivo(string rutaDirectorio)
    {
        foreach (var archivo in Directory.GetFiles(rutaDirectorio))
        {
            File.SetAttributes(archivo, FileAttributes.Normal);
            File.Delete(archivo);
        }

        foreach (var carpeta in Directory.GetDirectories(rutaDirectorio))
        {
            LimpiarDirectorioRecursivo(carpeta);
            Directory.Delete(carpeta, recursive: false);
        }

        File.SetAttributes(rutaDirectorio, FileAttributes.Normal);
    }

    private static async Task<UltimosArchivosEntidadArchivo> GuardarArchivoOriginalAsync(IFormFile archivo, string rutaBase, string tipo)
    {
        var nombreArchivo = Path.GetFileName(archivo.FileName);

        if (string.IsNullOrWhiteSpace(nombreArchivo))
        {
            nombreArchivo = $"{tipo}.xlsx";
        }

        var rutaTipo = Path.Combine(rutaBase, tipo);

        Directory.CreateDirectory(rutaTipo);

        var rutaDestino = Path.Combine(rutaTipo, nombreArchivo);

        await using (var origen = archivo.OpenReadStream())
        {
            if (origen.CanSeek)
            {
                origen.Position = 0;
            }

            await using var destino = new FileStream(rutaDestino, FileMode.Create, FileAccess.Write, FileShare.None);

            await origen.CopyToAsync(destino);
        }

        var info = new FileInfo(rutaDestino);

        return new UltimosArchivosEntidadArchivo
        {
            Tipo = tipo,
            NombreOriginal = nombreArchivo,
            RutaRelativa = $"{tipo}/{nombreArchivo}",
            TamanioBytes = info.Length,
            Sha256 = await CalcularSha256Async(rutaDestino)
        };
    }

    private static async Task<string> CalcularSha256Async(string rutaArchivo)
    {
        await using var stream = File.OpenRead(rutaArchivo);
        var hash = await SHA256.HashDataAsync(stream);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<UltimosArchivosEntidadMetadata?> LeerMetadataAsync(string rutaBase)
    {
        var rutaMetadata = Path.Combine(rutaBase, "metadata.json");

        if (!File.Exists(rutaMetadata))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(rutaMetadata);
        var metadata = JsonSerializer.Deserialize<UltimosArchivosEntidadMetadata>(json, JsonOptions);

        if (metadata == null)
        {
            return null;
        }

        if (metadata.Archivos.Count > 0)
        {
            return metadata;
        }

        // Compatibilidad con metadata vieja, por si ya existían respaldos guardados antes de este cambio.
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        AgregarArchivoFormatoAnterior(root, "archivoCarpetasOriginal", "carpetas", rutaBase, metadata.Archivos);
        AgregarArchivoFormatoAnterior(root, "archivoDelitosOriginal", "delitos", rutaBase, metadata.Archivos);
        AgregarArchivoFormatoAnterior(root, "archivoVictimasOriginal", "victimas", rutaBase, metadata.Archivos);

        return metadata;
    }

    private static void AgregarArchivoFormatoAnterior(JsonElement root, string propiedad, string tipo, string rutaBase, List<UltimosArchivosEntidadArchivo> archivos)
    {
        if (!root.TryGetProperty(propiedad, out var valor) || valor.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var nombreOriginal = Path.GetFileName(valor.GetString() ?? string.Empty);

        if (string.IsNullOrWhiteSpace(nombreOriginal))
        {
            return;
        }

        var rutaArchivo = Path.Combine(rutaBase, nombreOriginal);
        var tamanioBytes = File.Exists(rutaArchivo) ? new FileInfo(rutaArchivo).Length : 0;

        archivos.Add(new UltimosArchivosEntidadArchivo
        {
            Tipo = tipo,
            NombreOriginal = nombreOriginal,
            RutaRelativa = nombreOriginal,
            TamanioBytes = tamanioBytes,
            Sha256 = string.Empty
        });
    }

    private static string ObtenerRutaArchivoSegura(string rutaBase, string rutaRelativa)
    {
        var rutaBaseCompleta = Path.GetFullPath(rutaBase).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var rutaArchivo = Path.GetFullPath(Path.Combine(rutaBase, rutaRelativa.Replace('/', Path.DirectorySeparatorChar)));

        if (!rutaArchivo.StartsWith(rutaBaseCompleta, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("La ruta del archivo original no es válida.");
        }

        return rutaArchivo;
    }

    private string ObtenerRutaRaizSemanal()
    {
        return Path.Combine(
            _environment.ContentRootPath,
            "UltimosArchivosEntidadSemanal");
    }

    private string ObtenerRutaEntidadSemanal(int idEntidadFederativa)
    {
        return Path.Combine(
            ObtenerRutaRaizSemanal(),
            $"entidad-{idEntidadFederativa:00}");
    }

    private static async Task<UltimosArchivosEntidadSemanalMetadata?> LeerMetadataSemanalAsync(string rutaBase)
    {
        var rutaMetadata = Path.Combine(rutaBase, "metadata.json");

        if (!File.Exists(rutaMetadata))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(rutaMetadata);

        return JsonSerializer.Deserialize<UltimosArchivosEntidadSemanalMetadata>(
            json,
            JsonOptions);
    }
}