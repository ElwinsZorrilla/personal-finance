using Margen.Domain;

namespace Margen.Budget;

/// <summary>
/// Un movimiento visto por el motor.
/// </summary>
/// <remarks>
/// No es la entidad de EF Core. El motor recibe lo que necesita para sumar y
/// nada más: sin claves foráneas, sin colecciones de navegación, sin fechas en
/// UTC. <see cref="OccurredOn"/> ya viene convertido a hora de Santo Domingo
/// por quien llama, una sola vez, y por eso el motor nunca toca una zona
/// horaria.
/// </remarks>
public sealed record LedgerEntry(
    Guid TransactionId,
    Guid? CategoryId,
    Money Amount,
    TxKind Kind,
    TxStatus Status,
    DateOnly OccurredOn,
    Guid? RefundsTransactionId = null)
{
    /// <summary>
    /// Aporte con signo al gasto. Réplica de <c>Transaction.SpendingEffect</c>,
    /// que es la definición que manda.
    /// </summary>
    public Money SpendingEffect
    {
        get
        {
            bool counts = Status is not (TxStatus.Rejected or TxStatus.Duplicate)
                && Kind is not (TxKind.Transfer or TxKind.Payment);

            if (!counts)
            {
                return Money.Zero;
            }

            return Kind == TxKind.Refund ? -Amount : Amount;
        }
    }
}

/// <summary>Gasto de un ciclo, en total y por categoría.</summary>
public sealed record SpendingSummary(
    Money Total,
    IReadOnlyDictionary<Guid, Money> ByCategory,
    Money Uncategorized);

public static class SpendingLedger
{
    /// <summary>
    /// Suma el gasto del ciclo.
    /// </summary>
    /// <remarks>
    /// Dos reglas que no son evidentes y que son criterio de esta fase:
    ///
    /// **Una devolución resta de la categoría del movimiento original**, no de
    /// la suya. El banco no siempre categoriza la devolución igual que la
    /// compra, y si se restara de donde caiga, la categoría original quedaría
    /// inflada para siempre y otra quedaría en negativo.
    ///
    /// **Un pago de tarjeta no es gasto.** Mueve saldo entre cuentas propias.
    /// Contarlo duplicaría el gasto: una vez al comprar con la tarjeta y otra
    /// al pagarla.
    /// </remarks>
    public static Outcome<SpendingSummary> Summarize(
        BudgetCycle cycle,
        IReadOnlyCollection<LedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        ArgumentNullException.ThrowIfNull(entries);

        // El índice se construye sobre *todos* los movimientos, no solo los del
        // ciclo: una devolución de este mes puede corresponder a una compra del
        // anterior, y aun así tiene que descontarse de la categoría de aquella
        // compra.
        var categoryOfTransaction = new Dictionary<Guid, Guid?>(entries.Count);

        foreach (LedgerEntry entry in entries)
        {
            // Dos filas con el mismo identificador significan que quien llamó
            // trajo el mismo movimiento dos veces —una consulta con un join que
            // multiplica filas es la forma más común de que ocurra—. Sumarlo
            // dos veces inflaría el gasto sin que nada avisara, y descartarlo en
            // silencio taparía el defecto de quien llama. Se rechaza la entrada
            // entera: es dato contradictorio, no un caso de borde.
            if (!categoryOfTransaction.TryAdd(entry.TransactionId, entry.CategoryId))
            {
                return Outcome.Invalid<SpendingSummary>(
                    $"El movimiento {entry.TransactionId} viene repetido en la lista.");
            }
        }

        var byCategory = new Dictionary<Guid, Money>();
        Money uncategorized = Money.Zero;
        Money total = Money.Zero;

        foreach (LedgerEntry entry in entries)
        {
            if (!cycle.Contains(entry.OccurredOn))
            {
                continue;
            }

            Money effect = entry.SpendingEffect;
            if (effect.IsZero)
            {
                continue;
            }

            total += effect;

            Guid? category = ResolveCategory(entry, categoryOfTransaction);

            if (category is null)
            {
                uncategorized += effect;
                continue;
            }

            byCategory[category.Value] = byCategory.TryGetValue(category.Value, out Money running)
                ? running + effect
                : effect;
        }

        return Outcome.Computed(new SpendingSummary(total, byCategory, uncategorized));
    }

    /// <summary>
    /// La categoría a la que se imputa el movimiento. Para una devolución es la
    /// de la compra que devuelve, si esa compra está en el conjunto.
    /// </summary>
    private static Guid? ResolveCategory(
        LedgerEntry entry,
        Dictionary<Guid, Guid?> categoryOfTransaction)
    {
        if (entry.Kind != TxKind.Refund || entry.RefundsTransactionId is null)
        {
            return entry.CategoryId;
        }

        // Si la compra original no está en el conjunto, se cae a la categoría
        // de la propia devolución. No se descarta el movimiento: el dinero
        // volvió y el total tiene que reflejarlo aunque no se sepa de dónde.
        return categoryOfTransaction.TryGetValue(entry.RefundsTransactionId.Value, out Guid? original)
            ? original
            : entry.CategoryId;
    }
}
