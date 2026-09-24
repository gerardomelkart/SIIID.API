using System.Globalization;
using SIIID2.Api.Models;
using SIIID2.Api.Services;

namespace SIIID2.Api.Validators;

public sealed class FeminicidioRenapoValidator
{
    private const string ClaveFeminicidio = "1.03";
    private const int ConsultasSimultaneas = 4;

    private readonly IRenapoCurpService _renapoCurpService;
    private readonly SistemaConfiguracionService _config;

    public FeminicidioRenapoValidator(IRenapoCurpService renapoCurpService, SistemaConfiguracionService config)
    {
        _renapoCurpService = renapoCurpService;
        _config = config;
    }

    public async Task<(List<CargaValidacionError> Errores, List<CargaValidacionError> Advertencias)> ValidarAsync(List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas, CancellationToken cancellationToken = default)
    {
        var errores = new List<CargaValidacionError>();
        var advertencias = new List<CargaValidacionError>();
        if (!_config.Activa("MENSUAL", "RENAPO")) return (errores, advertencias);

        var feminicidios = filasDelitos
            .Where(EsFeminicidio)
            .Select(fila => CrearLlave(Valor(fila, "id_ci"), Valor(fila, "id_delito")))
            .Where(llave => !string.IsNullOrWhiteSpace(llave))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (feminicidios.Count == 0) return (errores, advertencias);

        var victimas = filasVictimas
            .Select(fila => new
            {
                Fila = fila,
                Llave = CrearLlave(Valor(fila, "id_ci"), Valor(fila, "id_delito")),
                Nacionalidad = Valor(fila, "nacional")?.Trim(),
                Curp = Valor(fila, "curp_vicfem")?.Trim().ToUpperInvariant()
            })
            .Where(x => feminicidios.Contains(x.Llave) && EsMexicana(x.Nacionalidad) && !string.IsNullOrWhiteSpace(x.Curp))
            .ToList();

        if (victimas.Count == 0) return (errores, advertencias);

        var grupos = victimas
            .GroupBy(x => x.Curp!, StringComparer.OrdinalIgnoreCase)
            .Select(grupo => new { Curp = grupo.Key, Filas = grupo.Select(x => x.Fila).ToList() })
            .ToList();

        var gruposProcesados = 0;

        foreach (var lote in grupos.Chunk(ConsultasSimultaneas))
        {
            var consultas = lote.Select(async grupo => new
            {
                Grupo = grupo,
                Resultado = await _renapoCurpService.ConsultarAsync(grupo.Curp, cancellationToken)
            });

            var resultados = await Task.WhenAll(consultas);
            var registrosSinValidarEnEsteLote = 0;
            var servicioNoDisponible = false;

            foreach (var consulta in resultados)
            {
                var grupo = consulta.Grupo;
                var resultado = consulta.Resultado;

                switch (resultado.Estado)
                {
                    case EstadoConsultaRenapo.Encontrada:
                        break;

                    case EstadoConsultaRenapo.NoEncontrada:
                        errores.Add(new CargaValidacionError
                        {
                            Archivo = "victimas",
                            Fila = grupo.Filas[0].NumeroFila,
                            Columna = "curp_vicfem",
                            Campo = "curp_vicfem",
                            Valor = grupo.Curp,
                            Codigo = "FEMINICIDIO_CURP_NO_ENCONTRADA_RENAPO",
                            DescripcionResumen = "CURP no encontrada en RENAPO",
                            Mensaje = $"La CURP {grupo.Curp} no se encuentra en la base de datos de RENAPO. Debe corregir este dato para continuar con la carga.",
                            TotalRegistrosAfectados = grupo.Filas.Count
                        });
                        break;

                    case EstadoConsultaRenapo.CurpInvalida:
                        errores.Add(new CargaValidacionError
                        {
                            Archivo = "victimas",
                            Fila = grupo.Filas[0].NumeroFila,
                            Columna = "curp_vicfem",
                            Campo = "curp_vicfem",
                            Valor = grupo.Curp,
                            Codigo = "FEMINICIDIO_CURP_INVALIDA_RENAPO",
                            DescripcionResumen = "CURP inválida según RENAPO",
                            Mensaje = $"RENAPO rechazó la CURP {grupo.Curp} por formato incorrecto. Debe corregir este dato para continuar con la carga.",
                            TotalRegistrosAfectados = grupo.Filas.Count
                        });
                        break;

                    case EstadoConsultaRenapo.NoDisponible:
                        servicioNoDisponible = true;
                        registrosSinValidarEnEsteLote += grupo.Filas.Count;
                        break;

                    case EstadoConsultaRenapo.ErrorValidacion:
                    default:
                        errores.Add(new CargaValidacionError
                        {
                            Archivo = "general",
                            Fila = null,
                            Columna = "curp_vicfem",
                            Campo = "curp_vicfem",
                            Valor = null,
                            Codigo = "FEMINICIDIO_RENAPO_ERROR_VALIDACION",
                            DescripcionResumen = "Error en la validación contra RENAPO",
                            Mensaje = "No fue posible completar la validación contra RENAPO debido a un error de configuración, autorización o respuesta del servicio. Debe revisarse el problema antes de continuar con la carga.",
                            TotalRegistrosAfectados = grupo.Filas.Count
                        });
                        break;
                }
            }

            gruposProcesados += lote.Length;

            if (!servicioNoDisponible) continue;

            var registrosPendientes = grupos.Skip(gruposProcesados).Sum(grupo => grupo.Filas.Count);
            var totalSinValidar = registrosSinValidarEnEsteLote + registrosPendientes;

            advertencias.Add(new CargaValidacionError
            {
                Archivo = "general",
                Fila = null,
                Columna = "curp_vicfem",
                Campo = "curp_vicfem",
                Valor = null,
                Codigo = "FEMINICIDIO_RENAPO_NO_DISPONIBLE",
                DescripcionResumen = "RENAPO no disponible",
                Mensaje = $"No fue posible validar {totalSinValidar} registro(s) de víctimas de feminicidio contra RENAPO por una falla de comunicación o indisponibilidad temporal del servicio. La validación externa quedó incompleta. ¿Desea continuar con la carga?",
                TotalRegistrosAfectados = totalSinValidar
            });

            break;
        }

        return (errores, advertencias);
    }

    private static bool EsFeminicidio(ArchivoFila fila) => string.Equals(Valor(fila, "clasf_de_dto")?.Trim(), ClaveFeminicidio, StringComparison.OrdinalIgnoreCase);

    private static bool EsMexicana(string? nacionalidad) => int.TryParse(nacionalidad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clave) && clave == 73;

    private static string CrearLlave(string? idCi, string? idDelito)
    {
        idCi = idCi?.Trim();
        idDelito = idDelito?.Trim();
        return string.IsNullOrWhiteSpace(idCi) || string.IsNullOrWhiteSpace(idDelito) ? string.Empty : $"{idCi}|{idDelito}";
    }

    private static string? Valor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }
}