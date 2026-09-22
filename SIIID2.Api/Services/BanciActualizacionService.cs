using System.Globalization;
using SIIID2.Api.Models;
using SIIID2.Api.Readers;
using SIIID2.Api.Repositories;
using SIIID2.Api.Validators;

namespace SIIID2.Api.Services;

public sealed class BanciActualizacionService(IBanciCargaRepository usuarios, BanciActualizacionRepository repository, BanciRenapoValidator renapo)
{
    public async Task<BanciUsuarioCargaInfo> AutorizarAsync(int idUsuario, bool modificar = false)
    {
        var usuario = await usuarios.ObtenerUsuarioCargaAsync(idUsuario);
        if (usuario == null || (!usuario.EsSuperUsuario && (usuario.Rol != "ENLACE_ESTATAL" || usuario.IdEntidadFederativa is not (>= 1 and <= 32))) || (modificar && !usuario.EsSuperUsuario && !usuario.HabilitaModificacion)) throw new UnauthorizedAccessException("No tiene permiso vigente para actualizar víctimas BANCI.");
        return usuario;
    }

    public async Task<BanciFormularioOpciones> OpcionesAsync(int idUsuario)
    {
        var usuario = await AutorizarAsync(idUsuario, true);
        return new() { EsSuperUsuario = usuario.EsSuperUsuario, IdEntidadFederativa = usuario.IdEntidadFederativa, Catalogos = await usuarios.ObtenerFormularioCatalogosAsync() };
    }

    public async Task<BanciActualizacionResultado> ValidarAsync(int idUsuario, int? entidadSolicitada, List<Dictionary<string, string?>> filas, string origen, CancellationToken cancellationToken)
    {
        var usuario = await AutorizarAsync(idUsuario, true);
        var entidad = usuario.EsSuperUsuario ? entidadSolicitada : usuario.IdEntidadFederativa;
        if (!usuario.EsSuperUsuario && entidadSolicitada.HasValue && entidadSolicitada != entidad) throw new UnauthorizedAccessException("Sólo puede actualizar víctimas de su entidad.");
        if (entidad is not (>= 1 and <= 32)) throw new ArgumentException("Seleccione una entidad federativa.");
        if (filas.Count is < 1 or > 20000 || (origen == "FORMULARIO" && filas.Count != 1)) throw new ArgumentException("Cantidad de víctimas inválida.");
        var respuesta = new BanciActualizacionResultado();
        var datos = new List<Dictionary<string, string?>>();
        for (var i = 0; i < filas.Count; i++)
        {
            var fila = filas[i] ?? throw new ArgumentException("La fila no puede ser null.");
            if (fila.Keys.Any(k => !BanciActualizacionReader.Campos.Contains(k) && k != "fila")) throw new ArgumentException("La actualización contiene campos desconocidos; use los nombres de la plantilla.");
            var normal = fila.ToDictionary(p => p.Key, p => string.IsNullOrWhiteSpace(p.Value) ? null : p.Value.Trim());
            if (!normal.ContainsKey("fila")) normal["fila"] = (i + 1).ToString(CultureInfo.InvariantCulture);
            if (!int.TryParse(normal["fila"], out var numero) || numero < 1) throw new ArgumentException("Número de fila inválido.");
            foreach (var (campo, valor) in normal)
            {
                var limite = campo switch { "no_banci" => 40, "curp" => 18, "rfc" => 13, "nomb" or "estado_migratorio" => 500, "delito" => 1000, "acciones_busqueda" or "obs" => 20000, _ => 250 };
                if (valor?.Length > limite) Error(respuesta, numero, campo, $"El campo admite hasta {limite} caracteres.");
            }
            if (normal.GetValueOrDefault("no_banci") == null && normal.GetValueOrDefault("identificador") == null)
                normal["identificador"] = normal.GetValueOrDefault("curp") ?? normal.GetValueOrDefault("folio_rnpdno");
            if (normal.GetValueOrDefault("no_banci") == null && normal.GetValueOrDefault("identificador") == null) Error(respuesta, numero, "identificador", "Indique NO_BANCI, CURP o folio RNPDNO para identificar la víctima.");
            foreach (var campo in new[] { "localizado_o_no_localizado", "con_o_sin_vida", "voluntaria_o_fue_delito" })
            {
                var valor = normal.GetValueOrDefault(campo);
                if (valor != null && valor is not ("1" or "2") && !(campo == "voluntaria_o_fue_delito" && valor == "3")) Error(respuesta, numero, campo, "Valor fuera del catálogo permitido.");
            }
            var fecha = normal.GetValueOrDefault("fecha_localizacion");
            if (fecha != null)
            {
                if (DateTime.TryParseExact(fecha, new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)) normal["fecha_localizacion"] = f.ToString("yyyy-MM-dd");
                else Error(respuesta, numero, "fecha_localizacion", "Fecha inválida. Use yyyy-MM-dd o dd/MM/yyyy.");
            }
            datos.Add(normal);
        }
        if (!respuesta.EsValido) return respuesta;
        var identificadas = (await repository.ResolverAsync(entidad.Value, datos)).ToLookup(v => v.Indice);
        var curps = new List<ArchivoFila>();
        var llaves = new HashSet<(string, string, string)>();
        for (var i = 0; i < datos.Count; i++)
        {
            var fila = datos[i];
            var numero = int.Parse(fila["fila"]!, CultureInfo.InvariantCulture);
            var candidatas = identificadas[i].ToList();
            if (candidatas.Count != 1)
            {
                Error(respuesta, numero, "identificador", candidatas.Count == 0 ? "No se encontró una víctima activa de su entidad con estos identificadores." : "Hay varias coincidencias. Seleccione la víctima y complete NO_BANCI + ID_DELITO + ID_VICF.");
                continue;
            }
            var victima = candidatas[0];
            if (!llaves.Add((victima.NoBanci, victima.IdDelito, victima.IdVicf))) Error(respuesta, numero, "id_vicf", "La misma víctima aparece más de una vez.");
            fila.Remove("identificador");
            fila["no_banci"] = victima.NoBanci;
            fila["id_delito"] = victima.IdDelito;
            fila["id_vicf"] = victima.IdVicf;
            var folio = fila.GetValueOrDefault("folio_rnpdno") ?? victima.FolioRnpdno;
            if (string.IsNullOrWhiteSpace(folio) || folio.ToUpperInvariant() is "N/D" or "ND" or "NO DISPONIBLE") Error(respuesta, numero, "folio_rnpdno", "El registro final debe contar con folio RNPDNO.");
            // Validar sólo la CURP proporcionada; la ausencia conserva el dato almacenado.
            curps.Add(new ArchivoFila { NumeroFila = numero, Columnas = fila });
        }
        if (!respuesta.EsValido) return respuesta;
        var validacion = await renapo.ValidarAsync(curps, cancellationToken);
        respuesta.Errores.AddRange(validacion.Errores);
        respuesta.Advertencias.AddRange(validacion.Advertencias);
        if (!respuesta.EsValido) return respuesta;
        return await repository.PrepararAsync(idUsuario, entidad.Value, origen, datos, respuesta.Advertencias);
    }
    private static void Error(BanciActualizacionResultado resultado, int fila, string campo, string mensaje) => resultado.Errores.Add(new() { Archivo = "actualizacion", NumeroFila = fila, Campo = campo, Codigo = "BANCI_ACTUALIZACION_INVALIDA", Mensaje = mensaje });
}
