using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using SIIID2.Api.Data;
using SIIID2.Api.Services;

namespace SIIID2.Api.Filters;

public sealed class SistemaConfiguracionFilter(SistemaConfiguracionService config, IDbConnectionFactory factory) : IAsyncActionFilter
{
    private static readonly Dictionary<string, (string Modulo, string Tipo)> Flujos = new()
    {
        ["Cargas"] = ("MENSUAL", "CARGA"),
        ["Actualizaciones"] = ("MENSUAL", "ACTUALIZACION"),
        ["FederalCargas"] = ("FEDERAL", "CARGA"),
        ["FederalActualizaciones"] = ("FEDERAL", "ACTUALIZACION"),
        ["SemanalCargas"] = ("SEMANAL", "CARGA"),
        ["BanciCargas"] = ("BANCI", "CARGA"),
        ["BanciActualizaciones"] = ("BANCI", "ACTUALIZACION"),
        ["AdministracionCargas"] = ("MENSUAL", ""),
        ["FederalAdministracionCargas"] = ("FEDERAL", ""),
        ["SemanalAdministracionCargas"] = ("SEMANAL", "")
    };
    private static object? Propiedad(object? valor, string nombre) => valor?.GetType().GetProperty(nombre)?.GetValue(valor);
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor accion) { await next(); return; }
        var politica = context.HttpContext.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(p => p.Policy).FirstOrDefault(p => p?.StartsWith("MODULO_", StringComparison.Ordinal) == true);
        if (politica != null)
        {
            using var acceso = factory.CrearConexion();
            if (!await acceso.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.catalogo_modulo WHERE clave = @Modulo AND activo = 1) THEN 1 ELSE 0 END AS BIT);", new { Modulo = politica["MODULO_".Length..] }))
            {
                context.Result = new ObjectResult(new { codigo = "MODULO_DESACTIVADO", mensaje = "El módulo está desactivado. Actualice sus accesos." }) { StatusCode = 403 };
                return;
            }
        }
        if (!int.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var usuario)) { await next(); return; }
        var escritura = HttpMethods.IsPost(context.HttpContext.Request.Method) || HttpMethods.IsPut(context.HttpContext.Request.Method) || HttpMethods.IsDelete(context.HttpContext.Request.Method) || HttpMethods.IsPatch(context.HttpContext.Request.Method);
        if (escritura && accion.ControllerName.EndsWith("Usuarios", StringComparison.Ordinal) && context.ActionArguments.TryGetValue("idUsuario", out var destino) && destino is int id && await config.EsCuentaProtegidaAsync(id))
        {
            context.Result = new ObjectResult(new { mensaje = "Esta cuenta administra el sistema y no se puede modificar desde la administración ordinaria de usuarios." }) { StatusCode = 403 };
            return;
        }
        var administrativa = accion.ActionName == "Aprobar";
        if (!Flujos.TryGetValue(accion.ControllerName, out var flujo) || !escritura || !(accion.ActionName.StartsWith("Validar", StringComparison.Ordinal) || accion.ActionName.StartsWith("Confirmar", StringComparison.Ordinal) || administrativa)) { await next(); return; }
        using var bloqueo = await config.BloquearLecturaAsync();
        await config.CargarAsync();
        if (!config.Modulos.Any(m => m.Clave == flujo.Modulo && m.Activo)) { context.Result = new ObjectResult(new { mensaje = "El módulo está desactivado." }) { StatusCode = 403 }; return; }
        var confirmar =
            administrativa ||
            accion.ActionName.StartsWith(
                "Confirmar",
                StringComparison.Ordinal);

        using var db = factory.CrearConexion();

        /*
            IMPORTANTE:

            La configuración vigente únicamente determina las reglas
            utilizadas cuando una operación se VALIDA.

            Una operación que ya fue validada conserva el resultado de
            aquella validación aunque posteriormente cambie la
            configuración del sistema.

            Por lo tanto:
            - validar -> registra la versión vigente;
            - confirmar -> NO compara contra la versión vigente;
            - aprobar administrativamente -> NO compara contra la versión vigente.

            sistema_validacion_configuracion queda exclusivamente como
            información de trazabilidad/auditoría.
        */
        var resultado = await next();

        /*
            Confirmaciones y aprobaciones no generan una nueva versión
            de validación. Solamente las acciones Validar exitosas
            registran la configuración con la que fueron evaluadas.
        */
        if (
            confirmar ||
            resultado.Exception != null ||
            resultado.Result is not ObjectResult respuesta ||
            (respuesta.StatusCode ?? 200) >= 400
        )
        {
            return;
        }

        var referencia =
            Convert.ToString(
                Propiedad(
                    respuesta.Value,
                    "CodigoReferencia"))
            ?? "";

        if (string.IsNullOrWhiteSpace(referencia))
        {
            return;
        }

        await db.ExecuteAsync(
                                """
                        INSERT dbo.sistema_validacion_configuracion
                        (
                            modulo,
                            tipo,
                            referencia,
                            id_usuario,
                            version
                        )
                        SELECT
                            @Modulo,
                            @Tipo,
                            @Referencia,
                            @Usuario,
                            @Version
                        WHERE NOT EXISTS
                        (
                            SELECT 1
                            FROM dbo.sistema_validacion_configuracion
                                 WITH (UPDLOCK, HOLDLOCK)
                            WHERE modulo = @Modulo
                              AND tipo = @Tipo
                              AND referencia = @Referencia
                        );
                        """,
            new
            {
                Modulo = flujo.Modulo,
                Tipo = flujo.Tipo,
                Referencia = referencia,
                Usuario = usuario,
                Version = config.Version
            });
    }
}
