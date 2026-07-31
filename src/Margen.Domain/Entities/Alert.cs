namespace Margen.Domain.Entities;

/// <summary>
/// Algo que pide atención: un correo sin interpretar, un cargo fuera de rango,
/// una suscripción que subió de precio. Es lo que llena la pantalla de Revisión.
/// </summary>
public class Alert
{
    public Guid Id { get; set; }

    public AlertKind Kind { get; set; }

    public required string Title { get; set; }

    public required string Detail { get; set; }

    /// <summary>Urgente sube la alerta al panel principal.</summary>
    public bool IsUrgent { get; set; }

    public Guid? TransactionId { get; set; }

    public Transaction? Transaction { get; set; }

    public Guid? IncomingEmailId { get; set; }

    public IncomingEmail? IncomingEmail { get; set; }

    public Guid? RecurringPaymentId { get; set; }

    public RecurringPayment? RecurringPayment { get; set; }

    /// <summary>
    /// Clave de deduplicación. Con índice único sobre las alertas sin resolver:
    /// una factura que no llegó genera una alerta, no una por cada vez que el
    /// worker mira el reloj.
    /// </summary>
    public required string DedupeKey { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public bool IsResolved => ResolvedAt is not null;
}
