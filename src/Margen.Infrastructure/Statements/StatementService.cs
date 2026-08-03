using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Ingest;
using Margen.Ingest.Statements;
using Microsoft.EntityFrameworkCore;

namespace Margen.Infrastructure.Statements;

/// <summary>Lo que se vería al importar, sin haber importado nada.</summary>
/// <param name="Report">Qué cuadra y qué no.</param>
/// <param name="Rejected">Las filas que no se entendieron, con su motivo.</param>
/// <param name="Delimiter">El separador que se usó.</param>
public sealed record StatementPreview(
    ReconcileReport Report,
    IReadOnlyList<RejectedLine> Rejected,
    char Delimiter);

/// <summary>Qué hizo una importación.</summary>
public readonly record struct ImportReport(
    int Reconciled,
    int Created,
    int Discrepant,
    int Duplicate,
    int Pending,
    int Rejected);

/// <summary>
/// Lee un estado de cuenta, lo cuadra contra lo registrado y aplica el
/// resultado.
/// </summary>
/// <remarks>
/// La lectura del CSV y el emparejamiento son lógica pura y viven en
/// `Margen.Ingest.Statements`. Aquí solo se traen filas, se llama y se escribe.
/// </remarks>
public sealed class StatementService(MargenDbContext db, TimeProvider clock)
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    /// <summary>
    /// Qué pasaría si se importara. **No escribe nada.**
    /// </summary>
    /// <remarks>
    /// Es la mitad del valor del mapeo de columnas: se ve lo que se entendió
    /// antes de tocar la base. Un mapeo con la columna de fecha equivocada se
    /// nota aquí, no después de meter trescientos movimientos con el día mal.
    /// </remarks>
    public async Task<Outcome<StatementPreview>> PreviewAsync(
        StatementProfile profile,
        string content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(content);

        StatementMapping mapping = MappingOf(profile);
        if (!mapping.IsUsable)
        {
            return Outcome.Invalid<StatementPreview>(
                "El perfil no dice cómo leer la fecha, la descripción o el monto.");
        }

        char delimiter = mapping.Delimiter ?? Csv.DetectDelimiter(content);
        StatementParse parse = StatementReader.Read(content, mapping);

        if (parse.Lines.Count == 0)
        {
            return Outcome.Insufficient<StatementPreview>(
                parse.Rejected.Count == 0
                    ? "El archivo no tiene filas de datos."
                    : $"Ninguna de las {parse.Rejected.Count} filas se pudo leer con este perfil.");
        }

        List<BookedTransaction> booked = await BookedAsync(
            profile.AccountId, parse.Lines, cancellationToken).ConfigureAwait(false);

        return Outcome.Computed(new StatementPreview(
            Reconciler.Reconcile(parse.Lines, booked),
            parse.Rejected,
            delimiter));
    }

    /// <summary>
    /// Importa: marca lo conciliado y crea lo que faltaba.
    /// </summary>
    /// <remarks>
    /// **Lo ausente se crea** —es el motivo entero de conciliar: el efectivo
    /// que nadie registró y los correos que no llegaron aparecen aquí—.
    /// **Lo discrepante y lo duplicado no se tocan**: son casos donde hay dos
    /// cifras y elegir una sin preguntar es justo lo que este sistema no hace.
    /// Salen en el informe para que una persona decida.
    /// </remarks>
    public async Task<Outcome<ImportReport>> ImportAsync(
        StatementProfile profile,
        string content,
        CancellationToken cancellationToken)
    {
        Outcome<StatementPreview> preview = await PreviewAsync(profile, content, cancellationToken)
            .ConfigureAwait(false);

        if (!preview.IsComputed)
        {
            // Se traslada el motivo tal cual: quien pidió importar tiene que
            // leer lo mismo que habría leído en la vista previa.
            return preview.Kind == OutcomeKind.Invalid
                ? Outcome.Invalid<ImportReport>(preview.Reason!)
                : Outcome.Insufficient<ImportReport>(preview.Reason!);
        }

        StatementPreview p = preview.Value;
        DateTime now = clock.GetUtcNow().UtcDateTime;

        var toReconcile = p.Report.Verdicts
            .Where(v => v.State == ReconcileState.Matched && v.TransactionId is not null)
            .Select(v => v.TransactionId!.Value)
            .ToHashSet();

        if (toReconcile.Count > 0)
        {
            List<Transaction> rows = await db.Transactions
                .Where(t => toReconcile.Contains(t.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (Transaction row in rows)
            {
                row.Status = TxStatus.Reconciled;
                row.UpdatedAt = now;
            }
        }

        int created = 0;

        foreach (ReconcileVerdict verdict in p.Report.Verdicts)
        {
            if (verdict.State != ReconcileState.Missing) continue;

            Transaction? row = BuildMissing(profile, verdict.Line, now);
            if (row is null) continue;

            db.Transactions.Add(row);
            created++;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // La huella es índice único. Si una fila del estado de cuenta
            // produce la misma huella que un movimiento que el emparejador no
            // reconoció, quien decide es la base: se descarta lo nuevo y se
            // vuelve a intentar sin ello, en vez de perder la importación
            // entera por una fila.
            foreach (var entry in db.ChangeTracker.Entries<Transaction>()
                .Where(e => e.State == EntityState.Added)
                .ToList())
            {
                entry.State = EntityState.Detached;
            }

            created = 0;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Outcome.Computed(new ImportReport(
            toReconcile.Count,
            created,
            p.Report.CountOf(ReconcileState.Discrepant),
            p.Report.CountOf(ReconcileState.Duplicate),
            p.Report.PendingTransactionIds.Count,
            p.Rejected.Count));
    }

    /// <summary>
    /// El movimiento que faltaba, con lo que dice el estado de cuenta.
    /// </summary>
    /// <remarks>
    /// Nace **en revisión y sin categoría**: el estado de cuenta dice cuánto y
    /// cuándo, no en qué. La cascada de la Fase 8 lo clasifica cuando alguien
    /// lo mire, o ya lo hace una regla si el comercio se reconoce.
    ///
    /// La hora es el mediodía local del día del banco. El estado de cuenta no
    /// trae hora, y la medianoche está a cuatro horas de la frontera del día en
    /// UTC: cualquier desajuste la empuja al día equivocado.
    /// </remarks>
    private static Transaction? BuildMissing(
        StatementProfile profile,
        StatementLine line,
        DateTime now)
    {
        string normalized = Fingerprints.NormalizeMerchant(line.Description);
        if (normalized.Length == 0) return null;

        DateTime occurredAt = TimeZoneInfo.ConvertTimeToUtc(
            line.Date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Unspecified), Zone);

        return new Transaction
        {
            Id = Guid.CreateVersion7(),
            AccountId = profile.AccountId,
            MerchantRaw = line.Description,
            MerchantNormalized = normalized,
            Amount = line.Amount,
            OccurredAt = occurredAt,
            Kind = line.Direction == TxDirection.Inflow ? TxKind.Deposit : TxKind.Purchase,
            Status = TxStatus.NeedsReview,
            Source = TxSource.Statement,
            Direction = line.Direction,
            ConfidenceBasisPoints = 0,
            Fingerprint = Fingerprints.OfTransaction(
                profile.AccountId, line.Date, line.Amount, normalized),
            Notes = "Salió del estado de cuenta: no estaba registrado.",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    private async Task<List<BookedTransaction>> BookedAsync(
        Guid accountId,
        IReadOnlyList<StatementLine> lines,
        CancellationToken cancellationToken)
    {
        DateOnly first = lines.Min(l => l.Date).AddDays(-Reconciler.DayTolerance);
        DateOnly last = lines.Max(l => l.Date).AddDays(Reconciler.DayTolerance + 1);

        DateTime from = ToUtc(first);
        DateTime until = ToUtc(last);

        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == accountId
                && t.OccurredAt >= from
                && t.OccurredAt < until
                && t.Status != TxStatus.Rejected
                && t.Status != TxStatus.Duplicate)
            .Select(t => new { t.Id, t.OccurredAt, t.MerchantNormalized, t.Amount, t.Direction })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(t => new BookedTransaction(
                t.Id,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(t.OccurredAt, Zone)),
                t.MerchantNormalized,
                t.Amount,
                t.Direction)),
        ];
    }

    private static StatementMapping MappingOf(StatementProfile p) => new(
        string.IsNullOrEmpty(p.Delimiter) ? null : p.Delimiter[0],
        p.SkipRows,
        p.DateColumn,
        p.DateFormat,
        p.DescriptionColumn,
        p.AmountColumn,
        p.DebitColumn,
        p.CreditColumn,
        string.Equals(p.Decimals, "Comma", StringComparison.OrdinalIgnoreCase)
            ? DecimalStyle.Comma
            : DecimalStyle.Point,
        p.InvertSign);

    private static DateTime ToUtc(DateOnly day) => TimeZoneInfo.ConvertTimeToUtc(
        day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);
}
