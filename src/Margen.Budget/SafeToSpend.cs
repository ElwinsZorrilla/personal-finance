using Margen.Domain;

namespace Margen.Budget;

/// <summary>
/// Qué aparta cada resta. Es un enum y no una cadena porque el motor devuelve
/// centavos y hechos: cómo se llama cada línea en pantalla lo decide el
/// cliente, que es quien conoce el idioma y el ancho de la columna.
/// </summary>
public enum DeductionKind
{
    /// <summary>Facturas y cuotas de este ciclo que aún no se han pagado.</summary>
    PendingObligations,

    /// <summary>Lo que hay que tener listo para el corte de las tarjetas.</summary>
    CardReserve,

    /// <summary>Ahorro que el usuario decidió no tocar.</summary>
    CommittedSavings,

    /// <summary>Colchón para lo imprevisto.</summary>
    SafetyFund,

    /// <summary>Dinero en la cuenta que el banco todavía no libera.</summary>
    Withholdings,
}

public sealed record Deduction(DeductionKind Kind, Money Amount);

/// <summary>
/// Todo lo que entra en la fórmula, ya agregado.
/// </summary>
/// <remarks>
/// El motor no busca estas cifras: las recibe. Quién las calcula a partir de la
/// base es la Fase 4. Esa separación es lo que permite probar la fórmula con
/// cuarenta combinaciones sin levantar nada.
/// </remarks>
public sealed record SafeToSpendInputs(
    Money Liquid,
    Money PendingObligations,
    Money CardReserve,
    Money CommittedSavings,
    Money SafetyFund,
    Money Withholdings);

/// <summary>
/// El resultado con su desglose.
/// </summary>
/// <remarks>
/// Se devuelve cada resta por separado, no solo el total. Un número sin
/// desglose es un número que el usuario no puede discutir, y cuando no lo puede
/// discutir deja de creerlo.
/// </remarks>
public sealed record SafeToSpendBreakdown(
    Money Liquid,
    IReadOnlyList<Deduction> Deductions,
    Money Total)
{
    /// <summary>
    /// El total salió negativo: ya se gastó más de lo que había.
    /// </summary>
    /// <remarks>
    /// El total se devuelve tal cual, negativo. Redondearlo a cero cambiaría
    /// «ya te pasaste por RD$3,000» por «no te queda nada», que son dos
    /// situaciones distintas y piden dos decisiones distintas.
    /// </remarks>
    public bool IsOverdrawn => Total.IsNegative;

    /// <summary>Cuánto se pasó. Cero cuando no se pasó.</summary>
    public Money Shortfall => IsOverdrawn ? -Total : Money.Zero;

    public Money TotalDeducted => Money.Sum(Deductions.Select(d => d.Amount));
}

public static class SafeToSpend
{
    /// <summary>
    /// <c>líquido − obligaciones − reserva de tarjetas − ahorro − fondo de
    /// seguridad − retenciones</c>.
    /// </summary>
    /// <remarks>
    /// Una resta negativa se rechaza en vez de tratarse como suma. «Obligaciones
    /// pendientes de −5,000» no significa nada, y si se dejara pasar sumaría al
    /// dinero seguro: el error más caro posible en esta pantalla es uno que
    /// hace la cifra mayor.
    /// </remarks>
    public static Outcome<SafeToSpendBreakdown> Compute(SafeToSpendInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var deductions = new List<Deduction>
        {
            new(DeductionKind.PendingObligations, inputs.PendingObligations),
            new(DeductionKind.CardReserve, inputs.CardReserve),
            new(DeductionKind.CommittedSavings, inputs.CommittedSavings),
            new(DeductionKind.SafetyFund, inputs.SafetyFund),
            new(DeductionKind.Withholdings, inputs.Withholdings),
        };

        foreach (Deduction deduction in deductions)
        {
            if (deduction.Amount.IsNegative)
            {
                return Outcome.Invalid<SafeToSpendBreakdown>(
                    $"La resta {deduction.Kind} es negativa: sumaría al dinero seguro.");
            }
        }

        Money total = inputs.Liquid - Money.Sum(deductions.Select(d => d.Amount));

        return Outcome.Computed(
            new SafeToSpendBreakdown(inputs.Liquid, deductions, total));
    }
}
