using Margen.Api.Budget;
using Margen.Budget;
using Margen.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Margen.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/dashboard", async (
                [FromServices] BudgetAssembler assembler,
                CancellationToken cancellationToken) =>
            {
                Outcome<DashboardView> view = await assembler
                    .BuildAsync(cancellationToken)
                    .ConfigureAwait(false);

                return ApiResults.FromOutcome(view);
            })
            .RequireAuthorization(Auth.ScopePolicies.Full)
            .WithName("GetDashboard")
            .WithTags("Panel")
            .WithSummary("La única pregunta: cuánto puedo gastar sin afectar mis compromisos.")

            // Sin esto, el contrato describe la petición y no la respuesta, y
            // el cliente no se puede generar de él. La prueba que busca cifras
            // decimales en el contrato tampoco tendría nada que mirar: los
            // montos viajan en la respuesta.
            .Produces<DashboardView>();

        return app;
    }
}
