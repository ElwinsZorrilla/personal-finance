namespace Margen.Domain.Entities;

/// <summary>Cuánto se asigna a una categoría dentro de un período.</summary>
public class CategoryBudget
{
    public Guid Id { get; set; }

    public Guid BudgetPeriodId { get; set; }

    public BudgetPeriod? BudgetPeriod { get; set; }

    public Guid CategoryId { get; set; }

    public Category? Category { get; set; }

    /// <summary>Lo asignado al abrir el período.</summary>
    public Money Allocated { get; set; }

    /// <summary>
    /// Ajuste acumulado de la redistribución. Se guarda aparte de
    /// <see cref="Allocated"/> para que se pueda ver qué se movió y por qué;
    /// sumarlo al asignado borraría el rastro.
    /// </summary>
    public Money Adjustment { get; set; }

    public Money Effective => Allocated + Adjustment;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
