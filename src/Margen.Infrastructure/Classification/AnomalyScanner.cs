using Margen.Classify;
using Margen.Domain;
using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Margen.Infrastructure.Classification;

/// <summary>Qué encontró una pasada del detector.</summary>
/// <param name="Created">Alertas nuevas escritas.</param>
/// <param name="Repeated">Anomalías detectadas que ya tenían alerta abierta.</param>
public readonly record struct ScanReport(int Created, int Repeated);

/// <summary>
/// Busca las anomalías de la fase y deja alertas sin repetirlas.
/// </summary>
/// <remarks>
/// La aritmética y las reglas están en <see cref="Anomalies"/>; aquí solo se
/// leen filas, se pregunta y se escribe. La deduplicación tiene dos capas: se
/// mira antes de escribir, y por debajo hay un índice único sobre las alertas
/// sin resolver. La de arriba evita el trabajo; la de abajo es la que manda.
/// </remarks>
public sealed class AnomalyScanner(MargenDbContext db, TimeProvider clock)
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    /// <summary>
    /// Cuántos días hacia atrás se miran los movimientos.
    /// </summary>
    /// <remarks>
    /// Existe por el primer día: al importar historia vieja, cada cargo antiguo
    /// dispararía su alerta y la pantalla de revisión nacería con trescientas.
    /// Nadie lee trescientas alertas; se borran todas de golpe y con ellas la
    /// que importaba.
    /// </remarks>
    public const int RecentDays = 7;

    /// <summary>
    /// Cuántos cargos anteriores del mismo comercio construyen el rango normal.
    /// </summary>
    public const int HistoryWindow = 30;

    /// <summary>
    /// Mira los movimientos recientes y los recurrentes vencidos, y escribe lo
    /// que encuentre.
    /// </summary>
    /// <remarks>
    /// **Guarda al final**, una sola vez. Y devuelve el recuento en vez de
    /// tragárselo: una pasada que no encuentra nada y una que encuentra veinte
    /// tienen que verse distintas en el registro.
    /// </remarks>
    public async Task<ScanReport> ScanAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, Zone));

        var found = new List<Anomaly>();

        found.AddRange(await UnusualAmountsAsync(now, cancellationToken).ConfigureAwait(false));
        found.AddRange(await RecurringAsync(today, cancellationToken).ConfigureAwait(false));

        return await WriteAsync(found, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<Anomaly>> UnusualAmountsAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        DateTime since = now.AddDays(-RecentDays);

        var recent = await db.Transactions
            .AsNoTracking()
            .Where(t => t.OccurredAt >= since
                && t.Status != TxStatus.Duplicate
                && t.Status != TxStatus.Rejected
                && t.Direction == TxDirection.Outflow)
            .Select(t => new { t.Id, t.MerchantNormalized, t.Amount, t.OccurredAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var found = new List<Anomaly>();

        foreach (var tx in recent)
        {
            // El propio movimiento no participa en la definición de lo normal:
            // un cargo que se compara consigo mismo nunca es raro.
            List<Money> history = await db.Transactions
                .AsNoTracking()
                .Where(t => t.MerchantNormalized == tx.MerchantNormalized
                    && t.Id != tx.Id
                    && t.OccurredAt < tx.OccurredAt
                    && t.Status != TxStatus.Duplicate
                    && t.Status != TxStatus.Rejected
                    && t.Direction == TxDirection.Outflow)
                .OrderByDescending(t => t.OccurredAt)
                .Take(HistoryWindow)
                .Select(t => t.Amount)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            Anomaly? anomaly = Anomalies.UnusualAmount(
                tx.Id, tx.MerchantNormalized, tx.Amount, NormalRanges.Of(history));

            if (anomaly is not null) found.Add(anomaly.Value);
        }

        return found;
    }

    private async Task<List<Anomaly>> RecurringAsync(
        DateOnly today,
        CancellationToken cancellationToken)
    {
        List<RecurringPayment> due = await db.RecurringPayments
            .AsNoTracking()
            .Where(r => r.IsActive && r.NextDueDate != null && r.NextDueDate <= today)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var found = new List<Anomaly>();

        foreach (RecurringPayment payment in due)
        {
            DateOnly dueDate = payment.NextDueDate!.Value;
            bool paid = payment.LastPaidDate is not null && payment.LastPaidDate >= dueDate;

            Anomaly? missing = Anomalies.MissingRecurring(
                payment.Id, payment.Label, dueDate, today, paid);

            if (missing is not null) found.Add(missing.Value);

            if (!paid) continue;

            // Pagado: ahora la pregunta es si costó lo que decía.
            Money? charged = await ChargedForAsync(payment, dueDate, cancellationToken)
                .ConfigureAwait(false);

            if (charged is null) continue;

            Anomaly? change = Anomalies.SubscriptionPriceChange(
                payment.Id, payment.Label, payment.ExpectedAmount, charged.Value);

            if (change is not null) found.Add(change.Value);
        }

        return found;
    }

    private async Task<Money?> ChargedForAsync(
        RecurringPayment payment,
        DateOnly dueDate,
        CancellationToken cancellationToken)
    {
        if (payment.CategoryId is null) return null;

        // La ventana es el vencimiento más la cortesía, en instantes UTC del día
        // local: un cargo del día 1 a las 22:00 en Santo Domingo es del día 2 en
        // UTC, y buscarlo por fecha UTC lo dejaría fuera.
        DateTime from = ToUtc(dueDate.AddDays(-Anomalies.GraceDays));
        DateTime to = ToUtc(dueDate.AddDays(Anomalies.GraceDays + 1));

        return await db.Transactions
            .AsNoTracking()
            .Where(t => t.CategoryId == payment.CategoryId
                && t.OccurredAt >= from
                && t.OccurredAt < to
                && t.Status != TxStatus.Duplicate
                && t.Status != TxStatus.Rejected
                && t.Direction == TxDirection.Outflow)
            .OrderByDescending(t => t.OccurredAt)
            .Select(t => (Money?)t.Amount)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ScanReport> WriteAsync(
        List<Anomaly> found,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (found.Count == 0) return new ScanReport(0, 0);

        string[] keys = [.. found.Select(a => a.DedupeKey).Distinct(StringComparer.Ordinal)];

        HashSet<string> open = [.. await db.Alerts
            .AsNoTracking()
            .Where(a => a.ResolvedAt == null && keys.Contains(a.DedupeKey))
            .Select(a => a.DedupeKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)];

        int created = 0;
        int repeated = 0;

        foreach (Anomaly anomaly in found)
        {
            // `open` acumula lo escrito en esta misma pasada además de lo que ya
            // estaba: dos movimientos del mismo comercio en la misma tanda no
            // pueden producir la misma clave dos veces y reventar contra el
            // índice único.
            if (!open.Add(anomaly.DedupeKey))
            {
                repeated++;
                continue;
            }

            db.Alerts.Add(new Alert
            {
                Id = Guid.CreateVersion7(),
                Kind = anomaly.Kind,
                Title = anomaly.Title,
                Detail = anomaly.Detail,
                DedupeKey = anomaly.DedupeKey,
                IsUrgent = anomaly.IsUrgent,
                TransactionId = anomaly.TransactionId,
                RecurringPaymentId = anomaly.RecurringPaymentId,
                CreatedAt = now,
            });

            created++;
        }

        if (created == 0) return new ScanReport(0, repeated);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // El índice único sobre las alertas sin resolver es quien decide de
            // verdad, igual que la huella en la ingesta. Entre la comprobación y
            // la escritura cabe otra pasada, y su rechazo significa lo mismo que
            // haberla encontrado abierta: la alerta ya existe.
            foreach (var entry in db.ChangeTracker.Entries<Alert>().ToList())
            {
                entry.State = EntityState.Detached;
            }

            return new ScanReport(0, repeated + created);
        }

        return new ScanReport(created, repeated);
    }

    private static DateTime ToUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone);
}
