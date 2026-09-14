using SIIID2.Api.Models;
using SIIID2.Api.Readers;

namespace SIIID2.Api.Services;

public class BanciCargaService : IBanciCargaService
{
    private readonly IBanciArchivoReader _archivoReader;

    private static readonly HashSet<string> ClasificacionesPermitidas =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "2.07.01",
            "2.07.02",
            "2.07.03",
            "2.07.04",
            "2.08.01",
            "2.08.02",
            "2.08.03",
            "2.08.04"
        };

    public BanciCargaService(
        IBanciArchivoReader archivoReader)
    {
        _archivoReader = archivoReader;
    }

    public async Task<BanciCargaValidacionResponse> ValidarArchivosAsync(
        BanciCargaArchivosRequest request,
        int idUsuarioCarga)
    {
        _ = idUsuarioCarga;

        var lectura =
            await _archivoReader.LeerAsync(request);

        var response =
            new BanciCargaValidacionResponse
            {
                ModalidadIngreso =
                    lectura.ModalidadIngreso,

                TotalCarpetas =
                    lectura.Carpetas.Count,

                TotalDelitos =
                    lectura.Delitos.Count,

                TotalVictimas =
                    lectura.Victimas.Count
            };

        response.Errores.AddRange(
            lectura.Errores);

        if (response.Errores.Count > 0)
        {
            response.Mensaje =
                "No fue posible leer correctamente la estructura de los archivos BANCI.";

            return response;
        }

        ValidarRegistrosMinimos(
            lectura,
            response.Errores);

        ValidarDuplicados(
            lectura,
            response.Errores);

        ValidarIntegridad(
            lectura,
            response.Errores);

        ValidarClasificaciones(
            lectura.Delitos,
            response.Errores);

        response.Mensaje =
            response.EsValido
                ? "Los archivos BANCI tienen una estructura válida."
                : $"Se encontraron {response.Errores.Count} errores en la información BANCI.";

        return response;
    }

    private static void ValidarRegistrosMinimos(
        BanciLecturaArchivosResultado lectura,
        List<BanciCargaValidacionError> errores)
    {
        if (lectura.Carpetas.Count == 0)
        {
            errores.Add(ErrorGeneral(
                "BANCI_SIN_CARPETAS",
                "No se encontraron registros de carpetas."));
        }

        if (lectura.Delitos.Count == 0)
        {
            errores.Add(ErrorGeneral(
                "BANCI_SIN_DELITOS",
                "No se encontraron registros de delitos."));
        }

        if (lectura.Victimas.Count == 0)
        {
            errores.Add(ErrorGeneral(
                "BANCI_SIN_VICTIMAS",
                "No se encontraron registros de víctimas."));
        }

        foreach (var fila in lectura.Carpetas)
        {
            ValidarCampoLlave(
                fila,
                "id_ci",
                "CARPETA",
                errores);
        }

        foreach (var fila in lectura.Delitos)
        {
            ValidarCampoLlave(
                fila,
                "id_ci",
                "DELITO",
                errores);

            ValidarCampoLlave(
                fila,
                "id_delito",
                "DELITO",
                errores);

            ValidarCampoLlave(
                fila,
                "clasf_de_dto",
                "DELITO",
                errores);
        }

        foreach (var fila in lectura.Victimas)
        {
            ValidarCampoLlave(
                fila,
                "id_ci",
                "VICTIMA",
                errores);

            ValidarCampoLlave(
                fila,
                "id_delito",
                "VICTIMA",
                errores);

            ValidarCampoLlave(
                fila,
                "id_vicf",
                "VICTIMA",
                errores);
        }
    }

    private static void ValidarDuplicados(
        BanciLecturaArchivosResultado lectura,
        List<BanciCargaValidacionError> errores)
    {
        var carpetas =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var fila in lectura.Carpetas)
        {
            var idCi =
                Valor(fila, "id_ci");

            if (string.IsNullOrWhiteSpace(idCi))
            {
                continue;
            }

            if (!carpetas.Add(idCi))
            {
                errores.Add(new BanciCargaValidacionError
                {
                    Archivo = "carpetas",
                    NumeroFila = fila.NumeroFila,
                    Campo = "id_ci",
                    Valor = idCi,
                    Codigo = "BANCI_CARPETA_DUPLICADA",
                    Mensaje = $"El ID_CI {idCi} está duplicado en Carpetas."
                });
            }
        }

        var delitos =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var fila in lectura.Delitos)
        {
            var idCi =
                Valor(fila, "id_ci");

            var idDelito =
                Valor(fila, "id_delito");

            if (string.IsNullOrWhiteSpace(idCi) ||
                string.IsNullOrWhiteSpace(idDelito))
            {
                continue;
            }

            var llave =
                Llave(idCi, idDelito);

            if (!delitos.Add(llave))
            {
                errores.Add(new BanciCargaValidacionError
                {
                    Archivo = "delitos",
                    NumeroFila = fila.NumeroFila,
                    Campo = "id_delito",
                    Valor = idDelito,
                    Codigo = "BANCI_DELITO_DUPLICADO",
                    Mensaje = $"La combinación ID_CI {idCi} / ID_DELITO {idDelito} está duplicada."
                });
            }
        }

        var victimas =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var fila in lectura.Victimas)
        {
            var idCi =
                Valor(fila, "id_ci");

            var idDelito =
                Valor(fila, "id_delito");

            var idVicf =
                Valor(fila, "id_vicf");

            if (string.IsNullOrWhiteSpace(idCi) ||
                string.IsNullOrWhiteSpace(idDelito) ||
                string.IsNullOrWhiteSpace(idVicf))
            {
                continue;
            }

            var llave =
                Llave(
                    idCi,
                    idDelito,
                    idVicf);

            if (!victimas.Add(llave))
            {
                errores.Add(new BanciCargaValidacionError
                {
                    Archivo = "victimas",
                    NumeroFila = fila.NumeroFila,
                    Campo = "id_vicf",
                    Valor = idVicf,
                    Codigo = "BANCI_VICTIMA_DUPLICADA",
                    Mensaje = $"La combinación ID_CI {idCi} / ID_DELITO {idDelito} / ID_VICF {idVicf} está duplicada."
                });
            }
        }
    }

    private static void ValidarIntegridad(
        BanciLecturaArchivosResultado lectura,
        List<BanciCargaValidacionError> errores)
    {
        var carpetas =
            lectura.Carpetas
                .Select(x =>
                    Valor(x, "id_ci"))
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x))
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        var delitos =
            lectura.Delitos
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        Valor(x, "id_ci")) &&
                    !string.IsNullOrWhiteSpace(
                        Valor(x, "id_delito")))
                .Select(x =>
                    Llave(
                        Valor(x, "id_ci"),
                        Valor(x, "id_delito")))
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        foreach (var fila in lectura.Delitos)
        {
            var idCi =
                Valor(fila, "id_ci");

            if (string.IsNullOrWhiteSpace(idCi))
            {
                continue;
            }

            if (!carpetas.Contains(idCi))
            {
                errores.Add(new BanciCargaValidacionError
                {
                    Archivo = "delitos",
                    NumeroFila = fila.NumeroFila,
                    Campo = "id_ci",
                    Valor = idCi,
                    Codigo = "BANCI_DELITO_SIN_CARPETA",
                    Mensaje = $"El delito referencia el ID_CI {idCi}, pero esa carpeta no existe en la carga."
                });
            }
        }

        foreach (var fila in lectura.Victimas)
        {
            var idCi =
                Valor(fila, "id_ci");

            var idDelito =
                Valor(fila, "id_delito");

            if (string.IsNullOrWhiteSpace(idCi) ||
                string.IsNullOrWhiteSpace(idDelito))
            {
                continue;
            }

            var llave =
                Llave(idCi, idDelito);

            if (!delitos.Contains(llave))
            {
                errores.Add(new BanciCargaValidacionError
                {
                    Archivo = "victimas",
                    NumeroFila = fila.NumeroFila,
                    Campo = "id_delito",
                    Valor = idDelito,
                    Codigo = "BANCI_VICTIMA_SIN_DELITO",
                    Mensaje = $"La víctima referencia ID_CI {idCi} / ID_DELITO {idDelito}, pero ese delito no existe en la carga."
                });
            }
        }
    }

    private static void ValidarClasificaciones(
        IEnumerable<ArchivoFila> delitos,
        List<BanciCargaValidacionError> errores)
    {
        foreach (var fila in delitos)
        {
            var clasificacion =
                Valor(
                    fila,
                    "clasf_de_dto");

            if (string.IsNullOrWhiteSpace(clasificacion))
            {
                continue;
            }

            if (ClasificacionesPermitidas.Contains(
                    clasificacion))
            {
                continue;
            }

            errores.Add(new BanciCargaValidacionError
            {
                Archivo = "delitos",
                NumeroFila = fila.NumeroFila,
                Campo = "clasf_de_dto",
                Valor = clasificacion,
                Codigo = "BANCI_CLASIFICACION_NO_PERMITIDA",
                Mensaje = $"La clasificación {clasificacion} no pertenece a los delitos BANCI 2.07.xx o 2.08.xx."
            });
        }
    }

    private static void ValidarCampoLlave(
        ArchivoFila fila,
        string campo,
        string tipo,
        List<BanciCargaValidacionError> errores)
    {
        var valor =
            Valor(fila, campo);

        if (!string.IsNullOrWhiteSpace(valor))
        {
            return;
        }

        errores.Add(new BanciCargaValidacionError
        {
            Archivo = tipo.ToLowerInvariant(),
            NumeroFila = fila.NumeroFila,
            Campo = campo,
            Codigo = $"BANCI_{tipo}_{campo.ToUpperInvariant()}_REQUERIDO",
            Mensaje = $"El campo {campo} es obligatorio para identificar el registro."
        });
    }

    private static string Valor(
        ArchivoFila fila,
        string columna)
    {
        return fila.Columnas.TryGetValue(
                columna,
                out var valor)
            ? valor?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static string Llave(
        params string[] valores)
    {
        return string.Join(
            "\u001F",
            valores.Select(x =>
                x.Trim().ToUpperInvariant()));
    }

    private static BanciCargaValidacionError ErrorGeneral(
        string codigo,
        string mensaje)
    {
        return new BanciCargaValidacionError
        {
            Archivo = "general",
            Codigo = codigo,
            Mensaje = mensaje
        };
    }
}