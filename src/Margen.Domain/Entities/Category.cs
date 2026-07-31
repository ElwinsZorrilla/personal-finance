namespace Margen.Domain.Entities;

/// <summary>Categoría de gasto. Su prioridad decide qué puede recortarse.</summary>
public class Category
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public Priority Priority { get; set; } = Priority.Flexible;

    /// <summary>
    /// Nombre del icono en el sistema visual. El servidor guarda el nombre; el
    /// cliente decide cómo se dibuja.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Categoría del sistema: no se puede borrar porque hay lógica que la
    /// nombra. «Sin clasificar» es una de ellas.
    /// </summary>
    public bool IsSystem { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];

    public ICollection<CategoryBudget> Budgets { get; set; } = [];
}
