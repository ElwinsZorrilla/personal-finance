using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Contracts;
using Margen.Budget;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Endpoints;

/// <summary>
/// Cerrar un período y proponer el del siguiente.
/// </summary>
/// <remarks>
/// La propuesta sale de **lo que se gastó de verdad**, no de lo que se asignó.
/// Un presupuesto que se copia a sí mismo mes tras mes repite el error del
/// primer mes para siempre: si en Comida se asignaron 10 000 y se gastaron
/// 14 000 tres períodos seguidos, la recomendación es 14 000.
/// </remarks>
public static class PeriodEndpoints
{
    public static IEndpointRouteBuilder MapPeriodEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/periods").WithTags("Períodos");

        group.MapGet("/recommendation", RecommendAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("GetPeriodRecommendation")
            .Produces<RecommendedBudgetView>();

        group.MapPost("/{id:guid}/close", CloseAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("ClosePeriod")
            .Produces<RecommendedBudgetView>();

        return app;
    }

    /// <summary>
    /// Qué asignar en el período que viene. **No cierra nada.**
    /// </summary>
    /// <remarks>
    /// Se puede consultar antes de cerrar, que es como se usa de verdad: se
    /// mira la propuesta, se ajusta lo que no cuadre y solo entonces se cierra.
    /// </remarks>
    private static async Task<IResult> RecommendAsync(
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken,
        long? expectedIncomeCents = null)
    {
        Money income = expectedIncomeCents is long cents
            ? new Money(cents)
            : await LastExpectedIncomeAsync(db, cancellationToken).ConfigureAwait(false);

        Outcome<RecommendedBudget> result = await BuildAsync(db, income, cancellationToken)
            .ConfigureAwait(false);

        return result.IsComputed
            ? Results.Ok(await ToViewAsync(db, result.Value, cancellationToken).ConfigureAwait(false))
            : ApiResults.Conflict(result.Reason!);
    }

    /// <summary>
    /// Cierra el período y devuelve la recomendación para el siguiente.
    /// </summary>
    /// <remarks>
    /// **Cerrar dos veces no cambia nada.** El worker, un doble toque en la
    /// pantalla o un reintento de red no pueden alterar el resultado de un
    /// período cerrado: su gasto ya alimenta la base histórica y volver a
    /// cerrarlo movería la fecha de cierre sin que haya pasado nada.
    /// </remarks>
    private static async Task<IResult> CloseAsync(
        Guid id,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        BudgetPeriod? period = await db.BudgetPeriods
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (period is null) return ApiResults.NotFound("No existe ese período.");

        if (!period.IsClosed)
        {
            period.IsClosed = true;
            period.ClosedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        Outcome<RecommendedBudget> result = await BuildAsync(
            db, period.ExpectedIncome, cancellationToken).ConfigureAwait(false);

        return result.IsComputed
            ? Results.Ok(await ToViewAsync(db, result.Value, cancellationToken).ConfigureAwait(false))
            : ApiResults.Conflict(result.Reason!);
    }

    /// <summary>
    /// Junta el gasto por categoría de cada período cerrado y pide la
    /// recomendación.
    /// </summary>
    /// <remarks>
    /// Se cuenta lo que **afecta al gasto**: no los traspasos, no los
    /// depósitos, no lo rechazado ni lo duplicado. Es la misma regla que usa el
    /// motor durante el período, y usar otra aquí haría que la recomendación no
    /// se pareciera a lo que la pantalla enseñó todo el mes.
    /// </remarks>
    private static async Task<Outcome<RecommendedBudget>> BuildAsync(
        MargenDbContext db,
        Money expectedIncome,
        CancellationToken cancellationToken)
    {
        List<BudgetPeriod> closed = await db.BudgetPeriods
            .AsNoTracking()
            .Where(p => p.IsClosed)
            .OrderByDescending(p => p.StartDate)
            .Take(HistoricalBaseline.Weights.Length)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (closed.Count == 0)
        {
            return Outcome.Insufficient<RecommendedBudget>(
                "No hay ningún período cerrado del que sacar la recomendación.");
        }

        Dictionary<Guid, Priority> priorities = await db.Categories
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Priority, cancellationToken)
            .ConfigureAwait(false);

        var history = new Dictionary<Guid, List<CategoryHistory>>();

        // Del más reciente al más viejo: es el orden que espera la media
        // ponderada, y al revés le daría el peso mayor al período más antiguo.
        foreach (BudgetPeriod period in closed)
        {
            DateTime from = LocalTime.StartOfLocalDay(period.StartDate);
            DateTime until = LocalTime.StartOfLocalDay(period.EndDate.AddDays(1));

            // Se traen las filas y se agrupa aquí. `Money` es un tipo con
            // conversión de valor, y sumar `t.Amount.Cents` dentro de un
            // `GroupBy` obliga a EF a mirar dentro de la conversión: no lo
            // traduce y revienta en tiempo de ejecución. Con un período de
            // movimientos personales son unos cientos de filas.
            var rows = await db.Transactions
                .AsNoTracking()
                .Where(t => t.OccurredAt >= from
                    && t.OccurredAt < until
                    && t.CategoryId != null
                    && t.Status != TxStatus.Rejected
                    && t.Status != TxStatus.Duplicate
                    && t.Direction == TxDirection.Outflow
                    && t.Kind != TxKind.Deposit)
                .Select(t => new { CategoryId = t.CategoryId!.Value, t.Amount })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var group in rows.GroupBy(r => r.CategoryId))
            {
                if (!history.TryGetValue(group.Key, out List<CategoryHistory>? list))
                {
                    list = [];
                    history[group.Key] = list;
                }

                list.Add(new CategoryHistory(
                    group.Key,
                    priorities.TryGetValue(group.Key, out Priority p) ? p : Priority.Flexible,
                    Money.Sum(group.Select(r => r.Amount))));
            }
        }

        return PeriodClose.Recommend(
            history.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<CategoryHistory>)pair.Value),
            expectedIncome);
    }

    private static async Task<Money> LastExpectedIncomeAsync(
        MargenDbContext db,
        CancellationToken cancellationToken)
    {
        long cents = await db.BudgetPeriods
            .AsNoTracking()
            .OrderByDescending(p => p.StartDate)
            .Select(p => p.ExpectedIncome.Cents)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Money(cents);
    }

    private static async Task<RecommendedBudgetView> ToViewAsync(
        MargenDbContext db,
        RecommendedBudget budget,
        CancellationToken cancellationToken)
    {
        Guid[] ids = [.. budget.Categories.Select(c => c.CategoryId)];

        Dictionary<Guid, string> names = await db.Categories
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken)
            .ConfigureAwait(false);

        return new RecommendedBudgetView(
            [.. budget.Categories.Select(c => new RecommendationView(
                c.CategoryId,
                names.TryGetValue(c.CategoryId, out string? name) ? name : "—",
                c.Recommended.Cents,
                c.Basis))],
            budget.Total.Cents,
            budget.Unallocated.Cents,

            // Que no quepa no es un fallo del cálculo: es que los compromisos
            // no caben en el sueldo, y taparlo recortando lo esencial sería
            // esconder el problema.
            budget.Unallocated.Cents < 0);
    }
}
