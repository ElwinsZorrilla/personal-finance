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

    /// <summary>
    /// Dinero que entra: un depósito en cajero o en ventanilla.
    /// </summary>
    /// <remarks>
    /// Apareció leyendo los correos reales del banco —«Depósito por ATM»— y no
    /// estaba en la lista. No es una devolución: una devolución deshace un
    /// gasto de una categoría, un depósito es dinero nuevo que no descuenta de
    /// ningún presupuesto.
    /// </remarks>
    Deposit,
}

/// <summary>
/// Hacia dónde va el dinero, en el sentido de todos los días.
/// </summary>
/// <remarks>
/// Es distinto de <see cref="TxKind"/>, que dice **qué operación** fue.
/// «Transferencia» no dice si el dinero se fue o solo cambió de bolsillo, y esa
/// es justo la pregunta que la pantalla necesita responder.
///
/// Se guarda en una columna en lugar de derivarse siempre del tipo, porque hay
/// un caso que el correo del banco no puede resolver: una transferencia enviada
/// a tu propia cuenta de ahorro no es un gasto, y una enviada a otra persona sí.
/// El valor nace deducido del tipo y el usuario lo corrige cuando haga falta.
/// </remarks>
public enum TxDirection
{
    /// <summary>Ingreso: el dinero entra y es tuyo para gastar.</summary>
    Inflow,

    /// <summary>Egreso: el dinero sale y no vuelve.</summary>
    Outflow,

    /// <summary>
    /// Traspaso: el dinero cambia de sitio dentro de lo tuyo. Ni ingreso ni
    /// gasto; contarlo sería contar dos veces la misma plata.
    /// </summary>
    Internal,
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
