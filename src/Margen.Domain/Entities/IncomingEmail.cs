namespace Margen.Domain.Entities;

/// <summary>
/// Correo descargado del buzón. Se guarda antes de intentar interpretarlo, para
/// que un parser que falla no pierda el original y se pueda reprocesar.
/// </summary>
public class IncomingEmail
{
    public Guid Id { get; set; }

    /// <summary>
    /// El <c>Message-ID</c> de la cabecera. Índice único: es la primera línea
    /// de defensa contra procesar dos veces el mismo correo. Reprocesar el
    /// buzón entero no puede crear un movimiento nuevo, y esto es lo que lo
    /// impide.
    /// </summary>
    public required string MessageId { get; set; }

    /// <summary>
    /// SHA-256 del cuerpo normalizado. Índice único. Cubre el caso del servidor
    /// de correo que reescribe el <c>Message-ID</c> al reenviar: el
    /// identificador cambia pero el cuerpo es el mismo.
    /// </summary>
    public required string BodyHash { get; set; }

    public required string Sender { get; set; }

    public required string Subject { get; set; }

    /// <summary>Cuerpo original. Sin él no hay reproceso posible.</summary>
    public required string Body { get; set; }

    /// <summary>Fecha de la cabecera del correo, en UTC.</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>Cuándo lo descargó el worker, en UTC.</summary>
    public DateTime FetchedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public EmailStatus Status { get; set; } = EmailStatus.Received;

    /// <summary>Qué parser lo reconoció. Nulo si ninguno.</summary>
    public string? ParserName { get; set; }

    /// <summary>
    /// Versión del parser que lo procesó. Permite encontrar qué correos hay que
    /// reprocesar cuando el parser mejora.
    /// </summary>
    public int? ParserVersion { get; set; }

    /// <summary>Motivo del fallo. Se guarda para poder arreglar el parser.</summary>
    public string? FailureReason { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];
}
