using System.Globalization;
using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;

public class FeminicidioVictimaValidator
{
    private const string ClaveFeminicidio = "1.03";
    private const string ClaveNacionalidadMexicana = "73";

    private static readonly string[] ColumnasFeminicidio =
    {
        "nombre_vicfem",
        "1apellido_vicfem",
        "2apellido_vicfem",
        "curp_vicfem"
    };

    public (List<CargaValidacionError> Errores, List<CargaValidacionError> Advertencias) Validar(List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas)
    {
        var errores = new List<CargaValidacionError>();
        var advertencias = new List<CargaValidacionError>();

        var feminicidios = filasDelitos
            .Where(EsFeminicidio)
            .Select(fila => CrearLlave(
                ObtenerValor(fila, "id_ci"),
                ObtenerValor(fila, "id_delito")))
            .Where(llave => !string.IsNullOrWhiteSpace(llave))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Si la carga no contiene feminicidios, las cuatro columnas
        // pueden incluso no existir en el archivo de víctimas.
        if (feminicidios.Count == 0) return (errores, advertencias);

        // Si existe al menos un feminicidio, el archivo de víctimas
        // sí debe contener las cuatro columnas del nuevo formato.
        var columnasArchivo = filasVictimas
            .SelectMany(fila => fila.Columnas.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var columna in ColumnasFeminicidio)
        {
            if (columnasArchivo.Contains(columna)) continue;

            errores.Add(new CargaValidacionError
            {
                Archivo = "victimas",
                Fila = 1,
                Columna = columna,
                Campo = columna,
                Valor = null,
                Codigo = "FEMINICIDIO_COLUMNA_OBLIGATORIA_NO_ENCONTRADA",
                DescripcionResumen = "Columna de feminicidio obligatoria no encontrada",
                Mensaje = $"El archivo de víctimas contiene registros asociados a feminicidio y debe incluir la columna \"{columna}\"."
            });
        }

        if (errores.Count > 0) return (errores, advertencias);

        var mexicanasSinCurp = 0;
        var mexicanasSinNombreOApellidos = 0;
        var extranjerasSinNombre = 0;
        var extranjerasSinPrimerApellido = 0;

        foreach (var fila in filasVictimas)
        {
            var llave = CrearLlave(
                ObtenerValor(fila, "id_ci"),
                ObtenerValor(fila, "id_delito"));

            // Las nuevas reglas solamente aplican a víctimas
            // relacionadas con un delito de feminicidio.
            if (!feminicidios.Contains(llave)) continue;

            var nacionalidad = ObtenerValor(fila, "nacional")?.Trim();

            // NACIONAL ya fue validado previamente por VictimasValidator
            // y CatalogosValidator. Si viene incorrecto, ellos generan el error.
            if (string.IsNullOrWhiteSpace(nacionalidad)) continue;

            var nombre = ObtenerValor(fila, "nombre_vicfem");
            var primerApellido = ObtenerValor(fila, "1apellido_vicfem");
            var segundoApellido = ObtenerValor(fila, "2apellido_vicfem");
            var curp = ObtenerValor(fila, "curp_vicfem");

            if (EsNacionalidadMexicana(nacionalidad))
            {
                // Mexicana:
                // CURP obligatorio.
                // Nombre y ambos apellidos opcionales, pero generan
                // una sola advertencia agregada si falta alguno.
                if (string.IsNullOrWhiteSpace(curp)) mexicanasSinCurp++;

                if (string.IsNullOrWhiteSpace(nombre) ||
                    string.IsNullOrWhiteSpace(primerApellido) ||
                    string.IsNullOrWhiteSpace(segundoApellido))
                {
                    mexicanasSinNombreOApellidos++;
                }

                continue;
            }

            // Nacionalidad distinta a mexicana:
            // CURP no se valida y se conserva tal como venga.
            // Nombre y primer apellido son obligatorios.
            // Segundo apellido es opcional.
            if (string.IsNullOrWhiteSpace(nombre)) extranjerasSinNombre++;
            if (string.IsNullOrWhiteSpace(primerApellido)) extranjerasSinPrimerApellido++;
        }

        if (mexicanasSinCurp > 0)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = "victimas",
                Fila = null,
                Columna = "curp_vicfem",
                Campo = "curp_vicfem",
                Valor = mexicanasSinCurp.ToString(CultureInfo.InvariantCulture),
                Codigo = "FEMINICIDIO_MEXICANA_CURP_OBLIGATORIO",
                DescripcionResumen = "Víctimas de feminicidio mexicanas sin CURP",
                Mensaje = $"Se están reportando {mexicanasSinCurp} víctimas de feminicidio sin CURP, se requiere la información para finalizar la carga.",
                TotalRegistrosAfectados = mexicanasSinCurp
            });
        }

        if (extranjerasSinNombre > 0)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = "victimas",
                Fila = null,
                Columna = "nombre_vicfem",
                Campo = "nombre_vicfem",
                Valor = extranjerasSinNombre.ToString(CultureInfo.InvariantCulture),
                Codigo = "FEMINICIDIO_EXTRANJERA_NOMBRE_OBLIGATORIO",
                DescripcionResumen = "Víctimas de feminicidio extranjeras sin nombre",
                Mensaje = $"Se están reportando {extranjerasSinNombre} víctimas de feminicidio de nacionalidad extranjera sin nombre(s), se requiere la información para finalizar la carga.",
                TotalRegistrosAfectados = extranjerasSinNombre
            });
        }

        if (extranjerasSinPrimerApellido > 0)
        {
            errores.Add(new CargaValidacionError
            {
                Archivo = "victimas",
                Fila = null,
                Columna = "1apellido_vicfem",
                Campo = "1apellido_vicfem",
                Valor = extranjerasSinPrimerApellido.ToString(CultureInfo.InvariantCulture),
                Codigo = "FEMINICIDIO_EXTRANJERA_PRIMER_APELLIDO_OBLIGATORIO",
                DescripcionResumen = "Víctimas de feminicidio extranjeras sin primer apellido",
                Mensaje = $"Se están reportando {extranjerasSinPrimerApellido} víctimas de feminicidio de nacionalidad extranjera sin primer apellido, se requiere la información para finalizar la carga.",
                TotalRegistrosAfectados = extranjerasSinPrimerApellido
            });
        }

        if (mexicanasSinNombreOApellidos > 0)
        {
            advertencias.Add(new CargaValidacionError
            {
                Archivo = "victimas",
                Fila = null,
                Columna = "nombre_vicfem+1apellido_vicfem+2apellido_vicfem",
                Campo = "nombre_vicfem+1apellido_vicfem+2apellido_vicfem",
                Valor = mexicanasSinNombreOApellidos.ToString(CultureInfo.InvariantCulture),
                Codigo = "FEMINICIDIO_MEXICANA_NOMBRE_APELLIDOS_ADVERTENCIA",
                DescripcionResumen = "Víctimas de feminicidio mexicanas sin nombre y/o apellidos",
                Mensaje = $"Se detectaron {mexicanasSinNombreOApellidos} víctimas de feminicidio de nacionalidad mexicana sin nombre y/o apellidos completos. ¿Desea continuar con la carga?",
                TotalRegistrosAfectados = mexicanasSinNombreOApellidos
            });
        }

        return (errores, advertencias);
    }

    private static bool EsFeminicidio(ArchivoFila fila)
    {
        return string.Equals(
            ObtenerValor(fila, "clasf_de_dto")?.Trim(),
            ClaveFeminicidio,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool EsNacionalidadMexicana(string nacionalidad)
    {
        if (int.TryParse(nacionalidad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clave)) return clave == 73;

        return string.Equals(
            nacionalidad.Trim(),
            ClaveNacionalidadMexicana,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CrearLlave(string? idCi, string? idDelito)
    {
        idCi = idCi?.Trim();
        idDelito = idDelito?.Trim();

        if (string.IsNullOrWhiteSpace(idCi) || string.IsNullOrWhiteSpace(idDelito)) return string.Empty;

        return $"{idCi}|{idDelito}";
    }

    private static string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }
}