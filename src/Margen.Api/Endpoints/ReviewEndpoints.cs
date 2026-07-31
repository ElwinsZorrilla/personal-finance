using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Contracts;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Endpoints;

/// <summary>
/// Lo que pide atención: movimientos sin clasificar y alertas sin resolver.
/// </summary>
public static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/review").WithTags("Revisión");

        group.MapGet("/", GetAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("GetReviewQueue");

        return app;
    }

    private static async Task<IResult> GetAsync(
        [FromServices] MargenDbContext db,
        CancellationToken cancellationToken)
    {
        List<Transaction> pending = await db.Transactions
            .AsNoTracking()
            .Include(t => t.Account)
            .Include(t => t.Category)
            .Where(t => t.Status == TxStatus.NeedsReview || t.Status == TxStatus.Duplicate)
            .OrderByDescending(t => t.OccurredAt)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        List<AlertView> alerts = await db.Alerts
            .AsNoTracking()
            .Where(a => a.ResolvedAt == null)
            .OrderByDescending(a => a.IsUrgent)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => new AlertView(
                a.Id, a.Kind.ToString(), a.Title, a.Detail, a.IsUrgent, a.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new
        {
            Transactions = pending.Select(BudgetAssembler.ToView).ToList(),
            Alerts = alerts,
        });
    }
}

/// <summary>Alertas: la bandeja de avisos y su resolución.</summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/notifications").WithTags("Avisos");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("ListNotifications");

        group.MapPost("/{id:guid}/resolve", ResolveAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("ResolveNotification");

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] MargenDbContext db,
        CancellationToken cancellationToken,
        bool includeResolved = false)
    {
        List<AlertView> alerts = await db.Alerts
            .AsNoTracking()
            .Where(a => includeResolved || a.ResolvedAt == null)
            .OrderByDescending(a => a.IsUrgent)
            .ThenByDescending(a => a.CreatedAt)
            .Take(200)
            .Select(a => new AlertView(
                a.Id, a.Kind.ToString(), a.Title, a.Detail, a.IsUrgent, a.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new Page<AlertView>(alerts, null, alerts.Count));
    }

    /// <summary>
    /// Marca un aviso como resuelto.
    /// </summary>
    /// <remarks>
    /// Idempotente: resolver dos veces no es un error y no mueve la fecha. El
    /// índice único de la Fase 2 es sobre las alertas **sin resolver**, así que
    /// resolver una libera la clave y el mismo problema puede volver a avisar
    /// el mes siguiente.
    /// </remarks>
    private static async Task<IResult> ResolveAsync(
        Guid id,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;

        int affected = await db.Alerts
            .Where(a => a.Id == id && a.ResolvedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(a => a.ResolvedAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        if (affected == 0)
        {
            bool exists = await db.Alerts
                .AnyAsync(a => a.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                return ApiResults.NotFound("No existe ese aviso.");
            }
        }

        return Results.NoContent();
    }
}
