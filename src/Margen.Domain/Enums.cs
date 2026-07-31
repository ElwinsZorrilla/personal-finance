namespace Margen.Domain;

/// <summary>
/// Naturaleza del movimiento. Los valores coinciden con <c>TxKind</c> en el
/// cliente y se persisten como texto, no como número: renumerar un enum es una
/// migración que reetiqueta filas viejas sin avisar.
/// </summary>
public enum TxKind
{
    Purchase,
    Withdrawal,

    /// <summary>Pago de tarjeta. Mueve saldo entre cuentas propias y no es gasto.</summary>
    Payment,

    /// <summary>Devolución. Reduce el gasto de la categoría original.</summary>
    Refund,
    Transfer,
    Fee,

    /// <summary>Efectivo registrado a mano o por el Atajo de iOS.</summary>
    Cash,
}

public enum TxStatus
{
    Pending,
    Posted,

    /// <summary>
    /// Falta un dato crítico o la clasificación no alcanzó confianza. Es el
    /// estado al que se cae cuando algo no se pudo determinar: nunca se asume
    /// un valor por defecto.
    /// </summary>
    NeedsReview,
    Duplicate,
    Rejected,
    Reconciled,
}

public enum TxSource
{
    Email,
    Shortcut,
    Manual,
    Statement,
}

/// <summary>
/// Prioridad de una categoría. Determina qué puede recortar la redistribución
/// del motor de presupuesto y qué es intocable.
/// </summary>
public enum Priority
{
    /// <summary>Indispensable. La redistribución no lo toca nunca.</summary>
    Essential = 1,

    /// <summary>Importante. La redistribución no lo toca nunca.</summary>
    Important = 2,

    Flexible = 3,
    Optional = 4,
}

/// <summary>Estado de un correo entrante dentro de la tubería de ingesta.</summary>
public enum EmailStatus
{
    /// <summary>Descargado y guardado; ningún parser lo ha mirado todavía.</summary>
    Received,

    Parsed,

    /// <summary>Ningún parser lo reconoció. No genera movimiento.</summary>
    Unrecognized,

    /// <summary>Su huella coincide con la de un correo ya procesado.</summary>
    Duplicate,

    /// <summary>El parser falló con excepción. Se guarda para reprocesar.</summary>
    Failed,
}

public enum AlertKind
{
    UnparsedEmail,
    LowConfidence,
    PossibleDuplicate,
    UnusualAmount,
    OverBudget,
    SubscriptionChange,
    FundingRisk,
    MissingRecurring,
}

/// <summary>Cadencia de un pago recurrente.</summary>
public enum Cadence
{
    Weekly,
    Biweekly,
    Monthly,
    Bimonthly,
    Quarterly,
    Yearly,
}

public enum AccountKind
{
    Checking,
    Savings,
    Credit,

    /// <summary>Efectivo en mano. No tiene notificaciones del banco.</summary>
    Cash,
}

/// <summary>Cómo se compara el patrón de una regla con el texto del comercio.</summary>
public enum MatchKind
{
    /// <summary>Igualdad exacta sobre el nombre normalizado. Es la que gana.</summary>
    Exact,

    Contains,
    Regex,
}
