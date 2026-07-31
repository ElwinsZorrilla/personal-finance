namespace Margen.Domain.Entities;

/// <summary>Una cuenta bancaria, una tarjeta o el efectivo en mano.</summary>
public class Account
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// Últimos cuatro dígitos. Es lo único que trae el correo del banco para
    /// identificar la cuenta, y lo único que hace falta guardar: el número
    /// completo no aporta nada aquí y sí sería un dato que proteger.
    /// </summary>
    public required string LastFour { get; set; }

    public AccountKind Kind { get; set; }

    public Money Balance { get; set; }

    /// <summary>Límite de crédito. Nulo en cuentas que no son de crédito.</summary>
    public Money? CreditLimit { get; set; }

    public string Currency { get; set; } = "DOP";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];
}
