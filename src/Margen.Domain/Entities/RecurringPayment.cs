namespace Margen.Domain.Entities;

/// <summary>
/// Compromiso que se repite: alquiler, suscripción, cuota. Es lo que se resta
/// del líquido antes de decir cuánto se puede gastar.
/// </summary>
public class RecurringPayment
{
    public Guid Id { get; set; }

    public required string Label { get; set; }

    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }

    public Guid? AccountId { get; set; }

    public Account? Account { get; set; }

    /// <summary>Monto esperado. Si el cargo real difiere, sale una alerta.</summary>
    public Money ExpectedAmount { get; set; }

    public Cadence Cadence { get; set; } = Cadence.Monthly;

    /// <summary>
    /// Día del mes en que se espera el cargo, de 1 a 31. Un pago de día 31 en
    /// un mes de 30 se corre al último día; esa decisión es del motor, no del
    /// esquema.
    /// </summary>
    public int DayOfCycle { get; set; }

    public DateOnly? NextDueDate { get; set; }

    public DateOnly? LastPaidDate { get; set; }

    /// <summary>
    /// Prioridad del compromiso. Con 1 o 2, la redistribución no puede
    /// recortarlo: el alquiler no es negociable a mitad de mes.
    /// </summary>
    public Priority Priority { get; set; } = Priority.Essential;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
