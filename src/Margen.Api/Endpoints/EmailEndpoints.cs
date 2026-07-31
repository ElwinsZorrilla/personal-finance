using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Contracts;
using Margen.Domain;
using Margen.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Endpoints;

/// <summary>
/// Los correos entrantes, para poder ver qué llegó y qué no se pudo interpretar.
/// </summary>
/// <remarks>
/// Solo lectura en esta fase. Quien **escribe** aquí es el worker de la Fase 6,
/// y el reproceso contra una versión nueva del parser es de esa misma fase.
///
/// El cuerpo del correo no sale por este endpoint. Es la notificación de un
/// banco: lleva los últimos cuatro dígitos de la tarjeta, el comercio y el
/// monto. No hay motivo para que viaje a la app cuando lo que la pantalla de
/// Revisión necesita es saber que llegó y que no se pudo leer.
/// </remarks>
public static class EmailEndpoints
{
    public static IEndpointRouteBuilder MapEmailEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/emails").WithTags("Correos entrantes");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("ListIncomingEmails");

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] MargenDbContext db,
        CancellationToken cancellationToken,
        string? status = null,
        int limit = 50)
    {
        if (limit is < 1 or > 200)
        {
            return ApiResults.BadRequest("El tamaño de página va de 1 a 200.");
        }

        IQueryable<Domain.Entities.IncomingEmail> query = db.IncomingEmails.AsNoTracking();

        if (status is not null)
        {
            if (!Enum.TryParse(status, ignoreCase: true, out EmailStatus estado))
            {
                return ApiResults.BadRequest($"Estado desconocido: {status}.");
            }

            query = query.Where(e => e.Status == estado);
        }

        List<IncomingEmailView> rows = await query
            .OrderByDescending(e => e.ReceivedAt)
            .Take(limit)
            .Select(e => new IncomingEmailView(
                e.Id,
                e.Sender,
                e.Subject,
                e.ReceivedAt,
                e.ProcessedAt,
                e.Status.ToString(),
                e.ParserName,
                e.ParserVersion,
                e.FailureReason,
                e.Transactions.Count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new Page<IncomingEmailView>(rows, null, rows.Count));
    }
}

/// <summary>
/// Estado de conciliación de los movimientos del período.
/// </summary>
/// <remarks>
/// Solo lectura en esta fase. La importación de CSV con mapeo de columnas y el
/// cierre de período son la Fase 10; lo que existe aquí es la vista que esa
/// fase va a llenar, para que la app pueda consumirla desde la Fase 5 sin
/// esperar.
/// </remarks>
public static class ReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/reconciliation", GetAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("GetReconciliation")
            .WithTags("Conciliación");

        return app;
    }

    private static async Task<IResult> GetAsync(
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        (_, Margen.Budget.BudgetCycle? cycle, IResult? error) =
            await BudgetEndpoints.ResolveCurrentAsync(db, clock, cancellationToken)
                .ConfigureAwait(false);

        if (error is not null)
        {
            return error;
        }

        (DateTime from, DateTime untilExclusive) = LocalTime.UtcRangeOf(cycle!);

        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => t.OccurredAt >= from && t.OccurredAt < untilExclusive)
            .Select(t => new
            {
                t.Id,
                t.MerchantRaw,
                t.Amount,
                t.OccurredAt,
                t.Status,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int Count(TxStatus status) => rows.Count(r => r.Status == status);

        // Lo pendiente de cuadrar: todo lo que no está conciliado ni descartado.
        var outstanding = rows
            .Where(r => r.Status is TxStatus.Pending or TxStatus.NeedsReview or TxStatus.Duplicate)
            .OrderByDescending(r => r.OccurredAt)
            .Select(r => new ReconciliationLineView(
                r.Id,
                r.MerchantRaw,
                r.Amount.Cents,
                LocalTime.LocalDateOf(r.OccurredAt),
                r.Status.ToString()))
            .ToList();

        return Results.Ok(new ReconciliationSummaryView(
            Count(TxStatus.Reconciled),
            Count(TxStatus.Pending),
            Count(TxStatus.NeedsReview),
            Count(TxStatus.Duplicate),
            Count(TxStatus.Rejected),
            outstanding));
    }
}
