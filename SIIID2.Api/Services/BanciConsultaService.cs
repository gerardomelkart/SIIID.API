using SIIID2.Api.Models;
using SIIID2.Api.Repositories;
using ClosedXML.Excel;
using System.Data;
using System.Globalization;

namespace SIIID2.Api.Services;

public class BanciConsultaService : IBanciConsultaService
{
    private readonly IBanciConsultaRepository _consultaRepository;
    private readonly IBanciCargaRepository _cargaRepository;

    public BanciConsultaService(IBanciConsultaRepository consultaRepository, IBanciCargaRepository cargaRepository)
    {
        _consultaRepository = consultaRepository;
        _cargaRepository = cargaRepository;
    }

    public async Task<(byte[] Archivo, string NombreArchivo)> DescargarExcelAsync(int idUsuario, BanciConsultaFiltro filtro)
    {
        var alcance = await ObtenerAlcanceAsync(idUsuario);
        if (alcance.HasValue && filtro.IdEntidadFederativa.HasValue && filtro.IdEntidadFederativa != alcance)
            throw new UnauthorizedAccessException();

        using var datos = await _consultaRepository.ObtenerExcelAsync(filtro, alcance);
        using var libro = new XLWorkbook();
        foreach (DataTable tabla in datos.Tables)
        {
            if (tabla.Rows.Count > 1048575)
                throw new ArgumentException("El resultado supera las filas permitidas por Excel. Seleccione un mes o una entidad para descargarlo completo.");
            var hoja = libro.Worksheets.Add(tabla.TableName);
            for (var columna = 0; columna < tabla.Columns.Count; columna++)
            {
                hoja.Cell(1, columna + 1).Value = tabla.Columns[columna].ColumnName;
                hoja.Column(columna + 1).Width = 22;
            }
            // Texto explícito: preserva ceros iniciales, identificadores y códigos largos.
            // No se asigna FormulaA1: los valores del usuario nunca son fórmulas.
            hoja.Range(1, 1, tabla.Rows.Count + 1, tabla.Columns.Count).Style.NumberFormat.Format = "@";
            for (var fila = 0; fila < tabla.Rows.Count; fila++)
                for (var columna = 0; columna < tabla.Columns.Count; columna++)
                {
                    var valor = tabla.Rows[fila][columna];
                    var texto = valor is DBNull ? string.Empty : Convert.ToString(valor, CultureInfo.InvariantCulture) ?? string.Empty;
                    if (texto.Length > 32767)
                        throw new ArgumentException($"El campo {tabla.Columns[columna].ColumnName} de la hoja {tabla.TableName} supera el tamaño permitido por una celda Excel. No se generó un archivo incompleto.");
                    hoja.Cell(fila + 2, columna + 1).Value = texto;
                }
            var encabezado = hoja.Range(1, 1, 1, tabla.Columns.Count);
            encabezado.Style.Font.Bold = true;
            encabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#6f2c91");
            encabezado.Style.Font.FontColor = XLColor.White;
            hoja.RangeUsed()!.SetAutoFilter();
            hoja.SheetView.FreezeRows(1);
        }
        using var archivo = new MemoryStream();
        libro.SaveAs(archivo);
        var periodo = filtro.Mes.HasValue ? $"{filtro.Anio}_{filtro.Mes.Value:00}" : $"{filtro.Anio}";
        var entidad = alcance ?? filtro.IdEntidadFederativa;
        var sufijo = entidad.HasValue ? $"ENTIDAD_{entidad.Value:00}" : "NACIONAL";
        return (archivo.ToArray(), $"BANCI_{periodo}_{sufijo}.xlsx");
    }

    private async Task<int?> ObtenerAlcanceAsync(int idUsuario)
    {
        // Verifica usuario/rol activos y acceso propio a BANCI; consulta no requiere permiso de carga.
        var usuario = await _cargaRepository.ObtenerUsuarioCargaAsync(idUsuario);
        if (usuario == null) throw new UnauthorizedAccessException();

        if (usuario.EsSuperUsuario) return null;
        if (usuario.Rol == "CONSULTA" && !usuario.IdEntidadFederativa.HasValue)
            return null; // Mantiene la regla de consulta nacional existente en SIIID2.

        if (usuario.Rol is not ("ENLACE_ESTATAL" or "CONSULTA") ||
            usuario.IdEntidadFederativa is not (>= 1 and <= 32))
            throw new UnauthorizedAccessException();

        return usuario.IdEntidadFederativa.Value;
    }

    public async Task<BanciConsultaOpciones> ObtenerOpcionesAsync(int idUsuario)
    {
        var alcance = await ObtenerAlcanceAsync(idUsuario);
        return await _consultaRepository.ObtenerOpcionesAsync(alcance);
    }

    public async Task<BanciConsultaResultado> ConsultarAsync(int idUsuario, BanciConsultaFiltro filtro)
    {
        var alcance = await ObtenerAlcanceAsync(idUsuario);
        if (alcance.HasValue && filtro.IdEntidadFederativa.HasValue && filtro.IdEntidadFederativa != alcance)
            throw new UnauthorizedAccessException();

        // El alcance autenticado es independiente del filtro que envía el navegador.
        return await _consultaRepository.ConsultarAsync(filtro, alcance);
    }

    public async Task<BanciConsultaDetalle?> ObtenerDetalleAsync(int idUsuario, long idCarpeta)
    {
        var alcance = await ObtenerAlcanceAsync(idUsuario);
        return await _consultaRepository.ObtenerDetalleAsync(idCarpeta, alcance);
    }
}
