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
        public string? HoraInicio { get; set; }
    }

    // Modelo interno para trabajar delitos y víctimas en las validaciones cruzadas.
    private class RegistroIntegridad
    {
        public ArchivoFila Fila { get; set; } = null!;
        public string? IdCi { get; set; }
        public string? IdDelito { get; set; }
        public string? FechaHechos { get; set; }
        public string? HoraHechos { get; set; }
        public string? IdVicf { get; set; }
    }

    public List<CargaValidacionError> Validar(List<ArchivoFila> filasCarpetas, List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas)
    {
        var errores = new List<CargaValidacionError>();

        // Regla: ID_CI debe ser único en carpetas.
        ValidarIdCiUnicoEnCarpetas(filasCarpetas, errores);

        // Regla: ID_CI + ID_DELITO debe ser único en delitos.
        ValidarIdDelitoUnicoPorCarpeta(filasDelitos, errores);

        // Regla: ID_CI + ID_DELITO + ID_VICF debe ser único en víctimas.
        ValidarIdVictimaUnicoPorDelito(filasVictimas, errores);

        // Construimos índice de carpetas por ID_CI.
        var carpetasPorIdCi = filasCarpetas
            .Select(f => new CarpetaIntegridad
            {
                Fila = f,
                IdCi = ObtenerValor(f, "id_ci")?.Trim() ?? string.Empty,
                FechaInicio = ObtenerValor(f, "fha_de_ini"),
                HoraInicio = ObtenerValor(f, "hra_de_ini")
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
                FechaHechos = ObtenerValor(f, "fha_de_hchos"),
                HoraHechos = ObtenerValor(f, "hra_de_hchos")
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
            var horaHechosValor = delito.HoraHechos;

            if (string.IsNullOrWhiteSpace(idCi)) 
            {
                continue;
            }

            if (!carpetasPorIdCi.TryGetValue(idCi, out var carpeta)) 
            {
                continue;
            }
                
            var fechaInicioValor = carpeta.FechaInicio;
            var horaInicioValor = carpeta.HoraInicio;

            if (!IntentarConvertirFechaHora(fechaHechosValor, horaHechosValor, out var fechaHechos))
            {
                continue;
            }

            if (!IntentarConvertirFechaHora(fechaInicioValor, horaInicioValor, out var fechaInicio))
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

        // Las fechas textuales de los archivos se interpretan en formato mexicano.
        // Ejemplo:
        // 01/04/2026 = 1 de abril de 2026
        // 04/02/2026 = 4 de febrero de 2026
        //
        // No usamos MM/dd/yyyy ni cultura en-US para evitar que fechas ambiguas
        // se interpreten como mes/día/año.
        var formatos = new[]
        {
        "dd/MM/yyyy",
        "d/M/yyyy",
        "dd-MM-yyyy",
        "d-M-yyyy",
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyyMMdd"
    };

        foreach (var formato in formatos)
        {
            if (DateTime.TryParseExact(
                    valor,
                    formato,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var fechaParseada))
            {
                fecha = fechaParseada.Date;
                return true;
            }
        }

        // Intento general con cultura mexicana.
        if (DateTime.TryParse(
                valor,
                new CultureInfo("es-MX"),
                DateTimeStyles.None,
                out var fechaMx))
        {
            fecha = fechaMx.Date;
            return true;
        }

        // Soporte para fechas como número serial de Excel.
        if (double.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeroExcel))
        {
            if (numeroExcel > 0 && numeroExcel < 60000)
            {
                try
                {
                    fecha = DateTime.FromOADate(numeroExcel).Date;
                    return true;
                }
                catch
                {
                    // Si no se puede convertir, se ignora.
                }
            }
        }

        // Soporte para número serial de Excel con cultura mexicana.
        if (double.TryParse(valor, NumberStyles.Any, new CultureInfo("es-MX"), out var numeroExcelMx))
        {
            if (numeroExcelMx > 0 && numeroExcelMx < 60000)
            {
                try
                {
                    fecha = DateTime.FromOADate(numeroExcelMx).Date;
                    return true;
                }
                catch
                {
                    // Si no se puede convertir, se ignora.
                }
            }
        }

        return false;
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

    private void ValidarIdCiUnicoEnCarpetas(List<ArchivoFila> filasCarpetas, List<CargaValidacionError> errores)
    {
        var carpetasDuplicadas = filasCarpetas
            .Select(f => new
            {
                Fila = f,
                IdCi = ObtenerValor(f, "id_ci")?.Trim()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.IdCi))
            .GroupBy(x => x.IdCi!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var grupo in carpetasDuplicadas)
        {
            var filas = grupo
                .Select(x => x.Fila.NumeroFila)
                .OrderBy(x => x)
                .ToList();

            foreach (var item in grupo)
            {
                AgregarError(
                    errores,
                    "carpetas",
                    item.Fila,
                    "id_ci",
                    "INTEGRIDAD_ID_CI_DUPLICADO_PERIODO",
                    "ID_CI duplicado en el periodo",
                    $"El ID_CI \"{grupo.Key}\" está duplicado en el archivo de carpetas. Filas detectadas: {string.Join(", ", filas)}. El ID_CI debe ser único para el periodo.");
            }
        }
    }

    private void ValidarIdDelitoUnicoPorCarpeta(List<ArchivoFila> filasDelitos, List<CargaValidacionError> errores)
    {
        var delitosDuplicados = filasDelitos
            .Select(f => new
            {
                Fila = f,
                IdCi = ObtenerValor(f, "id_ci")?.Trim(),
                IdDelito = ObtenerValor(f, "id_delito")?.Trim()
            })
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.IdCi) &&
                !string.IsNullOrWhiteSpace(x.IdDelito))
            .GroupBy(
                x => CrearLlaveDelito(x.IdCi!, x.IdDelito!),
                StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var grupo in delitosDuplicados)
        {
            var filas = grupo
                .Select(x => x.Fila.NumeroFila)
                .OrderBy(x => x)
                .ToList();

            foreach (var item in grupo)
            {
                AgregarError(
                    errores,
                    "delitos",
                    item.Fila,
                    "id_ci+id_delito",
                    "INTEGRIDAD_ID_DELITO_DUPLICADO_EN_CARPETA",
                    "ID_DELITO duplicado para la carpeta",
                    $"La combinación ID_CI \"{item.IdCi}\" + ID_DELITO \"{item.IdDelito}\" está duplicada en el archivo de delitos. Filas detectadas: {string.Join(", ", filas)}. La combinación debe ser única para el periodo.");
            }
        }
    }

    private void ValidarIdVictimaUnicoPorDelito(List<ArchivoFila> filasVictimas, List<CargaValidacionError> errores)
    {
        var victimasDuplicadas = filasVictimas
            .Select(f => new
            {
                Fila = f,
                IdCi = ObtenerValor(f, "id_ci")?.Trim(),
                IdDelito = ObtenerValor(f, "id_delito")?.Trim(),
                IdVicf = ObtenerValor(f, "id_vicf")?.Trim()
            })
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.IdCi) &&
                !string.IsNullOrWhiteSpace(x.IdDelito) &&
                !string.IsNullOrWhiteSpace(x.IdVicf))
            .GroupBy(
                x => CrearLlaveVictima(x.IdCi!, x.IdDelito!, x.IdVicf!),
                StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var grupo in victimasDuplicadas)
        {
            var filas = grupo
                .Select(x => x.Fila.NumeroFila)
                .OrderBy(x => x)
                .ToList();

            foreach (var item in grupo)
            {
                AgregarError(
                    errores,
                    "victimas",
                    item.Fila,
                    "id_ci+id_delito+id_vicf",
                    "INTEGRIDAD_ID_VICTIMA_DUPLICADO_EN_DELITO",
                    "ID_VICF duplicado para el delito",
                    $"La combinación ID_CI \"{item.IdCi}\" + ID_DELITO \"{item.IdDelito}\" + ID_VICF \"{item.IdVicf}\" está duplicada en el archivo de víctimas. Filas detectadas: {string.Join(", ", filas)}. La combinación debe ser única para el periodo.");
            }
        }
    }

    private static string CrearLlaveVictima(string idCi, string idDelito, string idVicf)
    {
        return $"{idCi.Trim()}|{idDelito.Trim()}|{idVicf.Trim()}";
    }

    private static bool IntentarConvertirFechaHora(string? fechaValor, string? horaValor, out DateTime fechaHora)
    {
        fechaHora = default;

        if (!IntentarConvertirFecha(fechaValor, out var fecha))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(horaValor))
        {
            fechaHora = fecha;
            return true;
        }

        horaValor = horaValor.Trim();

        if (TimeSpan.TryParse(horaValor, CultureInfo.InvariantCulture, out var hora))
        {
            fechaHora = fecha.Date.Add(hora);
            return true;
        }

        if (DateTime.TryParse(
                horaValor,
                new CultureInfo("es-MX"),
                DateTimeStyles.None,
                out var horaComoFecha))
        {
            fechaHora = fecha.Date.Add(horaComoFecha.TimeOfDay);
            return true;
        }

        if (double.TryParse(horaValor.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var numeroExcel))
        {
            if (numeroExcel >= 0 && numeroExcel < 1)
            {
                fechaHora = fecha.Date.Add(TimeSpan.FromDays(numeroExcel));
                return true;
            }
        }

        fechaHora = fecha;
        return true;
    }

}