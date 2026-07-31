using Margen.Domain;

namespace Margen.Budget;

/// <summary>Cuánto se puede gastar hoy sin comprometer lo que falta del ciclo.</summary>
public sealed record DailyAllowance(
    Money PerDay,
    int DaysRemaining,
    bool IsOverdrawn,
    Money Shortfall);

public static class DailyAllowanceCalculator
{
    /// <summary>
    /// Reparte el dinero seguro entre los días que quedan, hoy incluido.
    /// </summary>
    /// <remarks>
    /// La cifra es la **menor** de las partes de un reparto exacto, no el
    /// promedio redondeado. Gastar esa cantidad todos los días nunca supera el
    /// total; un promedio redondeado hacia arriba sí lo supera, por unos
    /// centavos al día que al cabo de un ciclo son pesos. Cuando la respuesta
    /// va a gobernar una decisión de gasto, se redondea en contra de quien
    /// pregunta.
    ///
    /// Con el dinero seguro en negativo la respuesta es cero, y eso no es la
    /// mentira que <see cref="Outcome{T}"/> existe para impedir: el desglose
    /// viaja al lado con <see cref="DailyAllowance.Shortfall"/>, así que la
    /// pantalla puede decir «ya te pasaste por tanto» en vez de «te quedan
    /// cero».
    /// </remarks>
    public static Outcome<DailyAllowance> For(Money safeToSpend, BudgetCycle cycle, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        // Nunca es cero: el último día del ciclo quedan 1 días, no 0.
        int daysRemaining = cycle.DaysRemainingFrom(today);

        if (safeToSpend.IsNegative)
        {
            return Outcome.Computed(new DailyAllowance(
                Money.Zero,
                daysRemaining,
                IsOverdrawn: true,
                Shortfall: -safeToSpend));
        }

        Money[] parts = safeToSpend.Split(daysRemaining);

        return Outcome.Computed(new DailyAllowance(
            parts[^1],
            daysRemaining,
            IsOverdrawn: false,
            Shortfall: Money.Zero));
    }
}
