using Margen.Api.Auth;

namespace Margen.Api.Endpoints;

/// <summary>
/// La ruta del registro rápido de efectivo.
/// </summary>
/// <remarks>
/// Existe desde esta fase con su autorización puesta y sin implementación, y
/// eso es a propósito. El criterio de la Fase 2 es que el token del Atajo tenga
/// alcance restringido, y «restringido» solo se puede comprobar contra una ruta
/// que ese token sí alcance: sin ella, la prueba diría que el token no puede
/// hacer nada, que es cierto por accidente.
///
/// La Fase 9 la implementa —interpretar «Gasté 450 pesos en almuerzo»— y le
/// pone el límite de peticiones. Hasta entonces devuelve 501, que es la verdad:
/// la ruta existe, la autorización funciona, el trabajo no está hecho.
/// </remarks>
public static class CashEndpoints
{
    public static IEndpointRouteBuilder MapCashEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/transactions/cash", () => Results.Problem(
                detail: "El registro rápido de efectivo llega en la Fase 9.",
                statusCode: StatusCodes.Status501NotImplemented))
            .RequireAuthorization(ScopePolicies.CashCreate)
            .WithTags("Efectivo");

        return app;
    }
}
