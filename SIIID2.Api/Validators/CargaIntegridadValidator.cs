using System.Globalization;
using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

public class CargaIntegridadValidator
{
    // Modelo interno para trabajar las carpetas en las validaciones cruzadas.
    private class CarpetaIntegridad
    {
        public ArchivoFila Fila { get; set; } = null!;
        public string IdCi { get; set; } = string.Empty;
        public string? FechaInicio { get; set; }
    }

    // Modelo interno para trabajar delitos y víctimas en las validaciones cruzadas.
    private class RegistroIntegridad
    {
        public ArchivoFila Fila { get; set; } = null!;
        public string? IdCi { get; set; }
        public string? IdDelito { get; set; }
        public string? FechaHechos { get; set; }
        public string? IdVicf { get; set; }
    }

    public List<CargaValidacionError> Validar(List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas)
    {
        var errores = new List<CargaValidacionError>();
        // Construimos índice de carpetas por ID_CI.
        var carpetasPorIdCi = filasCarpetas
            .Select(f => new CarpetaIntegridad
            {
                Fila = f,
                IdCi = ObtenerValor(f, "id_ci")?.Trim() ?? string.Empty,
                FechaInicio = ObtenerValor(f, "fha_de_ini")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.IdCi))
            .GroupBy(x => x.IdCi, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Construimos lista de delitos con sus llaves principales.
        var delitosPorIdCi = filasDelitos
            .Select(f => new RegistroIntegridad
            {
                Fila = f,
                IdCi = ObtenerValor(f, "id_ci")?.Trim(),
                IdDelito = ObtenerValor(f, "id_delito")?.Trim(),
                FechaHechos = ObtenerValor(f, "fha_de_hchos")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.IdCi))
            .ToList();

        // Construimos lista de víctimas con sus llaves principales.
        var victimasPorIdCi = filasVictimas
            .Select(f => new RegistroIntegridad
            {
                Fila = f,
                IdCi = ObtenerValor(f, "id_ci")?.Trim(),
                IdDelito = ObtenerValor(f, "id_delito")?.Trim(),
                IdVicf = ObtenerValor(f, "id_vicf")?.Trim()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.IdCi))
            .ToList();

        var idsCarpetas = carpetasPorIdCi.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var idsCiDelitos = delitosPorIdCi
            .Where(x => !string.IsNullOrWhiteSpace(x.IdCi))
            .Select(x => x.IdCi!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var idsCiVictimas = victimasPorIdCi
            .Where(x => !string.IsNullOrWhiteSpace(x.IdCi))
            .Select(x => x.IdCi!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var llavesDelitos = delitosPorIdCi
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.IdCi) &&
                !string.IsNullOrWhiteSpace(x.IdDelito))
            .Select(x => CrearLlaveDelito(x.IdCi!, x.IdDelito!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var llavesVictimas = victimasPorIdCi
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.IdCi) &&
                !string.IsNullOrWhiteSpace(x.IdDelito))
            .Select(x => CrearLlaveDelito(x.IdCi!, x.IdDelito!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Regla: la cantidad de delitos no debe ser mayor que la cantidad de víctimas.
        ValidarConteoDelitosVictimas(filasDelitos, filasVictimas, errores);
        // Regla: cada carpeta debe tener al menos un delito y una víctima.
        ValidarCarpetasConDelitosYVictimas(filasCarpetas, idsCiDelitos, idsCiVictimas, errores);
        // Regla: cada delito debe pertenecer a una carpeta existente.
        ValidarDelitosContraCarpetas(delitosPorIdCi, idsCarpetas, errores);
        // Regla: cada delito debe tener al menos una víctima asociada.
        ValidarDelitosConVictimas(delitosPorIdCi, llavesVictimas, errores);
        // Regla: cada víctima debe apuntar a una carpeta y a un delito existentes.
        ValidarVictimasContraCarpetasYDelitos(victimasPorIdCi, idsCarpetas, llavesDelitos, errores);
        // Regla: fecha de hechos no puede ser mayor que fecha de inicio.
        ValidarFechasHechosContraFechaInicio(delitosPorIdCi, carpetasPorIdCi, errores);
        return errores;
    }

    private void ValidarConteoDelitosVictimas(List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas, List<CargaValidacionError> errores)
    {
        if (filasDelitos.Count <= filasVictimas.Count)
        { 
            return; 
        }
        errores.Add(new CargaValidacionError
        {
            Archivo = "general",
            Fila = null,
            Columna = "",
            Campo = "",
            Valor = null,
            Codigo = "INTEGRIDAD_TOTAL_DELITOS_MAYOR_TOTAL_VICTIMAS",
            DescripcionResumen = "Total de delitos mayor que total de víctimas",
            Mensaje = $"La cantidad de delitos ({filasDelitos.Count}) no puede ser mayor que la cantidad de víctimas ({filasVictimas.Count})."
        });
    }

    private void ValidarCarpetasConDelitosYVictimas(List<ArchivoFila> filasCarpetas, HashSet<string> idsCiDelitos,  HashSet<string> idsCiVictimas, List<CargaValidacionError> errores)
    {
        foreach (var fila in filasCarpetas)
        {
            var idCi = ObtenerValor(fila, "id_ci")?.Trim();

            if (string.IsNullOrWhiteSpace(idCi))
            {
                continue;
            }    

            if (!idsCiDelitos.Contains(idCi))
            {
                AgregarError(
                    errores,
                    "carpetas",
                    fila,
                    "id_ci",
                    "INTEGRIDAD_CARPETA_SIN_DELITOS",
                    "Carpetas sin registro en delitos",
                    $"La carpeta ID_CI \"{idCi}\" no tiene registros asociados en el archivo de delitos.");
            }

            if (!idsCiVictimas.Contains(idCi))
            {
                AgregarError(
                    errores,
                    "carpetas",
                    fila,
                    "id_ci",
                    "INTEGRIDAD_CARPETA_SIN_VICTIMAS",
                    "Carpetas sin registro en víctimas",
                    $"La carpeta ID_CI \"{idCi}\" no tiene registros asociados en el archivo de víctimas.");
            }
        }
    }

    private void ValidarDelitosContraCarpetas(List<RegistroIntegridad> delitosPorIdCi, HashSet<string> idsCarpetas, List<CargaValidacionError> errores)
    {
        foreach (var delito in delitosPorIdCi)
        {
            var idCi = delito.IdCi;

            if (string.IsNullOrWhiteSpace(idCi))
            {
                continue;
            }
                
            if (!idsCarpetas.Contains(idCi))
            {
                AgregarError(
                    errores,
                    "delitos",
                    delito.Fila,
                    "id_ci",
                    "INTEGRIDAD_DELITO_ID_CI_NO_EXISTE_EN_CARPETAS",
                    "Delitos con ID_CI inexistente en carpetas",
                    $"El delito apunta al ID_CI \"{idCi}\", pero ese ID_CI no existe en el archivo de carpetas.");
            }
        }
    }

    private void ValidarDelitosConVictimas(List<RegistroIntegridad> delitosPorIdCi, HashSet<string> llavesVictimas, List<CargaValidacionError> errores)
    {
        foreach (var delito in delitosPorIdCi)
        {
            var idCi = delito.IdCi;
            var idDelito = delito.IdDelito;

            if (string.IsNullOrWhiteSpace(idCi) || string.IsNullOrWhiteSpace(idDelito)) 
            {
                continue;
            }
                
            var llaveDelito = CrearLlaveDelito(idCi, idDelito);

            if (!llavesVictimas.Contains(llaveDelito))
            {
                AgregarError(
                    errores,
                    "delitos",
                    delito.Fila,
                    "id_ci+id_delito",
                    "INTEGRIDAD_DELITO_SIN_VICTIMAS",
                    "Delitos sin víctima asociada",
                    $"El delito ID_DELITO \"{idDelito}\" de la carpeta ID_CI \"{idCi}\" no tiene víctimas asociadas.");
            }
        }
    }

    private void ValidarVictimasContraCarpetasYDelitos(List<RegistroIntegridad> victimasPorIdCi, HashSet<string> idsCarpetas, HashSet<string> llavesDelitos, List<CargaValidacionError> errores)
    {
        foreach (var victima in victimasPorIdCi)
        {
            var idCi = victima.IdCi;
            var idDelito = victima.IdDelito;

            if (!string.IsNullOrWhiteSpace(idCi) && !idsCarpetas.Contains(idCi))
            {
                AgregarError(
                    errores,
                    "victimas",
                    victima.Fila,
                    "id_ci",
                    "INTEGRIDAD_VICTIMA_ID_CI_NO_EXISTE_EN_CARPETAS",
                    "Víctimas con ID_CI inexistente en carpetas",
                    $"La víctima apunta al ID_CI \"{idCi}\", pero ese ID_CI no existe en el archivo de carpetas.");
            }

            if (string.IsNullOrWhiteSpace(idCi) || string.IsNullOrWhiteSpace(idDelito)) 
            {
                continue;
            }
            var llaveDelito = CrearLlaveDelito(idCi, idDelito);
            if (!llavesDelitos.Contains(llaveDelito))
            {
                AgregarError(
                    errores,
                    "victimas",
                    victima.Fila,
                    "id_ci+id_delito",
                    "INTEGRIDAD_VICTIMA_ID_DELITO_NO_EXISTE_EN_DELITOS",
                    "Víctimas con ID_DELITO inexistente en delitos",
                    $"La víctima apunta al delito ID_DELITO \"{idDelito}\" de la carpeta ID_CI \"{idCi}\", pero esa relación no existe en el archivo de delitos.");
            }
        }
    }

    private void ValidarFechasHechosContraFechaInicio(List<RegistroIntegridad> delitosPorIdCi, Dictionary<string, CarpetaIntegridad> carpetasPorIdCi, List<CargaValidacionError> errores)
    {
        foreach (var delito in delitosPorIdCi)
        {
            var idCi = delito.IdCi;
            var fechaHechosValor = delito.FechaHechos;

            if (string.IsNullOrWhiteSpace(idCi)) 
            {
                continue;
            }

            if (!carpetasPorIdCi.TryGetValue(idCi, out var carpeta)) 
            {
                continue;
            }
                
            var fechaInicioValor = carpeta.FechaInicio;

            if (!IntentarConvertirFecha(fechaHechosValor, out var fechaHechos))
            {
                continue;
            }

            if (!IntentarConvertirFecha(fechaInicioValor, out var fechaInicio))
            {
                continue;
            } 

            if (fechaHechos > fechaInicio)
            {
                AgregarError(
                    errores,
                    "delitos",
                    delito.Fila,
                    "fha_de_hchos",
                    "INTEGRIDAD_FECHA_HECHOS_MAYOR_FECHA_INICIO",
                    "Fecha de hechos mayor que fecha de inicio",
                    $"La fecha de hechos ({fechaHechos:yyyy-MM-dd}) no puede ser mayor que la fecha de inicio de la carpeta ({fechaInicio:yyyy-MM-dd}). ID_CI: \"{idCi}\".");
            }
        }
    }

    private static string CrearLlaveDelito(string idCi, string idDelito)
    {
        return $"{idCi.Trim()}|{idDelito.Trim()}";
    }

    private static string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }

    private static bool IntentarConvertirFecha(string? valor, out DateTime fecha)
    {
        fecha = default;

        if (string.IsNullOrWhiteSpace(valor))
            return false;

        valor = valor.Trim();

        var fechaCarga = DateTime.Today;
        var mesInmediatoAnterior = fechaCarga.AddMonths(-1);

        var formatos = new[]
        {
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyyMMdd",

        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",

        "MM/dd/yyyy",
        "M/d/yyyy",
        "MM-dd-yyyy",
        "M-d-yyyy"
    };

        var posiblesFechas = new List<DateTime>();

        foreach (var formato in formatos)
        {
            if (DateTime.TryParseExact(valor, formato, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fechaParseada))
            {
                posiblesFechas.Add(fechaParseada.Date);
            }
        }

        if (DateTime.TryParse(valor, new CultureInfo("es-MX"), DateTimeStyles.None, out var fechaMx))
        {
            posiblesFechas.Add(fechaMx.Date);
        }

        if (DateTime.TryParse(valor, new CultureInfo("en-US"), DateTimeStyles.None, out var fechaUs))
        {
            posiblesFechas.Add(fechaUs.Date);
        }

        if (double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeroExcel))
        {
            if (numeroExcel > 0 && numeroExcel < 60000)
            {
                try
                {
                    posiblesFechas.Add(DateTime.FromOADate(numeroExcel).Date);
                }
                catch
                {
                    // Si no se puede convertir, se ignora.
                }
            }
        }

        if (double.TryParse(valor, NumberStyles.Any, new CultureInfo("es-MX"), out var numeroExcelMx))
        {
            if (numeroExcelMx > 0 && numeroExcelMx < 60000)
            {
                try
                {
                    posiblesFechas.Add(DateTime.FromOADate(numeroExcelMx).Date);
                }
                catch
                {
                    // Si no se puede convertir, se ignora.
                }
            }
        }

        posiblesFechas = posiblesFechas.Distinct().ToList();

        if (posiblesFechas.Count == 0)
        {
            return false;
        } 

        // Si hay fechas ambiguas, se prefiere la que cae en el mes inmediato anterior.
        // Esto evita interpretar 01/04/2026 como enero 4 cuando realmente es abril 1.
        var fechaMesAnterior = posiblesFechas.FirstOrDefault(f => f.Year == mesInmediatoAnterior.Year && f.Month == mesInmediatoAnterior.Month);

        if (fechaMesAnterior != default)
        {
            fecha = fechaMesAnterior;
            return true;
        }

        // Si no hay coincidencia con el mes esperado, se toma la primera fecha parseada.
        fecha = posiblesFechas.First();
        return true;
    }

    private static void AgregarError(List<CargaValidacionError> errores, string archivo, ArchivoFila fila, string columna, string codigo, string descripcionResumen, string mensaje)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        errores.Add(new CargaValidacionError
        {
            Archivo = archivo,
            Fila = fila.NumeroFila,
            Columna = columna,
            Campo = columna,
            Valor = valor,
            Codigo = codigo,
            DescripcionResumen = descripcionResumen,
            Mensaje = mensaje
        });
    }
}