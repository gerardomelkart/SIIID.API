using SIIID2.Api.Models;
using SIIID2.Api.Readers;

namespace SIIID2.Api.Validators;

public class BanciMetodologiaValidator
{
    public (List<BanciCargaValidacionError> Errores, List<BanciCargaValidacionError> Advertencias) Validar(BanciLecturaArchivosResultado lectura)
    {
        var errores = new List<BanciCargaValidacionError>();
        var advertencias = new List<BanciCargaValidacionError>();
        foreach (var fila in lectura.Carpetas)
        {
            if (SinDato(Valor(fila, "ntra_ci"))) errores.Add(Error(fila, "carpetas", "ntra_ci", "BANCI_NTRA_CI_OBLIGATORIO", "NTRA_CI es obligatorio."));
            if (Valor(fila, "no_banci").Length > 40) errores.Add(Error(fila, "carpetas", "no_banci", "BANCI_FOLIO_INVALIDO", "NO_BANCI supera 40 caracteres; se genera al integrar una carpeta nueva."));
        }
        foreach (var fila in lectura.Victimas)
        {
            if (SinDato(Valor(fila, "folio_rnpdno"))) errores.Add(Error(fila, "victimas", "folio_rnpdno", "BANCI_RNPDNO_OBLIGATORIO", "Folio RNPDNO es obligatorio."));
            foreach (var campo in new[] { "pob", "disc" })
                if (!SinDato(Valor(fila, campo)) && Valor(fila, campo) is not ("0" or "1")) errores.Add(Error(fila, "victimas", campo, "BANCI_CATALOGO_INVALIDO", $"{campo} sólo permite 0 o 1."));
            foreach (var campo in BanciArchivoReader.ColumnasActualizacion)
            {
                if (!string.IsNullOrWhiteSpace(Valor(fila, campo))) advertencias.Add(Error(fila, "victimas", campo, "BANCI_CAMPO_SOLO_ACTUALIZACION", $"{campo} no se integrará en la carga inicial; captúrelo en Actualización de víctimas."));
                fila.Columnas[campo] = null;
            }
            foreach (var (campo, longitud) in new[] { ("folio_rnpdno", 250), ("pro_apellido", 250), ("sdo_apellido", 250), ("nomb", 500), ("entidad_nacimiento", 250), ("estado_migratorio", 500), ("rfc", 13) })
                if (Valor(fila, campo).Length > longitud) errores.Add(Error(fila, "victimas", campo, "BANCI_LONGITUD_INVALIDA", $"{campo} permite hasta {longitud} caracteres."));
        }
        return (errores, advertencias);
    }

    private static bool SinDato(string valor) => string.IsNullOrWhiteSpace(valor) || valor.ToUpperInvariant() is "ND" or "N/D" or "NO DISPONIBLE";
    private static string Valor(ArchivoFila fila, string campo) => fila.Columnas.GetValueOrDefault(campo)?.Trim() ?? "";
    private static BanciCargaValidacionError Error(ArchivoFila fila, string archivo, string campo, string codigo, string mensaje) => new() { Archivo = archivo, NumeroFila = fila.NumeroFila, Campo = campo, Codigo = codigo, Mensaje = mensaje };
}
