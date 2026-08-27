using Dapper;
using SIIID2.Api.Data;
using SIIID2.Api.Models;

namespace SIIID2.Api.Services;

public static class CodigoPostalActualizacionHelper
{
    private sealed class DiferenciaCpRow
    {
        public string IdentificadorFiscalia { get; set; } = string.Empty;
        public string? ValorAnterior { get; set; }
        public string? ValorNuevo { get; set; }
    }

    public static async Task AplicarDetalleAsync(IDbConnectionFactory dbConnectionFactory, string codigoReferencia, ActualizacionDiferenciasResponse response, int limitePorSeccion, bool soloMuestra = false)
    {
        if (!response.EsValido || string.IsNullOrWhiteSpace(codigoReferencia)) return;

        var diferencias = await ObtenerDiferenciasAsync(dbConnectionFactory, codigoReferencia, soloMuestra ? limitePorSeccion : int.MaxValue);
        var agregados = 0;

        foreach (var diferencia in diferencias)
        {
            var existente = response.Delitos.FirstOrDefault(x => x.IdentificadorFiscalia == diferencia.IdentificadorFiscalia);

            if (existente != null)
            {
                if (!existente.CamposModificados.Any(x => x.Campo == "cp"))
                {
                    existente.CamposModificados.Add(new ActualizacionCampoDiferencia { Campo = "cp", ValorAnterior = diferencia.ValorAnterior, ValorNuevo = diferencia.ValorNuevo });
                }

                continue;
            }

            agregados++;

            if (response.Delitos.Count < limitePorSeccion)
            {
                response.Delitos.Add(new ActualizacionDiferenciaRegistro
                {
                    TipoMovimiento = "MODIFICADO",
                    CampoIdentificador = "id_ci + id_delito",
                    IdentificadorFiscalia = diferencia.IdentificadorFiscalia,
                    CamposModificados = new List<ActualizacionCampoDiferencia>
                    {
                        new() { Campo = "cp", ValorAnterior = diferencia.ValorAnterior, ValorNuevo = diferencia.ValorNuevo }
                    }
                });
            }
        }

        response.TotalDelitos += agregados;
        response.TotalDiferencias += agregados;
        if (response.TotalDelitos > response.Delitos.Count) response.DetalleLimitado = true;
    }

    private static async Task<List<DiferenciaCpRow>> ObtenerDiferenciasAsync(IDbConnectionFactory dbConnectionFactory, string codigoReferencia, int limiteFilas)
    {
        const string sql = @"
            ;WITH ctx AS (
                SELECT TOP 1 id_carga, id_entidad_federativa, mes_corte, anio_corte
                FROM dbo.carga
                WHERE codigo_referencia = @CodigoReferencia
                  AND tipo_carga = N'ACTUALIZACION'
                  AND estado IN (N'VALIDADO_PENDIENTE_ACTUALIZACION', N'PENDIENTE_APROBACION')
                  AND activo = 1
            ),
            actuales_base AS (
                SELECT
                    ci.identificador_carpeta_fiscalia AS id_ci,
                    d.identificador_delito_fiscalia AS id_delito,
                    COALESCE(NULLIF(LTRIM(RTRIM(d.codigo_postal_fiscalia)), N''), NULLIF(LTRIM(RTRIM(cp.codigo_postal)), N'')) AS cp_actual,
                    ROW_NUMBER() OVER (
                        PARTITION BY ci.identificador_carpeta_fiscalia, d.identificador_delito_fiscalia
                        ORDER BY ISNULL(c.fecha_confirmacion, '19000101') DESC, d.id_carga DESC, d.id_delito DESC
                    ) AS rn
                FROM dbo.delito d
                INNER JOIN dbo.carpeta_investigacion ci ON ci.id_carpeta_investigacion = d.id_carpeta_investigacion AND ci.activo = 1
                INNER JOIN dbo.carga c ON c.id_carga = d.id_carga
                INNER JOIN ctx ON ctx.id_entidad_federativa = c.id_entidad_federativa AND ctx.mes_corte = c.mes_corte AND ctx.anio_corte = c.anio_corte
                LEFT JOIN dbo.catalogo_codigo_postal cp ON cp.id_codigo_postal = d.id_codigo_postal
                WHERE c.estado IN (N'CONFIRMADO', N'CONFIRMADO_ACTUALIZACION') AND c.activo = 1 AND d.activo = 1
            )
            SELECT TOP (@LimiteFilas)
                CONCAT(tmp.id_ci, N' | ', tmp.id_delito) AS IdentificadorFiscalia,
                a.cp_actual AS ValorAnterior,
                NULLIF(LTRIM(RTRIM(tmp.cp)), N'') AS ValorNuevo
            FROM dbo.carga_tmp_delito tmp
            INNER JOIN ctx ON ctx.id_carga = tmp.id_carga
            INNER JOIN actuales_base a ON a.id_ci = tmp.id_ci AND a.id_delito = tmp.id_delito AND a.rn = 1
            WHERE tmp.activo = 1
              AND ISNULL(a.cp_actual, N'') <> ISNULL(NULLIF(LTRIM(RTRIM(tmp.cp)), N''), N'');";

        using var connection = dbConnectionFactory.CrearConexion();
        var filas = await connection.QueryAsync<DiferenciaCpRow>(sql, new { CodigoReferencia = codigoReferencia, LimiteFilas = limiteFilas }, commandTimeout: 180);
        return filas.ToList();
    }
}
