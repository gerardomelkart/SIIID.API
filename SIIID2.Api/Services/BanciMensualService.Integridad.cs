using System.Data;
using System.Globalization;
using Dapper;
using SIIID2.Api.Models;

namespace SIIID2.Api.Services;
public sealed partial class BanciMensualService
{
    // Aun en históricos sin validación de contenido, la información debe poder persistirse sin truncar ni romper FKs.
    private async Task<List<CargaValidacionError>> ValidarPersistenciaAsync(BanciLibro libro)
    {
        var errores = new List<CargaValidacionError>();
        using var db = factory.CrearConexion();
        foreach (var(tabla, filas, columnas)in new[]
        {
            ("banci_carpeta_investigacion", libro.Carpetas, new[] { "id_ci", "ntra_ci", "fha_de_ini", "hra_de_ini", "rmen_de_hchos" }),
            ("banci_delito", libro.Delitos, ColumnasDelito),
            ("banci_victima", libro.Victimas, ColumnasVictima)
        }

        )
        {
            var metadatos = (await db.QueryAsync<ColumnaPersistencia>("SELECT c.name AS Nombre,t.name AS Tipo,c.max_length AS Longitud,c.is_nullable AS Opcional FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(@tabla);", new { tabla = "dbo." + tabla })).Where(c => columnas.Contains(c.Nombre)).ToArray();
            var permitidas = columnas.Select(c => c == "primer_apellido_vicfem" ? "1apellido_vicfem" : c == "segundo_apellido_vicfem" ? "2apellido_vicfem" : c).Concat(tabla == "banci_carpeta_investigacion" ? (libro.Actualizacion ? new[] { "no_banci" } : Array.Empty<string>()) : tabla == "banci_delito" ? new[] { "id_ci" } : new[]
            {
                "id_ci",
                "id_delito"
            }).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var archivo = tabla == "banci_carpeta_investigacion" ? "carpetas" : tabla == "banci_delito" ? "delitos" : "victimas";
            foreach (var fila in filas)
            {
                foreach (var campo in fila.Columnas.Keys.Where(c => !permitidas.Contains(c)))
                    errores.Add(Error(archivo, fila, campo, "La columna no pertenece a esta plantilla."));
                foreach (var columna in metadatos)
                {
                    var campo = columna.Nombre == "primer_apellido_vicfem" ? "1apellido_vicfem" : columna.Nombre == "segundo_apellido_vicfem" ? "2apellido_vicfem" : columna.Nombre;
                    var valor = Valor(fila, campo);
                    if(valor == "")
                    {
                        if(!columna.Opcional)
                            errores.Add(Error(archivo, fila, campo, "El dato es necesario para conservar la integridad del registro."));
                        continue;
                    }

                    var valido = true;
                    if(columna.Tipo is "nvarchar" or "varchar")
                        valido = columna.Longitud == -1 || valor.Length <= (columna.Tipo == "nvarchar" ? columna.Longitud / 2 : columna.Longitud);
                    if(columna.Tipo == "tinyint")
                        valido = byte.TryParse(valor, out _);
                    if(columna.Tipo == "int")
                        valido = int.TryParse(valor, out _);
                    if(columna.Tipo == "decimal")
                        valido = decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var coordenada) && Math.Abs(coordenada) < 100000000m && decimal.Round(coordenada, 10) == coordenada;
                    if(columna.Tipo == "date")
                    {
                        try
                        {
                            Fecha(fila, campo);
                        }
                        catch (InvalidOperationException)
                        {
                            valido = false;
                        }
                    }

                    if(columna.Tipo == "time")
                    {
                        try
                        {
                            Hora(fila, campo);
                        }
                        catch (InvalidOperationException)
                        {
                            valido = false;
                        }
                    }

                    if(!valido)
                        errores.Add(Error(archivo, fila, campo, "El valor no es interpretable o excede el tamaño admitido por el campo."));
                }
            }

            // Resuelve las FKs reales del esquema para no duplicar catálogos o claves en el código.
            var referencias = (await db.QueryAsync<ReferenciaPersistencia>("""
                SELECT f.object_id AS Id,s.name AS Esquema,t.name AS Tabla,pc.name AS Campo,rc.name AS Referencia
                FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
                JOIN sys.tables t ON t.object_id=f.referenced_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
                JOIN sys.columns pc ON pc.object_id=f.parent_object_id AND pc.column_id=fc.parent_column_id
                JOIN sys.columns rc ON rc.object_id=f.referenced_object_id AND rc.column_id=fc.referenced_column_id
                WHERE f.parent_object_id=OBJECT_ID(@tabla) ORDER BY f.object_id,fc.constraint_column_id;
                """, new { tabla = "dbo." + tabla })).GroupBy(r => r.Id);
            foreach (var fk in referencias)
            {
                var partes = fk.ToArray();
                if(partes.Any(p => !columnas.Contains(p.Campo)))
                    continue;
                static string Citar(string nombre) => "[" + nombre.Replace("]", "]]") + "]";
                var referencia = partes[0];
                var sql = $"SELECT {string.Join(',', partes.Select(p => Citar(p.Referencia)))} FROM {Citar(referencia.Esquema)}.{Citar(referencia.Tabla)};";
                var claves = (await db.QueryAsync(sql)).Select(f => Serializar(((IDictionary<string, object>)f).Values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""))).ToHashSet();
                foreach (var fila in filas)
                {
                    var valores = partes.Select(p => Valor(fila, p.Campo)).ToArray();
                    if(valores.Any(v => v == ""))
                        continue;
                    if(!claves.Contains(Serializar(valores)))
                        errores.Add(Error(archivo, fila, string.Join('+', partes.Select(p => p.Campo)), "La clave no existe en el catálogo de la base de datos."));
                }
            }
        }

        return errores;
    }

    private sealed class ColumnaPersistencia
    {
        public string Nombre { get; set; } = "";
        public string Tipo { get; set; } = "";
        public short Longitud { get; set; }
        public bool Opcional { get; set; }
    }

    private sealed class ReferenciaPersistencia
    {
        public int Id { get; set; }
        public string Esquema { get; set; } = "";
        public string Tabla { get; set; } = "";
        public string Campo { get; set; } = "";
        public string Referencia { get; set; } = "";
    }
}
