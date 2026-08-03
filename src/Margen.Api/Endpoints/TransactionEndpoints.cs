using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Contracts;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Margen.Infrastructure.Classification;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Endpoints;

public static class TransactionEndpoints
{
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/transactions").WithTags("Movimientos");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("ListTransactions")
            .Produces<Page<TransactionView>>();

        group.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("GetTransaction")
            .Produces<TransactionView>();

        // PUT y no PATCH: el cuerpo reemplaza el estado clasificable entero y
        // un nulo borra. Ver PutTransactionRequest.
        group.MapPut("/{id:guid}", ReplaceAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("PutTransaction")
            .Produces<TransactionView>();

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] MargenDbContext db,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? categoryId = null,
        string? status = null,
        int limit = 50)
    {
        if (limit is < 1 or > MaxPageSize)
        {
            return ApiResults.BadRequest(
                $"El tamaño de página va de 1 a {MaxPageSize}.");
        }

        IQueryable<Transaction> query = db.Transactions
            .AsNoTracking()
            .Include(t => t.Account)
            .Include(t => t.Category);

        // Los filtros de fecha llegan en día local y se traducen a un rango UTC
        // medio abierto. Compararlos directamente contra la columna dejaría
        // fuera las cuatro últimas horas del día pedido.
        if (from is DateOnly desde)
        {
            DateTime desdeUtc = LocalTime.StartOfLocalDay(desde);
            query = query.Where(t => t.OccurredAt >= desdeUtc);
        }

        if (to is DateOnly hasta)
        {
            DateTime hastaUtc = LocalTime.StartOfLocalDay(hasta.AddDays(1));
            query = query.Where(t => t.OccurredAt < hastaUtc);
        }

        if (categoryId is Guid categoria)
        {
            query = query.Where(t => t.CategoryId == categoria);
        }

        if (status is not null)
        {
            if (!Enum.TryParse(status, ignoreCase: true, out TxStatus estado))
            {
                return ApiResults.BadRequest($"Estado desconocido: {status}.");
            }

            query = query.Where(t => t.Status == estado);
        }

        List<Transaction> rows = await query
            .OrderByDescending(t => t.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new Page<TransactionView>(
            [.. rows.Select(BudgetAssembler.ToView)],
            NextCursor: null,
            Count: rows.Count));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        [FromServices] MargenDbContext db,
        CancellationToken cancellationToken)
    {
        Transaction? row = await db.Transactions
            .AsNoTracking()
            .Include(t => t.Account)
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return row is null
            ? ApiResults.NotFound("No existe ese movimiento.")
            : Results.Ok(BudgetAssembler.ToView(row));
    }

    /// <summary>
    /// La corrección del usuario. Es el único camino por el que una categoría
    /// cambia a mano.
    /// </summary>
    /// <remarks>
    /// Reemplaza el estado clasificable entero: lo que no venga en el cuerpo se
    /// borra. Ver <see cref="PutTransactionRequest"/> para por qué no es un
    /// parche.
    ///
    /// Con <c>CreateRule</c>, la corrección genera una regla para que la
    /// siguiente compra en el mismo comercio no vuelva a preguntar. Es lo que
    /// pide el criterio de la Fase 8 y aquí queda el camino abierto.
    /// </remarks>
    private static async Task<IResult> ReplaceAsync(
        Guid id,
        [FromBody] PutTransactionRequest request,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        [FromServices] RuleWriter rules,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Transaction? row = await db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return ApiResults.NotFound("No existe ese movimiento.");
        }

        if (request.CategoryId is Guid categoria)
        {
            bool existe = await db.Categories
                .AnyAsync(c => c.Id == categoria, cancellationToken)
                .ConfigureAwait(false);

            if (!existe)
            {
                return ApiResults.BadRequest("Esa categoría no existe.");
            }
        }

        if (request.Status is not null
            && !Enum.TryParse(request.Status, ignoreCase: true, out TxStatus _))
        {
            return ApiResults.BadRequest($"Estado desconocido: {request.Status}.");
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;

        // Reemplazo, no parche: los dos campos se aplican igual, y un nulo
        // borra en los dos. Mezclar las dos reglas haría que cambiar el estado
        // descategorizara el movimiento sin que nadie lo pidiera.
        row.CategoryId = request.CategoryId;
        row.Notes = request.Notes;
        row.UpdatedAt = now;

        // Una corrección del usuario vale más que una estadística: la confianza
        // sube al máximo y el movimiento sale de Revisión.
        row.ConfidenceBasisPoints = 10000;

        // **Aquí es donde el historial deja de ser un eco.** Marcar quién puso
        // la categoría es lo que permite que el escalón del historial cuente
        // solo lo que confirmó una persona, y no lo que dedujo la máquina y
        // luego se leyó a sí misma como confirmación.
        //
        // Descategorizar también se registra: quitar la categoría es una
        // decisión, y dejar la marca antigua diría que sigue confirmada.
        row.CategoryConfirmedAt = request.CategoryId is null ? null : now;
        row.ClassificationSource = request.CategoryId is null ? null : "Usuario";

        if (request.Status is not null)
        {
            row.Status = Enum.Parse<TxStatus>(request.Status, ignoreCase: true);
        }
        else if (row.Status == TxStatus.NeedsReview)
        {
            row.Status = TxStatus.Posted;
        }

        if (request.CreateRule && request.CategoryId is Guid destino)
        {
            // La misma pieza probada que usa el worker. Ni una segunda copia de
            // «crear o actualizar sin apilar» viviendo en un endpoint.
            await rules.LearnAsync(row.MerchantNormalized, destino, cancellationToken)
                .ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(BudgetAssembler.ToView(row));
    }

    /// <summary>
    /// Huella de un movimiento: cuenta, día local, monto y comercio.
    /// </summary>
    /// <remarks>
    /// El día es el **local**, no el UTC. Con el día UTC, dos compras idénticas
    /// hechas a las once de la noche de dos días locales distintos caerían en
    /// el mismo día UTC y la segunda se rechazaría como duplicada.
    /// </remarks>
    internal static string Fingerprint(
        Guid accountId,
        DateOnly localDay,
        Money amount,
        string merchantNormalized)
    {
        string material = string.Create(
            CultureInfo.InvariantCulture,
            $"{accountId:D}|{localDay:yyyy-MM-dd}|{amount.Cents}|{merchantNormalized}");

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
