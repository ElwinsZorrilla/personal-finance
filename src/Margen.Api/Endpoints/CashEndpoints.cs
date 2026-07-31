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
/// Registro rápido de efectivo.
/// </summary>
/// <remarks>
/// Es la ruta que alcanza el token del Atajo de iOS, y la única. Dejó de
/// devolver 501 en esta fase: paga la deuda m11 de CR-002.
///
/// Lo que sigue siendo de la Fase 9 es **interpretar** «Gasté 450 pesos en
/// almuerzo». Aquí el monto llega en centavos enteros y la Fase 9 añadirá un
/// campo de texto que se traduzca a estos mismos campos antes de guardar. La
/// diferencia importa: un modelo de lenguaje podrá proponer el comercio y la
/// categoría, pero el monto que se guarde tendrá que haber pasado por un
/// entero, no por su interpretación.
/// </remarks>
public static class CashEndpoints
{
    /// <summary>
    /// Tope de un registro de efectivo: RD$100,000. No es una regla de negocio,
    /// es un cortafuegos. El token del Atajo vive en una automatización del
    /// teléfono que cualquiera con el teléfono desbloqueado puede abrir; un
    /// tope hace que el peor caso sea molesto en vez de caro.
    /// </summary>
    private const long MaxCents = 10_000_000;

    public static IEndpointRouteBuilder MapCashEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/transactions/cash", CreateAsync)
            .RequireAuthorization(ScopePolicies.CashCreate)
            .RequireRateLimiting(RateLimits.QuickEntry)
            .WithName("CreateCashTransaction")
            .WithTags("Efectivo");

        return app;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateCashRequest request,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AmountCents <= 0)
        {
            return ApiResults.BadRequest("El monto tiene que ser mayor que cero.");
        }

        if (request.AmountCents > MaxCents)
        {
            return ApiResults.BadRequest(
                $"El registro rápido no admite más de {MaxCents / 100} pesos.");
        }

        if (string.IsNullOrWhiteSpace(request.Merchant))
        {
            return ApiResults.BadRequest("Falta en qué se gastó.");
        }

        Account? cash = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Kind == AccountKind.Cash && a.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (cash is null)
        {
            // Fallo cerrado. Crear una cuenta de efectivo al vuelo inventaría un
            // saldo inicial, y ese saldo entra directamente en el líquido de la
            // fórmula del dinero seguro.
            return ApiResults.Conflict(
                "No hay una cuenta de efectivo activa donde registrar el gasto.");
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateOnly localDay = request.OccurredOn ?? LocalTime.LocalDateOf(now);

        // Si el día es hoy se guarda el instante real; si es un día pasado, el
        // mediodía local. El mediodía y no la medianoche: medianoche está a
        // cuatro horas de la frontera del día en UTC y cualquier redondeo la
        // empuja al día equivocado.
        DateTime occurredAt = localDay == LocalTime.LocalDateOf(now)
            ? now
            : LocalTime.StartOfLocalDay(localDay).AddHours(12);

        string merchant = request.Merchant.Trim();
        string normalized = Normalize(merchant);
        var amount = new Money(request.AmountCents);

        string fingerprint = TransactionEndpoints.Fingerprint(
            cash.Id, localDay, amount, normalized);

        bool repeated = await db.Transactions
            .AnyAsync(t => t.Fingerprint == fingerprint, cancellationToken)
            .ConfigureAwait(false);

        if (repeated)
        {
            // El Atajo de iOS reintenta cuando la red falla a medio camino.
            // Sin esto, un reintento crearía un segundo gasto idéntico y el
            // usuario vería el doble de lo que gastó.
            return ApiResults.Conflict(
                "Ya hay un gasto igual ese día. Si de verdad gastaste dos veces "
                + "lo mismo, cámbiale la descripción a uno de los dos.");
        }

        var transaction = new Transaction
        {
            Id = Guid.CreateVersion7(),
            AccountId = cash.Id,
            CategoryId = request.CategoryId,
            MerchantRaw = merchant,
            MerchantNormalized = normalized,
            Amount = amount,
            OccurredAt = occurredAt,
            Kind = TxKind.Cash,
            Status = request.CategoryId is null ? TxStatus.NeedsReview : TxStatus.Posted,
            Source = TxSource.Shortcut,
            Fingerprint = fingerprint,
            Notes = request.Notes,
            ConfidenceBasisPoints = request.CategoryId is null ? 0 : 10000,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Transactions.Add(transaction);

        try
        {
            // El movimiento y el descuento del saldo van en una transacción: o
            // los dos o ninguno. Un gasto guardado sin bajar el saldo deja la
            // cuenta diciendo que hay más dinero del que hay.
            await db.Database
                .CreateExecutionStrategy()
                .ExecuteAsync(async () =>
                {
                    await using var scope = await db.Database
                        .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    // La resta la hace la base, no el proceso. Leer el saldo,
                    // restar en memoria y escribirlo pierde uno de los dos
                    // descuentos cuando entran dos registros a la vez: los dos
                    // leen el mismo saldo y el segundo pisa al primero. El
                    // gasto quedaría registrado y el saldo habría bajado una
                    // sola vez, y esa diferencia no aparece en ningún sitio.
                    await db.Database.ExecuteSqlAsync(
                        $"""
                        UPDATE "Accounts"
                        SET "Balance" = "Balance" - {amount.Cents},
                            "UpdatedAt" = {now}
                        WHERE "Id" = {cash.Id}
                        """,
                        cancellationToken).ConfigureAwait(false);

                    await scope.CommitAsync(cancellationToken).ConfigureAwait(false);
                })
                .ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // La comprobación de huella de más arriba tiene una ventana: entre
            // leer y escribir cabe otra petición idéntica. Quien decide de
            // verdad es el índice único, y aquí se traduce su rechazo a la
            // misma respuesta que daría el camino sin carrera.
            return ApiResults.Conflict(
                "Ya hay un gasto igual ese día. Si de verdad gastaste dos veces "
                + "lo mismo, cámbiale la descripción a uno de los dos.");
        }

        return Results.Created(
            $"/transactions/{transaction.Id}",
            BudgetAssembler.ToView(transaction));
    }

    /// <summary>
    /// Normaliza el comercio para agrupar el historial: mayúsculas, sin
    /// acentos, sin espacios repetidos.
    /// </summary>
    internal static string Normalize(string merchant)
    {
        string upper = merchant.ToUpperInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(upper.Length);
        bool lastWasSpace = false;

        foreach (char c in upper)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            builder.Append(c);
        }

        return builder.ToString().TrimEnd().Normalize(System.Text.NormalizationForm.FormC);
    }
}
