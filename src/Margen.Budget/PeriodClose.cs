using Margen.Domain;

namespace Margen.Budget;

/// <summary>Lo gastado en una categoría durante un período cerrado.</summary>
public readonly record struct CategoryHistory(Guid CategoryId, Priority Priority, Money Spent);

/// <summary>Lo que se recomienda asignar a una categoría en el período que viene.</summary>
/// <param name="CategoryId">La categoría.</param>
/// <param name="Recommended">Cuánto asignarle.</param>
/// <param name="Basis">De qué salió la cifra, en castellano.</param>
public readonly record struct Recommendation(Guid CategoryId, Money Recommended, string Basis);

/// <summary>El presupuesto recomendado para el período siguiente.</summary>
/// <param name="Categories">Una recomendación por categoría.</param>
/// <param name="Total">La suma, que es exactamente lo repartido.</param>
/// <param name="Unallocated">Lo que sobra del ingreso esperado.</param>
public sealed record RecommendedBudget(
    IReadOnlyList<Recommendation> Categories,
    Money Total,
    Money Unallocated);

/// <summary>
/// Cierra un período y propone el presupuesto del siguiente.
/// </summary>
/// <remarks>
/// La propuesta sale de **lo que se gastó de verdad**, no de lo que se asignó:
/// un presupuesto que se copia a sí mismo mes tras mes repite el error del
/// primer mes para siempre. Si en Comida se asignaron 10 000 y se gastaron
/// 14 000 tres períodos seguidos, la recomendación es 14 000 y no 10 000.
///
/// Lógica pura, como todo el motor: no sabe qué día es hoy ni qué hay en la
/// base.
/// </remarks>
public static class PeriodClose
{
    /// <summary>
    /// Qué asignar en el período que viene, a partir de los cerrados.
    /// </summary>
    /// <param name="historyNewestFirst">
    /// Por categoría, lo gastado en cada período cerrado, del más reciente al
    /// más viejo. La media ponderada de <see cref="HistoricalBaseline"/> es la
    /// que decide el peso de cada uno.
    /// </param>
    /// <param name="expectedIncome">El ingreso esperado del período nuevo.</param>
    /// <remarks>
    /// Devuelve <see cref="OutcomeKind.Insufficient"/> cuando no hay ni un
    /// período cerrado. Sin historia no hay recomendación, y proponer ceros
    /// —o el ingreso repartido a partes iguales— sería inventarse un
    /// presupuesto y presentarlo como si saliera de algún sitio.
    ///
    /// **Si lo recomendado pasa del ingreso esperado, se recorta**, y se recorta
    /// por prioridad: lo Esencial y lo Importante no se tocan, y el exceso sale
    /// de lo Flexible y lo Opcional a prorrata. Presentar un presupuesto que no
    /// cabe en el ingreso es proponer que se gaste dinero que no está.
    /// </remarks>
    public static Outcome<RecommendedBudget> Recommend(
        IReadOnlyDictionary<Guid, IReadOnlyList<CategoryHistory>> historyNewestFirst,
        Money expectedIncome)
    {
        ArgumentNullException.ThrowIfNull(historyNewestFirst);

        if (historyNewestFirst.Count == 0)
        {
            return Outcome.Insufficient<RecommendedBudget>(
                "No hay ningún período cerrado del que sacar la recomendación.");
        }

        var raw = new List<(Guid Id, Priority Priority, Money Amount, int Periods)>();

        foreach ((Guid categoryId, IReadOnlyList<CategoryHistory> history) in historyNewestFirst)
        {
            if (history.Count == 0) continue;

            Outcome<Money> average = HistoricalBaseline.Average(
                [.. history.Select(h => h.Spent)]);

            if (!average.IsComputed) continue;

            raw.Add((
                categoryId,
                history[0].Priority,
                average.Value,
                HistoricalBaseline.PeriodsConsidered(history.Count)));
        }

        if (raw.Count == 0)
        {
            return Outcome.Insufficient<RecommendedBudget>(
                "Ningún período cerrado tiene gasto del que sacar una media.");
        }

        Money total = Money.Sum(raw.Select(r => r.Amount));

        List<Recommendation> categories = total <= expectedIncome
            ? [.. raw.Select(r => new Recommendation(
                r.Id,
                r.Amount,
                $"Media ponderada de {r.Periods} período(s) cerrados."))]
            : Trim(raw, total - expectedIncome);

        Money assigned = Money.Sum(categories.Select(c => c.Recommended));

        return Outcome.Computed(new RecommendedBudget(
            categories,
            assigned,
            expectedIncome - assigned));
    }

    /// <summary>
    /// Recorta el exceso de lo que se puede recortar, a prorrata.
    /// </summary>
    /// <remarks>
    /// El reparto usa <see cref="Money.Prorate"/>, que es de resto mayor: los
    /// centavos que sobran de la división se reparten uno a uno, así que la
    /// suma de los recortes es **exactamente** el exceso. Repartir con división
    /// entera a secas deja centavos sin asignar, y esos centavos aparecen luego
    /// como una diferencia que nadie sabe de dónde sale.
    ///
    /// Si lo recortable no alcanza para cubrir el exceso, se recorta todo lo
    /// que hay y **la recomendación sigue pasándose del ingreso**. Eso no es un
    /// fallo del cálculo: es que los compromisos no caben en el sueldo, y
    /// taparlo recortando lo Esencial sería esconder el problema.
    /// </remarks>
    private static List<Recommendation> Trim(
        List<(Guid Id, Priority Priority, Money Amount, int Periods)> raw,
        Money excess)
    {
        var trimmable = raw
            .Select((r, i) => (Index: i, r.Amount))
            .Where(x => raw[x.Index].Priority is Priority.Flexible or Priority.Optional)
            .Where(x => x.Amount.Cents > 0)
            .ToList();

        Money pool = Money.Sum(trimmable.Select(t => t.Amount));

        var result = new List<Recommendation>(raw.Count);
        var cuts = new Dictionary<int, Money>();

        if (trimmable.Count > 0 && pool.Cents > 0)
        {
            Money toCut = excess > pool ? pool : excess;
            Money[] shares = toCut.Prorate([.. trimmable.Select(t => t.Amount.Cents)]);

            for (int i = 0; i < trimmable.Count; i++)
            {
                cuts[trimmable[i].Index] = shares[i];
            }
        }

        for (int i = 0; i < raw.Count; i++)
        {
            var r = raw[i];

            if (!cuts.TryGetValue(i, out Money cut) || cut.Cents == 0)
            {
                result.Add(new Recommendation(
                    r.Id,
                    r.Amount,
                    $"Media ponderada de {r.Periods} período(s) cerrados."));
                continue;
            }

            result.Add(new Recommendation(
                r.Id,
                r.Amount - cut,
                $"Media de {r.Periods} período(s), recortada para caber en el ingreso."));
        }

        return result;
    }
}
