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

public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/budget").WithTags("Presupuesto");

        group.MapGet("/", GetAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("GetBudget")
            .Produces<BudgetView>();

        group.MapPost("/redistribute", RedistributeAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("RedistributeBudget")
            .Produces<BudgetView>();

        return app;
    }

    private static async Task<IResult> GetAsync(
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        (Domain.Entities.BudgetPeriod? period, BudgetCycle? cycle, IResult? error) =
            await ResolveCurrentAsync(db, clock, cancellationToken).ConfigureAwait(false);

        if (error is not null)
        {
            return error;
        }

        List<CategoryBudget> budgets = await db.CategoryBudgets
            .AsNoTracking()
            .Include(b => b.Category)
            .Where(b => b.BudgetPeriodId == period!.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        SpendingSummary spending = await SummarizeAsync(db, cycle!, cancellationToken)
            .ConfigureAwait(false) is { IsComputed: true } outcome
            ? outcome.Value
            : new SpendingSummary(Money.Zero, new Dictionary<Guid, Money>(), Money.Zero);

        List<CategoryAllocation> allocations = [.. budgets.Select(b => new CategoryAllocation(
            b.CategoryId,
            b.Category?.Priority ?? Priority.Flexible,
            b.Allocated + b.Adjustment,
            spending.ByCategory.TryGetValue(b.CategoryId, out Money spent) ? spent : Money.Zero))];

        var lines = budgets
            .Zip(allocations, (b, a) => new BudgetLineView(
                b.CategoryId,
                b.Category?.Name ?? "Sin nombre",
                a.Priority.ToString(),
                b.Allocated.Cents,
                b.Adjustment.Cents,
                a.Allocated.Cents,
                a.Spent.Cents,
                a.Available.Cents,
                a.CanBeTrimmed))
            .OrderBy(l => l.Priority)
            .ThenBy(l => l.CategoryName, StringComparer.Ordinal)
            .ToList();

        return Results.Ok(new BudgetView(
            period!.Id,
            period.StartDate,
            period.EndDate,
            period.IsClosed,
            Money.Sum(allocations.Select(a => a.Allocated)).Cents,
            Money.Sum(allocations.Select(a => a.Spent)).Cents,
            Redistribution.TrimmableTotal(allocations).Cents,
            lines));
    }

    /// <summary>
    /// Mueve presupuesto de una categoría a otra.
    /// </summary>
    /// <remarks>
    /// La decisión la toma el motor, no este endpoint: aquí solo se cargan las
    /// dos categorías, se le pasan a <see cref="Redistribution.Move"/> y se
    /// guarda lo que devuelva. Duplicar aquí la regla de las prioridades
    /// intocables sería tener dos sitios donde el alquiler se puede recortar, y
    /// uno de los dos acabaría desactualizado.
    ///
    /// El ajuste se guarda en <c>Adjustment</c> y no restando de
    /// <c>Allocated</c>: así queda el rastro de qué se movió y por qué la
    /// categoría tiene hoy menos de lo que se le asignó al abrir el período.
    /// </remarks>
    private static async Task<IResult> RedistributeAsync(
        [FromBody] RedistributeRequest request,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        (Domain.Entities.BudgetPeriod? period, BudgetCycle? cycle, IResult? error) =
            await ResolveCurrentAsync(db, clock, cancellationToken).ConfigureAwait(false);

        if (error is not null)
        {
            return error;
        }

        if (request.FromCategoryId == request.ToCategoryId)
        {
            return ApiResults.BadRequest("El origen y el destino son la misma categoría.");
        }

        List<CategoryBudget> rows = await db.CategoryBudgets
            .Include(b => b.Category)
            .Where(b => b.BudgetPeriodId == period!.Id
                && (b.CategoryId == request.FromCategoryId
                    || b.CategoryId == request.ToCategoryId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        CategoryBudget? source = rows.Find(b => b.CategoryId == request.FromCategoryId);
        CategoryBudget? target = rows.Find(b => b.CategoryId == request.ToCategoryId);

        if (source is null || target is null)
        {
            return ApiResults.NotFound(
                "Alguna de las dos categorías no tiene presupuesto en este período.");
        }

        Outcome<SpendingSummary> spending = await SummarizeAsync(db, cycle!, cancellationToken)
            .ConfigureAwait(false);

        if (!spending.IsComputed)
        {
            return ApiResults.FromOutcome(spending);
        }

        Money Spent(Guid categoryId) =>
            spending.Value.ByCategory.TryGetValue(categoryId, out Money value)
                ? value
                : Money.Zero;

        var from = new CategoryAllocation(
            source.CategoryId,
            source.Category?.Priority ?? Priority.Flexible,
            source.Allocated + source.Adjustment,
            Spent(source.CategoryId));

        var to = new CategoryAllocation(
            target.CategoryId,
            target.Category?.Priority ?? Priority.Flexible,
            target.Allocated + target.Adjustment,
            Spent(target.CategoryId));

        RedistributionResult result = Redistribution.Move(
            from, to, new Money(request.AmountCents));

        if (!result.Succeeded)
        {
            return result.Refusal switch
            {
                RedistributionRefusal.ProtectedPriority => ApiResults.Problem(
                    StatusCodes.Status409Conflict,
                    "Esa categoría es indispensable o importante: no se recorta."),
                RedistributionRefusal.NotEnoughAvailable => ApiResults.Problem(
                    StatusCodes.Status409Conflict,
                    "No queda tanto sin gastar en la categoría de origen."),
                RedistributionRefusal.NonPositiveAmount => ApiResults.BadRequest(
                    "El monto a mover tiene que ser mayor que cero."),
                _ => ApiResults.BadRequest("El origen y el destino son la misma categoría."),
            };
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        var amount = new Money(request.AmountCents);

        source.Adjustment -= amount;
        source.UpdatedAt = now;
        target.Adjustment += amount;
        target.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await GetAsync(db, clock, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Outcome<SpendingSummary>> SummarizeAsync(
        MargenDbContext db,
        BudgetCycle cycle,
        CancellationToken cancellationToken)
    {
        (DateTime from, DateTime untilExclusive) = LocalTime.UtcRangeOf(cycle);

        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => t.OccurredAt >= from.AddDays(-120) && t.OccurredAt < untilExclusive)
            .Select(t => new
            {
                t.Id,
                t.CategoryId,
                t.Amount,
                t.Kind,
                t.Status,
                t.OccurredAt,
                t.RefundsTransactionId,
                t.Direction,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return SpendingLedger.Summarize(cycle, [.. rows.Select(r => new LedgerEntry(
            r.Id,
            r.CategoryId,
            r.Amount,
            r.Kind,
            r.Status,
            LocalTime.LocalDateOf(r.OccurredAt),
            r.RefundsTransactionId,
            r.Direction))]);
    }

    internal static async Task<(Domain.Entities.BudgetPeriod? Period, BudgetCycle? Cycle, IResult? Error)>
        ResolveCurrentAsync(
            MargenDbContext db,
            TimeProvider clock,
            CancellationToken cancellationToken)
    {
        DateOnly today = LocalTime.LocalDateOf(clock.GetUtcNow().UtcDateTime);

        Domain.Entities.BudgetPeriod? period = await db.BudgetPeriods
            .AsNoTracking()
            .Where(p => !p.IsClosed && p.StartDate <= today && p.EndDate >= today)
            .OrderByDescending(p => p.StartDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (period is null)
        {
            return (null, null, ApiResults.Conflict(
                "No hay un período presupuestario abierto que contenga el día de hoy."));
        }

        Outcome<BudgetCycle> cycle = BudgetCycle.Inclusive(period.StartDate, period.EndDate);

        return cycle.IsComputed
            ? (period, cycle.Value, null)
            : (null, null, ApiResults.Problem(
                StatusCodes.Status500InternalServerError, cycle.Reason!));
    }
}
