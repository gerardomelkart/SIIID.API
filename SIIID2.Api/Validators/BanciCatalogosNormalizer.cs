using System.Globalization;
using SIIID2.Api.Models;

namespace SIIID2.Api.Validators;
/// <summary>Resuelve ceros y omisiones con la opción No identificado del catálogo activo de cada campo.</summary>
public static class BanciCatalogosNormalizer
{
    private static readonly string[] CamposVictima = ["sexo", "genero", "pob", "disc", "nacional"];
    private static readonly string[] CamposDelito = ["forma_acc", "emto_com_dto", "grdo_cons"];
    public static void Preparar(BanciLecturaArchivosResultado lectura, IReadOnlyList<BanciFormularioOpcion> catalogos)
    {
        foreach (var(archivo, filas, campos)in new[]
        {
            ("victimas", lectura.Victimas, CamposVictima),
            ("delitos", lectura.Delitos, CamposDelito)
        }

        )
        {
            foreach (var campo in campos)
            {
                var sinDato = filas.Where(f => EsSinDato(f.Columnas.GetValueOrDefault(campo))).ToList();
                if(sinDato.Count == 0)
                    continue;
                var claves = catalogos.Where(c => c.Campo == campo && c.CodigoRegla == "NO_IDENTIFICADO").Select(c => c.Clave).Distinct().ToList();
                if(claves.Count != 1)
                {
                    foreach (var fila in sinDato)
                        lectura.Errores.Add(new BanciCargaValidacionError
                        {
                            Archivo = archivo,
                            NumeroFila = fila.NumeroFila,
                            Campo = campo,
                            Codigo = "BANCI_CATALOGO_SIN_NO_IDENTIFICADO",
                            Mensaje = $"El catálogo activo de {campo} debe contener una única opción No identificado."
                        });
                    continue;
                }

                foreach (var fila in sinDato)
                    fila.Columnas[campo] = claves[0];
            }
        }
    }

    public static BanciCruceArchivos NormalizarCruce(BanciCruceArchivos archivos, IReadOnlyList<BanciFormularioOpcion> catalogos)
    {
        var copia = new BanciLecturaArchivosResultado
        {
            Delitos = archivos.Delitos.Select(Copiar).ToList(),
            Victimas = archivos.Victimas.Select(Copiar).ToList()
        };
        Preparar(copia, catalogos);
        if(copia.Errores.Count > 0)
            throw new InvalidOperationException(copia.Errores[0].Mensaje);
        return new(archivos.Carpetas, copia.Delitos, copia.Victimas);
    }

    private static ArchivoFila Copiar(ArchivoFila fila) => new()
    {
        NumeroFila = fila.NumeroFila,
        Columnas = new Dictionary<string, string?>(fila.Columnas, StringComparer.OrdinalIgnoreCase)
    };
    private static bool EsSinDato(string? valor) => string.IsNullOrWhiteSpace(valor) || (int.TryParse(valor.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var clave) && clave == 0);
}
