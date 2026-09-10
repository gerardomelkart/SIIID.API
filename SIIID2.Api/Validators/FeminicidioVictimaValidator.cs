using System.Globalization;
using SIIID2.Api.Models;
using SIIID2.Api.Repositories;

namespace SIIID2.Api.Validators;

public class FeminicidioVictimaValidator
{
    private readonly ICatalogoRepository _catalogoRepository;

    public FeminicidioVictimaValidator(ICatalogoRepository catalogoRepository)
    {
        _catalogoRepository = catalogoRepository;
    }

    public async Task<(List<CargaValidacionError> Errores, List<CargaValidacionError> Advertencias)> ValidarAsync(List<ArchivoFila> filasDelitos, List<ArchivoFila> filasVictimas)
    {
        var errores = new List<CargaValidacionError>();
        var advertencias = new List<CargaValidacionError>();

        var claveMexicana = await _catalogoRepository.ObtenerClaveNacionalidadMexicanaAsync();

        if (string.IsNullOrWhiteSpace(claveMexicana)) return (errores, advertencias);

        var feminicidios = filasDelitos
            .Where(EsFeminicidio)
            .Select(fila => CrearLlave(
                ObtenerValor(fila, "id_ci"),
                ObtenerValor(fila, "id_delito")))
            .Where(llave => !string.IsNullOrWhiteSpace(llave))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (feminicidios.Count == 0) return (errores, advertencias);

        var mexicanasSinCurp = 0;
        var mexicanasSinNombreOApellido = 0;
        var extranjerasSinNombre = 0;
        var extranjerasSinPrimerApellido = 0;

        foreach (var fila in filasVictimas)
        {
            var llave = CrearLlave(
                ObtenerValor(fila, "id_ci"),
                ObtenerValor(fila, "id_delito"));

            if (!feminicidios.Contains(llave)) continue;

            var nacionalidad = ObtenerValor(fila, "nacional")?.Trim();

            if (string.IsNullOrWhiteSpace(nacionalidad)) continue;

            var esMexicana = ClavesIguales(nacionalidad, claveMexicana);

            var nombre = ObtenerValor(fila, "nombre_vicfem");
            var primerApellido = ObtenerValor(fila, "1apellido_vicfem");
            var segundoApellido = ObtenerValor(fila, "2apellido_vicfem");
            var curp = ObtenerValor(fila, "curp_vicfem");

            if (esMexicana)
            {
                if (string.IsNullOrWhiteSpace(curp)) mexicanasSinCurp++;

                if (string.IsNullOrWhiteSpace(nombre) ||
                    string.IsNullOrWhiteSpace(primerApellido) ||
                    string.IsNullOrWhiteSpace(segundoApellido))
                {
                    mexicanasSinNombreOApellido++;
                }

                continue;
            }

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
                Mensaje = $"Se están reportando {mexicanasSinCurp} víctimas de feminicidio de nacionalidad mexicana sin CURP. Se requiere la información para finalizar la carga.",
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
                Mensaje = $"Se detectaron {extranjerasSinNombre} víctimas de feminicidio de nacionalidad distinta a mexicana sin nombre. El nombre es obligatorio.",
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
                Mensaje = $"Se detectaron {extranjerasSinPrimerApellido} víctimas de feminicidio de nacionalidad distinta a mexicana sin primer apellido. El primer apellido es obligatorio.",
                TotalRegistrosAfectados = extranjerasSinPrimerApellido
            });
        }

        if (mexicanasSinNombreOApellido > 0)
        {
            advertencias.Add(new CargaValidacionError
            {
                Archivo = "victimas",
                Fila = null,
                Columna = "nombre_vicfem+1apellido_vicfem+2apellido_vicfem",
                Campo = "nombre_vicfem+1apellido_vicfem+2apellido_vicfem",
                Valor = mexicanasSinNombreOApellido.ToString(CultureInfo.InvariantCulture),
                Codigo = "FEMINICIDIO_MEXICANA_NOMBRE_APELLIDOS_ADVERTENCIA",
                DescripcionResumen = "Víctimas de feminicidio mexicanas sin nombre y/o apellidos",
                Mensaje = $"Se detectaron {mexicanasSinNombreOApellido} víctimas de feminicidio de nacionalidad mexicana sin nombre y/o apellidos completos. Esta información es opcional. ¿Desea continuar con la carga?",
                TotalRegistrosAfectados = mexicanasSinNombreOApellido
            });
        }

        return (errores, advertencias);
    }

    private static bool EsFeminicidio(ArchivoFila fila)
    {
        return string.Equals(
            ObtenerValor(fila, "clasf_de_dto")?.Trim(),
            "1.03",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string CrearLlave(string? idCi, string? idDelito)
    {
        idCi = idCi?.Trim();
        idDelito = idDelito?.Trim();

        if (string.IsNullOrWhiteSpace(idCi) || string.IsNullOrWhiteSpace(idDelito)) return string.Empty;

        return $"{idCi}|{idDelito}";
    }

    private static bool ClavesIguales(string valor, string claveCatalogo)
    {
        if (int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valorNumero) &&
            int.TryParse(claveCatalogo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var claveNumero))
        {
            return valorNumero == claveNumero;
        }

        return string.Equals(valor.Trim(), claveCatalogo.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ObtenerValor(ArchivoFila fila, string columna)
    {
        fila.Columnas.TryGetValue(columna, out var valor);
        return valor;
    }
}