using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using SIIID2.Api.Models;

namespace SIIID2.Api.Readers;

public class BanciArchivoReader : IBanciArchivoReader
{
    private readonly IArchivoReader _archivoReader;

    private const long TamanioMaximoBytes = 50L * 1024L * 1024L;

    private static readonly string[] ColumnasCarpetas =
    [
        "entidad",
        "id_ci",
        "ntra_ci",
        "fha_de_ini",
        "hra_de_ini",
        "rmen_de_hchos",
        "ord_apreh",
        "fgran",
        "ctaon",
        "td_v_ap",
        "proc_abrev",
        "juc_oral",
        "td_sen_con",
        "no_ejer_acc_pnal",
        "otra",
        "dic"
    ];

    private static readonly string[] ColumnasDelitos =
    [
        "entidad",
        "id_ci",
        "id_delito",
        "dto",
        "moda_dto",
        "forma_acc",
        "fha_de_hchos",
        "hra_de_hchos",
        "emto_com_dto",
        "grdo_cons",
        "clasf_de_dto",
        "nom_ent_hchos",
        "id_ent_hchos",
        "nom_mun_hchos",
        "id_mun_hchos",
        "nom_loc_hchos",
        "id_loc_hchos",
        "nom_col_hchos",
        "id_col_hchos",
        "cp",
        "coord_x",
        "coord_y",
        "dom_hchos"
    ];

    private static readonly string[] ColumnasVictimas =
    [
        "entidad",
        "id_ci",
        "id_delito",
        "id_vicf",
        "sexo",
        "genero",
        "pob",
        "disc",
        "fha_nac",
        "edad",
        "nacional",
        "no_banci",
        "folio_fotovolante",
        "folio_rnpdno",
        "pro_apellido",
        "sdo_apellido",
        "nomb",
        "entidad_nacimiento",
        "estado_migratorio",
        "curp",
        "rfc",
        "fecha_ultimo_contacto",
        "hora_ultimo_contacto",
        "entidad_visto",
        "municipio_visto",
        "lugar_ultimo_contacto",
        "senas_tatuaje_datos_identificacion",
        "localizado_o_no_localizado",
        "con_o_sin_vida",
        "fecha_localizacion",
        "voluntaria",
        "fue_delito",
        "delito",
        "obs"
    ];

    private static readonly HashSet<string> ColumnasFecha =
    [
        "fha_de_ini",
        "fha_de_hchos",
        "fha_nac",
        "fecha_ultimo_contacto",
        "fecha_localizacion"
    ];

    private static readonly HashSet<string> ColumnasHora =
    [
        "hra_de_ini",
        "hra_de_hchos",
        "hora_ultimo_contacto"
    ];

    private static readonly Dictionary<string, string> AliasCarpetas =
        CrearMapaAlias(
            ColumnasCarpetas,
            ("fha_ini", "fha_de_ini"),
            ("fecha_inicio", "fha_de_ini"),
            ("hra_ini", "hra_de_ini"),
            ("hora_inicio", "hra_de_ini"),
            ("pro_abrev", "proc_abrev"),
            ("td_sec_con", "td_sen_con")
        );

    private static readonly Dictionary<string, string> AliasDelitos =
        CrearMapaAlias(
            ColumnasDelitos,
            ("fecha_hechos", "fha_de_hchos"),
            ("hora_hechos", "hra_de_hchos"),
            ("forma_accion", "forma_acc"),
            ("clasificacion_delito", "clasf_de_dto")
        );

    private static readonly Dictionary<string, string> AliasVictimas =
        CrearMapaAlias(
            ColumnasVictimas,
            ("nacionalidad", "nacional"),
            ("nombre", "nomb"),
            ("nombres", "nomb"),
            ("primer_apellido", "pro_apellido"),
            ("segundo_apellido", "sdo_apellido"),
            ("lugar_ultimo_contaco", "lugar_ultimo_contacto"),
            ("fecha_localizacion_victima", "fecha_localizacion"),
            ("senas_tatuaje_datos_identificaci_n", "senas_tatuaje_datos_identificacion"),
            ("senas_tatuajes_datos_identificacion", "senas_tatuaje_datos_identificacion")
        );

    public BanciArchivoReader(IArchivoReader archivoReader)
    {
        _archivoReader = archivoReader;
    }

    public async Task<BanciLecturaArchivosResultado> LeerAsync(
        BanciCargaArchivosRequest request)
    {
        var resultado = new BanciLecturaArchivosResultado();

        var tieneLibro = request.ArchivoLibro != null;

        var tieneArchivoCarpetas = request.ArchivoCarpetas != null;
        var tieneArchivoDelitos = request.ArchivoDelitos != null;
        var tieneArchivoVictimas = request.ArchivoVictimas != null;

        var cantidadSeparados =
            Convert.ToInt32(tieneArchivoCarpetas) +
            Convert.ToInt32(tieneArchivoDelitos) +
            Convert.ToInt32(tieneArchivoVictimas);

        if (tieneLibro && cantidadSeparados > 0)
        {
            resultado.Errores.Add(ErrorGeneral(
                "BANCI_MODALIDAD_AMBIGUA",
                "Debe enviar un solo Excel con tres pestañas o tres archivos separados, pero no ambas modalidades al mismo tiempo."));

            return resultado;
        }

        if (!tieneLibro && cantidadSeparados == 0)
        {
            resultado.Errores.Add(ErrorGeneral(
                "BANCI_SIN_ARCHIVOS",
                "Debe enviar un Excel con las pestañas CI, Delitos y Victimas, o bien los tres archivos separados."));

            return resultado;
        }

        if (!tieneLibro && cantidadSeparados != 3)
        {
            resultado.Errores.Add(ErrorGeneral(
                "BANCI_ARCHIVOS_INCOMPLETOS",
                "Para la modalidad de archivos separados debe enviar Carpetas, Delitos y Víctimas."));

            return resultado;
        }

        if (tieneLibro)
        {
            resultado.ModalidadIngreso = "UN_EXCEL_TRES_HOJAS";

            ValidarArchivoBase(
                request.ArchivoLibro!,
                permitirCsv: false,
                resultado.Errores);

            if (resultado.Errores.Count > 0)
            {
                return resultado;
            }

            await LeerLibroAsync(
                request.ArchivoLibro!,
                resultado);

            return resultado;
        }

        resultado.ModalidadIngreso = "TRES_ARCHIVOS";

        ValidarArchivoBase(
            request.ArchivoCarpetas!,
            permitirCsv: true,
            resultado.Errores);

        ValidarArchivoBase(
            request.ArchivoDelitos!,
            permitirCsv: true,
            resultado.Errores);

        ValidarArchivoBase(
            request.ArchivoVictimas!,
            permitirCsv: true,
            resultado.Errores);

        if (resultado.Errores.Count > 0)
        {
            return resultado;
        }

        resultado.Carpetas = await LeerArchivoIndividualAsync(
            request.ArchivoCarpetas!,
            "CARPETA",
            ColumnasCarpetas,
            AliasCarpetas,
            ["id_ci", "ntra_ci", "fha_de_ini"],
            resultado.Errores);

        resultado.Delitos = await LeerArchivoIndividualAsync(
            request.ArchivoDelitos!,
            "DELITO",
            ColumnasDelitos,
            AliasDelitos,
            ["id_ci", "id_delito", "clasf_de_dto"],
            resultado.Errores);

        resultado.Victimas = await LeerArchivoIndividualAsync(
            request.ArchivoVictimas!,
            "VICTIMA",
            ColumnasVictimas,
            AliasVictimas,
            ["id_ci", "id_delito", "id_vicf"],
            resultado.Errores);

        return resultado;
    }

    private async Task LeerLibroAsync(
        IFormFile archivo,
        BanciLecturaArchivosResultado resultado)
    {
        await using var stream = archivo.OpenReadStream();

        using var workbook = new XLWorkbook(stream);

        var hojaCarpetas = BuscarHoja(
            workbook,
            ["ci", "carpetas", "carpeta"]);

        var hojaDelitos = BuscarHoja(
            workbook,
            ["delitos", "delito"]);

        var hojaVictimas = BuscarHoja(
            workbook,
            ["victimas", "victima"]);

        if (hojaCarpetas == null)
        {
            resultado.Errores.Add(new BanciCargaValidacionError
            {
                Archivo = archivo.FileName,
                Hoja = "CI",
                Codigo = "BANCI_FALTA_HOJA_CI",
                Mensaje = "El Excel debe contener una hoja CI."
            });
        }

        if (hojaDelitos == null)
        {
            resultado.Errores.Add(new BanciCargaValidacionError
            {
                Archivo = archivo.FileName,
                Hoja = "Delitos",
                Codigo = "BANCI_FALTA_HOJA_DELITOS",
                Mensaje = "El Excel debe contener una hoja Delitos."
            });
        }

        if (hojaVictimas == null)
        {
            resultado.Errores.Add(new BanciCargaValidacionError
            {
                Archivo = archivo.FileName,
                Hoja = "Victimas",
                Codigo = "BANCI_FALTA_HOJA_VICTIMAS",
                Mensaje = "El Excel debe contener una hoja Victimas."
            });
        }

        if (resultado.Errores.Count > 0)
        {
            return;
        }

        resultado.Carpetas = LeerHojaExcel(
            archivo.FileName,
            hojaCarpetas!,
            "CARPETA",
            ColumnasCarpetas,
            AliasCarpetas,
            ["id_ci", "ntra_ci", "fha_de_ini"],
            resultado.Errores);

        resultado.Delitos = LeerHojaExcel(
            archivo.FileName,
            hojaDelitos!,
            "DELITO",
            ColumnasDelitos,
            AliasDelitos,
            ["id_ci", "id_delito", "clasf_de_dto"],
            resultado.Errores);

        resultado.Victimas = LeerHojaExcel(
            archivo.FileName,
            hojaVictimas!,
            "VICTIMA",
            ColumnasVictimas,
            AliasVictimas,
            ["id_ci", "id_delito", "id_vicf"],
            resultado.Errores);
    }

    private async Task<List<ArchivoFila>> LeerArchivoIndividualAsync(
        IFormFile archivo,
        string tipo,
        IReadOnlyCollection<string> columnasEsperadas,
        IReadOnlyDictionary<string, string> alias,
        IReadOnlyCollection<string> clavesMinimas,
        List<BanciCargaValidacionError> errores)
    {
        var extension = Path
            .GetExtension(archivo.FileName)
            .ToLowerInvariant();

        if (extension == ".xlsx")
        {
            await using var stream = archivo.OpenReadStream();

            using var workbook = new XLWorkbook(stream);

            var worksheet = workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
            {
                errores.Add(new BanciCargaValidacionError
                {
                    Archivo = archivo.FileName,
                    Codigo = $"BANCI_{tipo}_SIN_HOJA",
                    Mensaje = $"El archivo {archivo.FileName} no contiene una hoja de datos."
                });

                return [];
            }

            return LeerHojaExcel(
                archivo.FileName,
                worksheet,
                tipo,
                columnasEsperadas,
                alias,
                clavesMinimas,
                errores);
        }

        var encabezados = await _archivoReader.LeerEncabezadosAsync(archivo);

        var mapaEncabezados = ConstruirMapaEncabezadosCsv(
            archivo.FileName,
            tipo,
            encabezados,
            columnasEsperadas,
            alias,
            clavesMinimas,
            errores);

        if (errores.Count > 0)
        {
            return [];
        }

        var filasOriginales = await _archivoReader.LeerAsync(archivo);

        return RemapearFilas(
            filasOriginales,
            columnasEsperadas,
            mapaEncabezados);
    }

    private static List<ArchivoFila> LeerHojaExcel(
        string nombreArchivo,
        IXLWorksheet worksheet,
        string tipo,
        IReadOnlyCollection<string> columnasEsperadas,
        IReadOnlyDictionary<string, string> alias,
        IReadOnlyCollection<string> clavesMinimas,
        List<BanciCargaValidacionError> errores)
    {
        var filas = new List<ArchivoFila>();

        var primeraFilaUsada = worksheet.FirstRowUsed();
        var ultimaFilaUsada = worksheet.LastRowUsed();
        var ultimaColumnaUsada = worksheet.LastColumnUsed();

        if (primeraFilaUsada == null ||
            ultimaFilaUsada == null ||
            ultimaColumnaUsada == null)
        {
            errores.Add(new BanciCargaValidacionError
            {
                Archivo = nombreArchivo,
                Hoja = worksheet.Name,
                Codigo = $"BANCI_{tipo}_HOJA_VACIA",
                Mensaje = $"La hoja {worksheet.Name} está vacía."
            });

            return filas;
        }

        var filaEncabezado = EncontrarFilaEncabezado(
            worksheet,
            primeraFilaUsada.RowNumber(),
            ultimaFilaUsada.RowNumber(),
            ultimaColumnaUsada.ColumnNumber(),
            alias,
            clavesMinimas);

        if (!filaEncabezado.HasValue)
        {
            errores.Add(new BanciCargaValidacionError
            {
                Archivo = nombreArchivo,
                Hoja = worksheet.Name,
                Codigo = $"BANCI_{tipo}_ENCABEZADO_NO_ENCONTRADO",
                Mensaje = $"No fue posible localizar la fila de encabezados oficiales en la hoja {worksheet.Name}."
            });

            return filas;
        }

        var mapaColumnas = ConstruirMapaColumnasExcel(
            nombreArchivo,
            worksheet,
            tipo,
            filaEncabezado.Value,
            ultimaColumnaUsada.ColumnNumber(),
            columnasEsperadas,
            alias,
            errores);

        if (errores.Any(x =>
                x.Archivo == nombreArchivo &&
                x.Hoja == worksheet.Name))
        {
            return filas;
        }

        for (var row = filaEncabezado.Value + 1;
             row <= ultimaFilaUsada.RowNumber();
             row++)
        {
            var fila = new ArchivoFila
            {
                NumeroFila = row
            };

            var tieneDatoOficial = false;

            foreach (var columna in columnasEsperadas)
            {
                string? valor = null;

                var coincidencia = mapaColumnas
                    .FirstOrDefault(x =>
                        string.Equals(
                            x.Value,
                            columna,
                            StringComparison.OrdinalIgnoreCase));

                if (coincidencia.Key > 0)
                {
                    valor = ObtenerValorCelda(
                        columna,
                        worksheet.Cell(row, coincidencia.Key));
                }

                if (!string.IsNullOrWhiteSpace(valor))
                {
                    tieneDatoOficial = true;
                }

                fila.Columnas[columna] = valor;
            }

            if (tieneDatoOficial)
            {
                filas.Add(fila);
            }
        }

        return filas;
    }

    private static int? EncontrarFilaEncabezado(
        IXLWorksheet worksheet,
        int primeraFila,
        int ultimaFila,
        int ultimaColumna,
        IReadOnlyDictionary<string, string> alias,
        IReadOnlyCollection<string> clavesMinimas)
    {
        var hastaFila = Math.Min(
            ultimaFila,
            primeraFila + 25);

        int? mejorFila = null;
        var mejorPuntaje = -1;

        for (var row = primeraFila;
             row <= hastaFila;
             row++)
        {
            var columnasEncontradas =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            for (var col = 1;
                 col <= ultimaColumna;
                 col++)
            {
                var texto = worksheet
                    .Cell(row, col)
                    .GetString();

                var normalizado =
                    NormalizarNombreColumna(texto);

                if (alias.TryGetValue(
                        normalizado,
                        out var canonico))
                {
                    columnasEncontradas.Add(canonico);
                }
            }

            if (!clavesMinimas.All(
                    columnasEncontradas.Contains))
            {
                continue;
            }

            if (columnasEncontradas.Count >
                mejorPuntaje)
            {
                mejorPuntaje =
                    columnasEncontradas.Count;

                mejorFila = row;
            }
        }

        return mejorFila;
    }

    private static Dictionary<int, string> ConstruirMapaColumnasExcel(
        string nombreArchivo,
        IXLWorksheet worksheet,
        string tipo,
        int filaEncabezado,
        int ultimaColumna,
        IReadOnlyCollection<string> columnasEsperadas,
        IReadOnlyDictionary<string, string> alias,
        List<BanciCargaValidacionError> errores)
    {
        var mapa =
            new Dictionary<int, string>();

        var canonicosEncontrados =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

        for (var col = 1;
             col <= ultimaColumna;
             col++)
        {
            var encabezado = worksheet
                .Cell(filaEncabezado, col)
                .GetString();

            var normalizado =
                NormalizarNombreColumna(encabezado);

            if (!alias.TryGetValue(
                    normalizado,
                    out var canonico))
            {
                continue;
            }

            if (canonicosEncontrados.ContainsKey(canonico))
            {
                errores.Add(new BanciCargaValidacionError
                {
                    Archivo = nombreArchivo,
                    Hoja = worksheet.Name,
                    NumeroFila = filaEncabezado,
                    Campo = canonico,
                    Codigo = $"BANCI_{tipo}_COLUMNA_DUPLICADA",
                    Mensaje = $"La columna oficial {canonico} aparece más de una vez en la hoja {worksheet.Name}."
                });

                continue;
            }

            canonicosEncontrados[canonico] = col;
            mapa[col] = canonico;
        }

        foreach (var columna in columnasEsperadas)
        {
            if (canonicosEncontrados.ContainsKey(columna))
            {
                continue;
            }

            errores.Add(new BanciCargaValidacionError
            {
                Archivo = nombreArchivo,
                Hoja = worksheet.Name,
                NumeroFila = filaEncabezado,
                Campo = columna,
                Codigo = $"BANCI_{tipo}_COLUMNA_FALTANTE",
                Mensaje = $"Falta la columna oficial {columna} en la hoja {worksheet.Name}."
            });
        }

        return mapa;
    }

    private static Dictionary<string, string> ConstruirMapaEncabezadosCsv(
        string nombreArchivo,
        string tipo,
        IReadOnlyCollection<string> encabezados,
        IReadOnlyCollection<string> columnasEsperadas,
        IReadOnlyDictionary<string, string> alias,
        IReadOnlyCollection<string> clavesMinimas,
        List<BanciCargaValidacionError> errores)
    {
        var mapa =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        var canonicos =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var encabezado in encabezados)
        {
            var normalizado =
                NormalizarNombreColumna(encabezado);

            if (!alias.TryGetValue(
                    normalizado,
                    out var canonico))
            {
                continue;
            }

            if (!canonicos.Add(canonico))
            {
                errores.Add(new BanciCargaValidacionError
                {
                    Archivo = nombreArchivo,
                    Campo = canonico,
                    Codigo = $"BANCI_{tipo}_COLUMNA_DUPLICADA",
                    Mensaje = $"La columna oficial {canonico} aparece más de una vez."
                });

                continue;
            }

            mapa[encabezado] = canonico;
        }

        if (!clavesMinimas.All(canonicos.Contains))
        {
            errores.Add(new BanciCargaValidacionError
            {
                Archivo = nombreArchivo,
                Codigo = $"BANCI_{tipo}_ENCABEZADO_INVALIDO",
                Mensaje = "El archivo no contiene los identificadores mínimos esperados."
            });
        }

        foreach (var columna in columnasEsperadas)
        {
            if (canonicos.Contains(columna))
            {
                continue;
            }

            errores.Add(new BanciCargaValidacionError
            {
                Archivo = nombreArchivo,
                Campo = columna,
                Codigo = $"BANCI_{tipo}_COLUMNA_FALTANTE",
                Mensaje = $"Falta la columna oficial {columna}."
            });
        }

        return mapa;
    }

    private static List<ArchivoFila> RemapearFilas(
        IReadOnlyCollection<ArchivoFila> filasOriginales,
        IReadOnlyCollection<string> columnasEsperadas,
        IReadOnlyDictionary<string, string> mapaEncabezados)
    {
        var filas =
            new List<ArchivoFila>();

        foreach (var original in filasOriginales)
        {
            var fila = new ArchivoFila
            {
                NumeroFila = original.NumeroFila
            };

            var tieneDato = false;

            foreach (var columna in columnasEsperadas)
            {
                string? valor = null;

                var encabezadoOriginal =
                    mapaEncabezados
                        .FirstOrDefault(x =>
                            string.Equals(
                                x.Value,
                                columna,
                                StringComparison.OrdinalIgnoreCase))
                        .Key;

                if (!string.IsNullOrWhiteSpace(encabezadoOriginal))
                {
                    original.Columnas.TryGetValue(
                        encabezadoOriginal,
                        out valor);
                }

                valor = valor?.Trim();

                if (!string.IsNullOrWhiteSpace(valor))
                {
                    tieneDato = true;
                }

                fila.Columnas[columna] = valor;
            }

            if (tieneDato)
            {
                filas.Add(fila);
            }
        }

        return filas;
    }

    private static IXLWorksheet? BuscarHoja(
        XLWorkbook workbook,
        IReadOnlyCollection<string> nombres)
    {
        foreach (var worksheet in workbook.Worksheets)
        {
            var nombre =
                NormalizarNombreColumna(worksheet.Name);

            if (nombres.Any(x =>
                    string.Equals(
                        nombre,
                        NormalizarNombreColumna(x),
                        StringComparison.OrdinalIgnoreCase)))
            {
                return worksheet;
            }
        }

        return null;
    }

    private static string? ObtenerValorCelda(
        string columna,
        IXLCell celda)
    {
        if (celda.IsEmpty())
        {
            return null;
        }

        if (ColumnasFecha.Contains(columna))
        {
            if (celda.DataType == XLDataType.DateTime)
            {
                return celda
                    .GetDateTime()
                    .ToString(
                        "dd/MM/yyyy",
                        CultureInfo.InvariantCulture);
            }

            if (celda.DataType == XLDataType.Number)
            {
                var numero =
                    celda.GetDouble();

                if (numero > 0 &&
                    numero < 100000)
                {
                    try
                    {
                        return DateTime
                            .FromOADate(numero)
                            .ToString(
                                "dd/MM/yyyy",
                                CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                    }
                }
            }
        }

        if (ColumnasHora.Contains(columna))
        {
            if (celda.DataType == XLDataType.TimeSpan)
            {
                return celda
                    .GetTimeSpan()
                    .ToString(@"hh\:mm\:ss");
            }

            if (celda.DataType == XLDataType.DateTime)
            {
                return celda
                    .GetDateTime()
                    .ToString(
                        "HH:mm:ss",
                        CultureInfo.InvariantCulture);
            }

            if (celda.DataType == XLDataType.Number)
            {
                var numero =
                    celda.GetDouble();

                if (numero >= 0 &&
                    numero < 1)
                {
                    try
                    {
                        return DateTime
                            .FromOADate(numero)
                            .ToString(
                                "HH:mm:ss",
                                CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                    }
                }
            }
        }

        var valor =
            celda.GetFormattedString();

        return string.IsNullOrWhiteSpace(valor)
            ? null
            : valor.Trim();
    }

    private static void ValidarArchivoBase(
        IFormFile archivo,
        bool permitirCsv,
        List<BanciCargaValidacionError> errores)
    {
        if (archivo.Length == 0)
        {
            errores.Add(new BanciCargaValidacionError
            {
                Archivo = archivo.FileName,
                Codigo = "BANCI_ARCHIVO_VACIO",
                Mensaje = $"El archivo {archivo.FileName} está vacío."
            });

            return;
        }

        if (archivo.Length > TamanioMaximoBytes)
        {
            errores.Add(new BanciCargaValidacionError
            {
                Archivo = archivo.FileName,
                Codigo = "BANCI_ARCHIVO_EXCEDE_TAMANIO",
                Mensaje = $"El archivo {archivo.FileName} excede el tamaño máximo permitido de 50 MB."
            });
        }

        var extension = Path
            .GetExtension(archivo.FileName)
            .ToLowerInvariant();

        var permitido =
            extension == ".xlsx" ||
            (permitirCsv && extension == ".csv");

        if (!permitido)
        {
            errores.Add(new BanciCargaValidacionError
            {
                Archivo = archivo.FileName,
                Codigo = "BANCI_EXTENSION_NO_PERMITIDA",
                Mensaje = permitirCsv
                    ? "Sólo se permiten archivos .xlsx o .csv."
                    : "Para la modalidad de un solo archivo se requiere un archivo .xlsx."
            });
        }
    }

    private static Dictionary<string, string> CrearMapaAlias(
        IEnumerable<string> columnas,
        params (string Alias, string Canonico)[] adicionales)
    {
        var resultado =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var columna in columnas)
        {
            resultado[NormalizarNombreColumna(columna)] =
                columna;
        }

        foreach (var adicional in adicionales)
        {
            resultado[NormalizarNombreColumna(adicional.Alias)] =
                adicional.Canonico;
        }

        return resultado;
    }

    private static string NormalizarNombreColumna(
        string? columna)
    {
        if (string.IsNullOrWhiteSpace(columna))
        {
            return string.Empty;
        }

        var texto = columna
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD);

        var caracteres =
            texto
                .Where(c =>
                    CharUnicodeInfo.GetUnicodeCategory(c) !=
                    UnicodeCategory.NonSpacingMark)
                .ToArray();

        texto = new string(caracteres)
            .Normalize(NormalizationForm.FormC);

        texto = Regex.Replace(
            texto,
            @"[^a-z0-9]+",
            "_");

        texto = Regex.Replace(
            texto,
            @"_+",
            "_");

        return texto.Trim('_');
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