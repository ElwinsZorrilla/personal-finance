namespace Margen.Domain.Entities;

/// <summary>
/// Regla que asigna categoría a un comercio. Una corrección del usuario crea
/// una de estas, y por eso la siguiente compra en el mismo sitio no vuelve a
/// preguntarle a nadie.
/// </summary>
public class MerchantRule
{
    public Guid Id { get; set; }

    /// <summary>Patrón contra el que se compara el comercio normalizado.</summary>
    public required string Pattern { get; set; }

    public MatchKind MatchKind { get; set; } = MatchKind.Exact;

    public Guid CategoryId { get; set; }

    public Category? Category { get; set; }

    /// <summary>
    /// Prioridad de la regla; mayor gana. Una regla creada por el usuario nace
    /// por encima de una deducida del historial: la corrección explícita de una
    /// persona pesa más que una estadística.
    /// </summary>
    public int Weight { get; set; }

    /// <summary>La creó el usuario al corregir, no un proceso automático.</summary>
    public bool IsUserDefined { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Cuántas veces se aplicó. Sirve para encontrar reglas muertas.</summary>
    public long TimesApplied { get; set; }

    public DateTime? LastAppliedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
