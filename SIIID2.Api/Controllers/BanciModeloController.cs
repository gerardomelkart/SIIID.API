using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SIIID2.Api.Data;

namespace SIIID2.Api.Controllers;
[ApiController]
[Authorize(Policy = "MODULO_BANCI")]
[Route("api/banci/modelo")]
public sealed class BanciModeloController(IDbConnectionFactory factory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ObtenerAsync()
    {
        using var db = factory.CrearConexion();
        var instalado = await db.ExecuteScalarAsync<bool>("SELECT CONVERT(bit,CASE WHEN COL_LENGTH(N'dbo.banci_carga',N'mes_corte') IS NOT NULL AND OBJECT_ID(N'dbo.banci_reclasificado',N'U') IS NOT NULL THEN 1 ELSE 0 END);");
        return Ok(new { instalado, cargaDisponible = false, actualizacionDisponible = false });
    }
}
