using Margen.Api.Budget;
using Margen.Budget;
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
            .WithSummary("La única pregunta: cuánto puedo gastar sin afectar mis compromisos.");

        return app;
    }
}
