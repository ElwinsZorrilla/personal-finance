using Margen.Budget;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Budget;

/// <summary>
/// De la base a las entradas del motor, y de la salida del motor al cable.
/// </summary>
/// <remarks>
/// Es la única pieza nueva con lógica de esta fase; todo lo demás es
/// transporte. El motor está probado al 100 %, así que lo que puede salir mal
/// aquí no es un cálculo: es traerle datos equivocados y que calcule
/// impecablemente sobre ellos.
/// </remarks>
public sealed class BudgetAssembler(MargenDbContext db, TimeProvider clock)
{
    /// <summary>Cuántos movimientos recientes acompañan al panel.</summary>
    private const int RecentCount = 12;

    /// <summary>
    /// Cuánto se mira hacia atrás para encontrar la compra original de una
    /// devolución del ciclo. Sin este margen, <c>ResolveCategory</c> no
    /// encuentra el original y lo imputa a la categoría de la devolución.
    /// </summary>
    private static readonly TimeSpan RefundLookback = TimeSpan.FromDays(120);

    public async Task<Outcome<DashboardView>> BuildAsync(CancellationToken cancellationToken)
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
            // Sin período abierto no hay nada que responder. Inventar uno de
            // mes natural daría un «disponible diario» que no se parece a nada,
            // porque el ciclo real de esta persona va de un ingreso al
            // siguiente y solo ella sabe cuándo cobra.
            return Outcome.Insufficient<DashboardView>(
                "No hay un período presupuestario abierto que contenga el día de hoy.");
        }

        Outcome<BudgetCycle> cycleOutcome = BudgetCycle.Inclusive(period.StartDate, period.EndDate);
        if (!cycleOutcome.IsComputed)
        {
            return Outcome.Invalid<DashboardView>(cycleOutcome.Reason!);
        }

        BudgetCycle cycle = cycleOutcome.Value;

        IReadOnlyList<LedgerEntry> entries = await LoadLedgerAsync(cycle, cancellationToken)
            .ConfigureAwait(false);

        Outcome<SpendingSummary> spending = SpendingLedger.Summarize(cycle, entries);
        if (!spending.IsComputed)
        {
            return Outcome.Invalid<DashboardView>(spending.Reason!);
        }

        SafeToSpendInputs inputs = await BuildInputsAsync(period, cycle, cancellationToken)
            .ConfigureAwait(false);

        Outcome<SafeToSpendBreakdown> safe = SafeToSpend.Compute(inputs);
        if (!safe.IsComputed)
        {
            return Outcome.Invalid<DashboardView>(safe.Reason!);
        }

        DailyAllowance daily = DailyAllowanceCalculator
            .For(safe.Value.Total, cycle, today).Value;

        List<CategoryBudget> budgets = await db.CategoryBudgets
            .AsNoTracking()
            .Include(b => b.Category)
            .Where(b => b.BudgetPeriodId == period.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Money totalBudget = Money.Sum(budgets.Select(b => b.Allocated + b.Adjustment));

        Outcome<PaceReading> pace = Pace.Read(totalBudget, spending.Value.Total, cycle, today);
        if (!pace.IsComputed)
        {
            return Outcome.Invalid<DashboardView>(pace.Reason!);
        }

        Outcome<Money> baseline = await BuildBaselineAsync(period, cancellationToken)
            .ConfigureAwait(false);

        return Outcome.Computed(new DashboardView(
            Period: new PeriodView(
                cycle.Start,
                cycle.End,
                today,
                cycle.TotalDays,
                cycle.DaysRemainingFrom(today),
                cycle.ElapsedDaysAt(today)),
            SafeToSpendCents: safe.Value.Total.Cents,
            IsOverdrawn: safe.Value.IsOverdrawn,
            ShortfallCents: safe.Value.Shortfall.Cents,
            Deductions: [.. safe.Value.Deductions
                .Select(d => new DeductionView(d.Kind.ToString(), d.Amount.Cents))],
            TotalDeductedCents: safe.Value.TotalDeducted.Cents,
            LiquidCents: safe.Value.Liquid.Cents,
            SafeTodayCents: daily.PerDay.Cents,
            SpentSoFarCents: spending.Value.Total.Cents,
            ExpectedByNowCents: pace.Value.Expected.Cents,
            PaceDeviationCents: pace.Value.Deviation.Cents,
            ProjectedCloseCents: pace.Value.ProjectedClose.Cents,
            ProjectedDepletion: pace.Value.ProjectedDepletion,
            HistoricalBaseline: MoneyValue.From(baseline),
            HistoricalPeriodsConsidered: baseline.IsComputed
                ? HistoricalBaseline.PeriodsConsidered(await CountClosedAsync(period, cancellationToken).ConfigureAwait(false))
                : 0,
            Categories: BuildCategoryLines(budgets, spending.Value, cycle, today),
            Commitments: await LoadCommitmentsAsync(cycle, cancellationToken).ConfigureAwait(false),
            Attention: await LoadAlertsAsync(cancellationToken).ConfigureAwait(false),
            Recent: await LoadRecentAsync(cycle, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>
    /// Trae los movimientos del ciclo más los que alguna devolución referencia.
    /// </summary>
    /// <remarks>
    /// La ventana se amplía hacia atrás porque una devolución de este mes puede
    /// corresponder a una compra del anterior, y aun así tiene que descontarse
    /// de la categoría de aquella compra. Traer solo el ciclo haría que se
    /// imputara a la de la devolución, inflando una categoría y dejando otra en
    /// negativo para siempre.
    ///
    /// La consulta usa el rango UTC del ciclo, no las fechas locales: comparar
    /// una columna <c>timestamptz</c> contra una fecha sin zona deja fuera las
    /// cuatro últimas horas del último día, que en hora local son las de más
    /// gasto.
    /// </remarks>
    private async Task<IReadOnlyList<LedgerEntry>> LoadLedgerAsync(
        BudgetCycle cycle,
        CancellationToken cancellationToken)
    {
        (DateTime from, DateTime untilExclusive) = LocalTime.UtcRangeOf(cycle);
        DateTime lookback = from - RefundLookback;

        var rows = await db.Transactions
            .AsNoTracking()
            .Where(t => t.OccurredAt >= lookback && t.OccurredAt < untilExclusive)
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

        return [.. rows.Select(r => new LedgerEntry(
            r.Id,
            r.CategoryId,
            r.Amount,
            r.Kind,
            r.Status,
            LocalTime.LocalDateOf(r.OccurredAt),
            r.RefundsTransactionId,
            r.Direction))];
    }

    /// <summary>
    /// Las seis cifras de la fórmula del dinero seguro.
    /// </summary>
    /// <remarks>
    /// Qué es cada una es una decisión de producto y vive aquí, no en el motor.
    /// El motor recibe cifras y las resta en un orden fijo.
    ///
    /// <c>Withholdings</c> y el gasto del ciclo se solapan a propósito: el saldo
    /// que reporta el banco todavía no descuenta lo autorizado y sin asentar, así
    /// que hay que restarlo del líquido aunque ya cuente como gasto. Si no se
    /// restara, el dinero seguro sería mayor de lo real durante los dos o tres
    /// días que el banco tarda en asentar, que es justo cuando el usuario está
    /// decidiendo si puede gastar.
    /// </remarks>
    private async Task<SafeToSpendInputs> BuildInputsAsync(
        Domain.Entities.BudgetPeriod period,
        BudgetCycle cycle,
        CancellationToken cancellationToken)
    {
        List<Account> accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => a.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Money liquid = Money.Sum(accounts
            .Where(a => a.Kind != AccountKind.Credit)
            .Select(a => a.Balance));

        // Deuda de tarjeta: el saldo de una cuenta de crédito es negativo
        // cuando se debe. Se aparta en valor absoluto.
        Money cardReserve = Money.Sum(accounts
            .Where(a => a.Kind == AccountKind.Credit && a.Balance.IsNegative)
            .Select(a => a.Balance.Abs));

        Money pendingObligations = Money.Sum(await db.RecurringPayments
            .AsNoTracking()
            .Where(p => p.IsActive
                && p.NextDueDate != null
                && p.NextDueDate >= cycle.Start
                && p.NextDueDate <= cycle.End
                && (p.LastPaidDate == null || p.LastPaidDate < cycle.Start))
            .Select(p => p.ExpectedAmount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));

        (DateTime from, DateTime untilExclusive) = LocalTime.UtcRangeOf(cycle);

        Money withholdings = Money.Sum(await db.Transactions
            .AsNoTracking()
            .Where(t => t.Status == TxStatus.Pending
                && t.OccurredAt >= from
                && t.OccurredAt < untilExclusive)
            .Select(t => t.Amount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));

        return new SafeToSpendInputs(
            liquid,
            pendingObligations,
            cardReserve,
            period.CommittedSavings,
            period.SafetyFund,
            withholdings);
    }

    /// <summary>
    /// La base histórica sobre los tres períodos cerrados más recientes.
    /// </summary>
    /// <remarks>
    /// El gasto de cada período cerrado se vuelve a sumar desde los
    /// movimientos en vez de leerse de un campo. Un total guardado se queda
    /// viejo en cuanto alguien recategoriza un movimiento de un mes cerrado, y
    /// esa cifra propone el presupuesto del período siguiente.
    /// </remarks>
    private async Task<Outcome<Money>> BuildBaselineAsync(
        Domain.Entities.BudgetPeriod current,
        CancellationToken cancellationToken)
    {
        List<Domain.Entities.BudgetPeriod> closed = await db.BudgetPeriods
            .AsNoTracking()
            .Where(p => p.IsClosed && p.EndDate < current.StartDate)
            .OrderByDescending(p => p.StartDate)
            .Take(HistoricalBaseline.Weights.Length)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var totals = new List<Money>(closed.Count);

        foreach (Domain.Entities.BudgetPeriod past in closed)
        {
            Outcome<BudgetCycle> pastCycle =
                BudgetCycle.Inclusive(past.StartDate, past.EndDate);

            if (!pastCycle.IsComputed)
            {
                continue;
            }

            IReadOnlyList<LedgerEntry> entries =
                await LoadLedgerAsync(pastCycle.Value, cancellationToken).ConfigureAwait(false);

            Outcome<SpendingSummary> summary = SpendingLedger.Summarize(pastCycle.Value, entries);

            if (!summary.IsComputed)
            {
                return Outcome.Invalid<Money>(summary.Reason!);
            }

            // Un período con gasto neto negativo —más devoluciones que
            // compras— no describe ningún hábito. El motor lo rechaza, así que
            // se acota a cero antes de pasárselo en lugar de perder toda la
            // base histórica por un mes raro.
            totals.Add(summary.Value.Total.IsNegative ? Money.Zero : summary.Value.Total);
        }

        return HistoricalBaseline.Average(totals);
    }

    private async Task<int> CountClosedAsync(
        Domain.Entities.BudgetPeriod current,
        CancellationToken cancellationToken) =>
        await db.BudgetPeriods
            .AsNoTracking()
            .CountAsync(
                p => p.IsClosed && p.EndDate < current.StartDate,
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Una línea por categoría, con su proyección de cierre.
    /// </summary>
    /// <remarks>
    /// La proyección la calcula el motor, categoría por categoría, con la misma
    /// función que proyecta el total. Extrapolar en la pantalla sería el segundo
    /// sitio donde vive la misma fórmula, y el que se desincroniza.
    /// </remarks>
    private static IReadOnlyList<CategoryLineView> BuildCategoryLines(
        List<CategoryBudget> budgets,
        SpendingSummary spending,
        BudgetCycle cycle,
        DateOnly today)
    {
        return [.. budgets
            .Select(b =>
            {
                Money spent = spending.ByCategory.TryGetValue(b.CategoryId, out Money value)
                    ? value
                    : Money.Zero;

                var allocation = new CategoryAllocation(
                    b.CategoryId,
                    b.Category?.Priority ?? Priority.Flexible,
                    b.Allocated + b.Adjustment,
                    spent);

                // Un gasto neto negativo —más devoluciones que compras— no tiene
                // ritmo que extrapolar, y el motor rechaza un presupuesto
                // negativo. En los dos casos la proyección honesta es lo
                // gastado hasta ahora, no una cifra inventada.
                Outcome<PaceReading> pace = Pace.Read(
                    allocation.Allocated.Cents < 0 ? Money.Zero : allocation.Allocated,
                    spent,
                    cycle,
                    today);

                Money projected = pace.IsComputed ? pace.Value.ProjectedClose : spent;

                return new CategoryLineView(
                    b.CategoryId,
                    b.Category?.Name ?? "Sin nombre",
                    allocation.Priority.ToString(),
                    allocation.Allocated.Cents,
                    spent.Cents,
                    allocation.Available.Cents,
                    projected.Cents,
                    projected > allocation.Allocated,
                    allocation.CanBeTrimmed);
            })
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.Name, StringComparer.Ordinal)];
    }

    private async Task<IReadOnlyList<CommitmentView>> LoadCommitmentsAsync(
        BudgetCycle cycle,
        CancellationToken cancellationToken) =>
        await db.RecurringPayments
            .AsNoTracking()
            .Where(p => p.IsActive
                && p.NextDueDate != null
                && p.NextDueDate >= cycle.Start
                && p.NextDueDate <= cycle.End)
            .OrderBy(p => p.NextDueDate)
            .Select(p => new CommitmentView(
                p.Id,
                p.Label,
                p.ExpectedAmount.Cents,
                p.Priority.ToString(),
                p.NextDueDate))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<AlertView>> LoadAlertsAsync(
        CancellationToken cancellationToken) =>
        await db.Alerts
            .AsNoTracking()
            .Where(a => a.ResolvedAt == null)
            .OrderByDescending(a => a.IsUrgent)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => new AlertView(
                a.Id,
                a.Kind.ToString(),
                a.Title,
                a.Detail,
                a.IsUrgent,
                a.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<TransactionView>> LoadRecentAsync(
        BudgetCycle cycle,
        CancellationToken cancellationToken)
    {
        (DateTime from, DateTime untilExclusive) = LocalTime.UtcRangeOf(cycle);

        List<Transaction> rows = await db.Transactions
            .AsNoTracking()
            .Include(t => t.Account)
            .Include(t => t.Category)
            .Where(t => t.OccurredAt >= from && t.OccurredAt < untilExclusive)
            .OrderByDescending(t => t.OccurredAt)
            .Take(RecentCount)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(ToView)];
    }

    public static TransactionView ToView(Transaction t)
    {
        ArgumentNullException.ThrowIfNull(t);

        return new TransactionView(
            t.Id,
            t.MerchantRaw,
            t.Amount.Cents,
            t.Currency,
            t.OccurredAt,
            LocalTime.LocalDateOf(t.OccurredAt),
            t.Kind.ToString(),
            t.Direction.ToString(),
            Directions.Label(t.Direction),
            t.IsIncome,
            t.Status.ToString(),
            t.Source.ToString(),
            t.CategoryId,
            t.Category?.Name,
            t.Account?.LastFour ?? "----",
            t.ConfidenceBasisPoints);
    }
}
