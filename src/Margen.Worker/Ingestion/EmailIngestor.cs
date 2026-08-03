using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Margen.Ingest;
using Microsoft.EntityFrameworkCore;

namespace Margen.Worker.Ingestion;

/// <summary>Qué pasó con un correo. Es lo que devuelve la tubería y lo que se registra.</summary>
public enum IngestOutcome
{
    /// <summary>Se guardó el correo y se creó el movimiento.</summary>
    Created,

    /// <summary>El correo ya estaba: mismo identificador o mismo cuerpo.</summary>
    AlreadySeen,

    /// <summary>Se guardó el correo; ningún parser lo reconoció. No crea nada.</summary>
    Unrecognized,

    /// <summary>Se reconoció pero falta un dato crítico. No crea nada.</summary>
    NeedsReview,

    /// <summary>El movimiento ya existía con la misma huella.</summary>
    DuplicateExact,

    /// <summary>Se creó marcado como duplicado probable, para que lo mire una persona.</summary>
    DuplicateProbable,

    /// <summary>El parser lanzó. El correo queda guardado para reprocesar.</summary>
    Failed,
}

public sealed record IngestReport(IngestOutcome Outcome, Guid? EmailId, Guid? TransactionId, string? Detail);

/// <summary>
/// El camino de un correo hasta un movimiento.
/// </summary>
/// <remarks>
/// El orden importa y es este: **guardar el correo primero, interpretarlo
/// después**. Un parser que revienta con el original ya guardado se arregla y
/// se reprocesa; uno que revienta antes de guardar pierde el correo, y el banco
/// no lo manda dos veces.
///
/// Reprocesar el mismo correo dos veces no crea dos movimientos. Tres defensas
/// en cadena: identificador de mensaje, hash del cuerpo y huella del
/// movimiento. Las tres son índices únicos desde la migración inicial, así que
/// la última palabra la tiene la base y no una comprobación en memoria que
/// puede llegar tarde.
/// </remarks>
public sealed class EmailIngestor(
    MargenDbContext db,
    ParserRegistry parsers,
    TimeProvider clock,
    DuplicateDetector? detector = null)
{
    private readonly DuplicateDetector _detector = detector ?? new DuplicateDetector();

    /// <summary>Cuánto se mira hacia atrás al buscar un duplicado aproximado.</summary>
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(2);

    public async Task<IngestReport> IngestAsync(RawEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        string bodyHash = Fingerprints.OfBody(email.Body);

        // Primera y segunda defensa, juntas: el identificador cubre el caso
        // normal y el hash cubre que el servidor de correo lo haya reescrito.
        IncomingEmail? existing = await db.IncomingEmails
            .FirstOrDefaultAsync(
                e => e.MessageId == email.MessageId || e.BodyHash == bodyHash,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return new IngestReport(
                IngestOutcome.AlreadySeen,
                existing.Id,
                null,
                "El correo ya estaba registrado.");
        }

        var stored = new IncomingEmail
        {
            Id = Guid.CreateVersion7(),
            MessageId = email.MessageId,
            BodyHash = bodyHash,
            Sender = email.Sender,
            Subject = email.Subject,
            Body = email.Body,
            ReceivedAt = email.ReceivedAtUtc,
            FetchedAt = now,
            Status = EmailStatus.Received,
        };

        db.IncomingEmails.Add(stored);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await ProcessAsync(stored, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Interpreta un correo ya guardado. Es también el camino del reproceso.
    /// </summary>
    public async Task<IngestReport> ProcessAsync(
        IncomingEmail stored,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stored);

        DateTime now = clock.GetUtcNow().UtcDateTime;

        var raw = new RawEmail(
            stored.MessageId,
            stored.Sender,
            stored.Subject,
            stored.Body,
            stored.ReceivedAt);

        IEmailParser? parser = parsers.Find(raw);

        if (parser is null)
        {
            // Fallo cerrado: no crea nada y queda para revisión humana.
            stored.Status = EmailStatus.Unrecognized;
            stored.ProcessedAt = now;
            stored.FailureReason = "Ningún parser reconoce este remitente.";
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new IngestReport(
                IngestOutcome.Unrecognized, stored.Id, null, stored.FailureReason);
        }

        stored.ParserName = parser.Name;
        stored.ParserVersion = parser.Version;

        ParseResult result;
        try
        {
            result = parser.Parse(raw);
        }
#pragma warning disable CA1031 // Un parser mal escrito no puede tumbar la tanda:
        // el correo queda guardado con el motivo y se reprocesa cuando se
        // arregle. Dejar subir la excepción perdería los correos siguientes.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            stored.Status = EmailStatus.Failed;
            stored.ProcessedAt = now;
            stored.FailureReason = ex.Message;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new IngestReport(IngestOutcome.Failed, stored.Id, null, ex.Message);
        }

        if (result.Status is ParseStatus.NotMine or ParseStatus.NeedsReview)
        {
            stored.Status = result.Status == ParseStatus.NotMine
                ? EmailStatus.Unrecognized
                : EmailStatus.Received;
            stored.ProcessedAt = now;
            stored.FailureReason = result.Reason ?? "El parser no pudo extraer todo.";
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new IngestReport(
                result.Status == ParseStatus.NotMine
                    ? IngestOutcome.Unrecognized
                    : IngestOutcome.NeedsReview,
                stored.Id,
                null,
                stored.FailureReason);
        }

        return await MaterializeAsync(stored, result.Transaction!, now, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IngestReport> MaterializeAsync(
        IncomingEmail stored,
        ParsedTransaction parsed,
        DateTime now,
        CancellationToken cancellationToken)
    {
        Account? account = await db.Accounts
            .FirstOrDefaultAsync(
                a => a.LastFour == parsed.AccountLastFour && a.IsActive,
                cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            // No se crea una cuenta al vuelo: tendría un saldo inicial
            // inventado, y ese saldo entra directo en el líquido de la fórmula
            // del dinero seguro.
            stored.Status = EmailStatus.Received;
            stored.ProcessedAt = now;
            stored.FailureReason =
                $"No hay una cuenta activa que termine en {parsed.AccountLastFour}.";
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new IngestReport(
                IngestOutcome.NeedsReview, stored.Id, null, stored.FailureReason);
        }

        string merchant = Fingerprints.NormalizeMerchant(parsed.MerchantRaw);
        DateOnly localDay = LocalDayOf(parsed.OccurredAtUtc);
        string fingerprint = Fingerprints.OfTransaction(
            account.Id, localDay, parsed.Amount, merchant, parsed.Reference);

        DateTime desde = parsed.OccurredAtUtc - Lookback;
        DateTime hasta = parsed.OccurredAtUtc + Lookback;

        List<ExistingTransaction> candidates = await db.Transactions
            .AsNoTracking()
            .Where(t => t.OccurredAt >= desde && t.OccurredAt <= hasta)
            .Select(t => new ExistingTransaction(
                t.Id,
                t.AccountId,
                t.Amount,
                t.OccurredAt,
                t.MerchantNormalized,
                t.Fingerprint))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        DuplicateCheck check = _detector.Check(parsed, account.Id, fingerprint, candidates);

        if (check.Verdict == DuplicateVerdict.Exact)
        {
            // Es lo que hace que reprocesar el buzón entero no duplique nada.
            stored.Status = EmailStatus.Duplicate;
            stored.ProcessedAt = now;
            stored.FailureReason = check.Reason;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return new IngestReport(
                IngestOutcome.DuplicateExact, stored.Id, check.MatchId, check.Reason);
        }

        bool probable = check.Verdict == DuplicateVerdict.Probable;

        var transaction = new Transaction
        {
            Id = Guid.CreateVersion7(),
            AccountId = account.Id,
            MerchantRaw = parsed.MerchantRaw,
            MerchantNormalized = merchant,
            Amount = parsed.Amount,
            Currency = parsed.Currency,
            OccurredAt = parsed.OccurredAtUtc,

            // Sin clasificar: eso es la Fase 8. Nace en revisión, que es donde
            // debe estar algo cuya categoría no se sabe.
            CategoryId = null,
            Kind = parsed.Kind,
            Status = probable ? TxStatus.Duplicate : TxStatus.NeedsReview,
            Source = TxSource.Email,

            // Deducida del tipo, con la misma regla que usa la entidad al
            // nacer. Una transferencia nace como egreso: el correo dice que
            // el dinero salió, no a dónde fue, y contar de menos es el error
            // que hace gastar dinero que no está.
            Direction = Directions.Of(parsed.Kind),
            ConfidenceBasisPoints = 0,
            DuplicateOfTransactionId = probable ? check.MatchId : null,
            IncomingEmailId = stored.Id,
            Fingerprint = fingerprint,
            Notes = parsed.Reference is null ? null : $"Referencia {parsed.Reference}",
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Transactions.Add(transaction);

        stored.Status = EmailStatus.Parsed;
        stored.ProcessedAt = now;
        stored.FailureReason = null;

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // La huella es índice único. Entre la comprobación y la escritura
            // cabe otra tanda: quien decide de verdad es la base, y su rechazo
            // significa lo mismo que la coincidencia exacta.
            db.Entry(transaction).State = EntityState.Detached;

            return new IngestReport(
                IngestOutcome.DuplicateExact,
                stored.Id,
                null,
                "La huella ya existía al escribir.");
        }

        return new IngestReport(
            probable ? IngestOutcome.DuplicateProbable : IngestOutcome.Created,
            stored.Id,
            transaction.Id,
            check.Reason);
    }

    /// <summary>
    /// El día local de un instante. Duplica a propósito lo mínimo de
    /// `LocalTime` del API: el worker no depende del proyecto web.
    /// </summary>
    private static DateOnly LocalDayOf(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));

    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");
}
