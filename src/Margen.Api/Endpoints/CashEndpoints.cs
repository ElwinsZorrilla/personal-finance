using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Contracts;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Margen.Infrastructure.Classification;
using Margen.Ingest;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Endpoints;

/// <summary>
/// Registro rápido de efectivo.
/// </summary>
/// <remarks>
/// Dos puertas al mismo sitio, y las dos las alcanza el token del Atajo:
///
/// - `POST /transactions/cash` con el monto en centavos enteros. Es la que usa
///   la app, que ya tiene un teclado numérico.
/// - `POST /transactions/cash/phrase` con «Gasté 450 pesos en almuerzo». Es la
///   que usa el Atajo de iOS.
///
/// **El monto de la frase lo saca una expresión regular, no un modelo.** Un
/// modelo que un día lea «450» donde decía «45.0» mete un error de un orden de
/// magnitud en una cifra que después se resta del líquido, y no hay ninguna
/// señal de que ha pasado. Lo que sí puede opinar un modelo es la categoría, y
/// eso va por la cascada de la Fase 8, donde tampoco decide solo.
///
/// Dos rutas y no un cuerpo con dos formas: ver `CreateCashRequest`.
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
            .WithTags("Efectivo")
            .Produces<TransactionView>(StatusCodes.Status201Created);

        app.MapPost("/transactions/cash/phrase", CreateFromPhraseAsync)
            .RequireAuthorization(ScopePolicies.CashCreate)
            .RequireRateLimiting(RateLimits.QuickEntry)
            .WithName("CreateCashTransactionFromPhrase")
            .WithTags("Efectivo")
            .Produces<TransactionView>(StatusCodes.Status201Created);

        return app;
    }

    /// <summary>
    /// «Gasté 450 pesos en almuerzo».
    /// </summary>
    /// <remarks>
    /// Interpreta la frase y delega en el mismo camino que la otra puerta. Lo
    /// que no se entiende **no crea nada** y responde diciendo qué falta, con el
    /// texto que el Atajo enseña en pantalla: la persona está de pie en la calle
    /// y necesita saber si se registró o no.
    /// </remarks>
    private static async Task<IResult> CreateFromPhraseAsync(
        [FromBody] CreateCashPhraseRequest request,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        [FromServices] TransactionClassifier classifier,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        PhraseResult parsed = CashPhrase.Parse(request.Text);

        if (!parsed.Ok)
        {
            return ApiResults.BadRequest(parsed.Message!);
        }

        CashEntry entry = parsed.Entry!.Value;
        DateOnly day = LocalTime.LocalDateOf(clock.GetUtcNow().UtcDateTime)
            .AddDays(entry.DayOffset);

        return await CreateAsync(
            new CreateCashRequest(
                entry.Amount.Cents,
                entry.Description,
                CategoryId: null,
                OccurredOn: day,
                Notes: null),
            db,
            clock,
            classifier,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateCashRequest request,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        [FromServices] TransactionClassifier classifier,
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
            Direction = Directions.Of(TxKind.Cash),
            Fingerprint = fingerprint,
            Notes = request.Notes,
            ConfidenceBasisPoints = request.CategoryId is null ? 0 : 10000,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Se clasifica antes de guardar y en la misma transacción, igual que en
        // la ingesta de correo. Una categoría puesta por la cascada deja el
        // movimiento listo; una sugerida lo deja en revisión con la respuesta
        // escrita, que es un toque en vez de una lista de veinte categorías.
        if (request.CategoryId is null)
        {
            await classifier.ApplyAsync(transaction, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // La categoría vino en la petición: la puso una persona en la app.
            transaction.CategoryConfirmedAt = now;
            transaction.ClassificationSource = "Usuario";
        }

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
