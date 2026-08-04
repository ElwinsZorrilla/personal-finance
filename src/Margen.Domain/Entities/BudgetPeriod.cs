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

    /// <summary>
    /// Los días del mes en que se cobra, tal como estaban al abrir el período.
    /// </summary>
    /// <remarks>
    /// Se guarda **en el período y no en unos ajustes globales** por una razón
    /// concreta: si mañana cambian los días de cobro, los períodos ya cerrados
    /// tienen que seguir contando su propia historia. Un ajuste global reescribe
    /// el pasado, y la base histórica —que pondera los tres períodos
    /// anteriores— se calcularía sobre ciclos que nunca existieron.
    ///
    /// También es lo que permite abrir el período siguiente sin volver a
    /// preguntar: el que está abierto sabe con qué calendario nació.
    /// </remarks>
    public IReadOnlyList<int> PayDays { get; set; } = [];

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
