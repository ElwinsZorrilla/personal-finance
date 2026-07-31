namespace Margen.Domain.Entities;

/// <summary>
/// Período presupuestario. No va del 1 al 30: va de un ingreso al siguiente,
/// porque ese es el ciclo real del dinero de una persona asalariada.
/// </summary>
public class BudgetPeriod
{
    public Guid Id { get; set; }

    /// <summary>
    /// Primer día del período, inclusive. Es <see cref="DateOnly"/> y no
    /// <see cref="DateTime"/> porque un período empieza un día, no en un
    /// instante: darle hora obligaría a elegir una y a defenderla.
    /// </summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Último día del período, inclusive.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>Ingreso que abre el período.</summary>
    public Money ExpectedIncome { get; set; }

    public Money ActualIncome { get; set; }

    /// <summary>Fondo de seguridad apartado en este período.</summary>
    public Money SafetyFund { get; set; }

    /// <summary>Ahorro comprometido en este período.</summary>
    public Money CommittedSavings { get; set; }

    /// <summary>
    /// Cerrado significa que ya no admite movimientos nuevos y que su resultado
    /// alimenta la base histórica del período siguiente.
    /// </summary>
    public bool IsClosed { get; set; }

    public DateTime? ClosedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<CategoryBudget> CategoryBudgets { get; set; } = [];
}
