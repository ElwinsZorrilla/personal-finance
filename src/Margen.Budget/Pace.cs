using Margen.Domain;

namespace Margen.Budget;

/// <summary>Cómo va el ritmo de gasto contra lo que quedaba del ciclo.</summary>
/// <param name="Expected">Lo que se esperaría llevar gastado a estas alturas.</param>
/// <param name="Actual">Lo gastado de verdad.</param>
/// <param name="Deviation">Positivo = por encima del ritmo esperado.</param>
/// <param name="ProjectedClose">Dónde termina el ciclo si se sigue así.</param>
/// <param name="ProjectedDepletion">
/// Día en que el dinero se acaba al ritmo actual. Nulo cuando alcanza hasta el
/// cierre, que es el caso sano.
/// </param>
public sealed record PaceReading(
    Money Expected,
    Money Actual,
    Money Deviation,
    Money ProjectedClose,
    DateOnly? ProjectedDepletion)
{
    public bool IsAheadOfPace => Deviation.IsNegative;

    public bool RunsOutEarly => ProjectedDepletion is not null;
}

public static class Pace
{
    /// <summary>
    /// Compara lo gastado con lo que tocaría, y extrapola.
    /// </summary>
    /// <remarks>
    /// Todo el cálculo es entero. El esperado es
    /// <c>presupuesto × transcurridos / totales</c> en una sola operación con
    /// <see cref="Money.Scale"/>: convertir la fracción a <c>double</c> primero
    /// y multiplicar después mete un error de redondeo en la cifra que decide
    /// si la pantalla se pone ámbar.
    /// </remarks>
    public static Outcome<PaceReading> Read(
        Money budget,
        Money actualSpent,
        BudgetCycle cycle,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        if (budget.IsNegative)
        {
            return Outcome.Invalid<PaceReading>("Un presupuesto negativo no tiene ritmo que medir.");
        }

        (int elapsed, int total) = cycle.ProgressAt(today);

        Money expected = budget.Scale(elapsed, total);
        Money deviation = actualSpent - expected;

        // Proyección lineal: si en `elapsed` días se gastó `actualSpent`, en
        // `total` días se gastará esto. Es la extrapolación más simple posible
        // y es la honesta: cualquier modelo más fino fingiría saber cómo se
        // reparte el gasto dentro del mes.
        Money projectedClose = actualSpent.Scale(total, elapsed);

        DateOnly? depletion = ProjectDepletion(budget, actualSpent, cycle, today, elapsed);

        return Outcome.Computed(
            new PaceReading(expected, actualSpent, deviation, projectedClose, depletion));
    }

    /// <summary>
    /// El día en que el presupuesto se agota al ritmo actual, o nulo si aguanta
    /// hasta el cierre.
    /// </summary>
    private static DateOnly? ProjectDepletion(
        Money budget,
        Money actualSpent,
        BudgetCycle cycle,
        DateOnly today,
        int elapsed)
    {
        // Sin gasto no hay ritmo que proyectar, y una división por cero.
        if (actualSpent.Cents <= 0)
        {
            return null;
        }

        Money remaining = budget - actualSpent;

        if (remaining.Cents <= 0)
        {
            // Ya se agotó. El día es hoy, no una fecha pasada inventada a
            // partir de un ritmo que ya no describe nada.
            return today;
        }

        // Días que aguanta lo que queda al ritmo de hasta ahora:
        //   restante / (gastado / transcurridos) = restante × transcurridos / gastado
        // Se trunca hacia abajo a propósito: si aguanta 3.9 días, el cuarto ya
        // no se completa.
        Int128 daysLeft = (Int128)remaining.Cents * elapsed / actualSpent.Cents;

        if (daysLeft <= 0)
        {
            return today;
        }

        int daysUntilCycleEnd = cycle.End.DayNumber - today.DayNumber;

        if (daysLeft >= daysUntilCycleEnd)
        {
            // Aguanta hasta el cierre. Es el caso sano y se dice con un nulo,
            // no con la fecha de cierre: son cosas distintas.
            return null;
        }

        return today.AddDays((int)daysLeft);
    }
}
