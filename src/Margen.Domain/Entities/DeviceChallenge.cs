namespace Margen.Domain.Entities;

/// <summary>
/// Reto de un solo uso. El dispositivo lo firma con su clave privada y canjea
/// la firma por un token.
/// </summary>
/// <remarks>
/// La defensa contra la repetición no es reconocer una firma ya vista: es que
/// el reto solo se pueda canjear una vez. <see cref="ConsumedAt"/> se escribe
/// con un <c>UPDATE ... WHERE ConsumedAt IS NULL</c> que tiene que afectar
/// exactamente una fila. Si afecta cero, alguien llegó antes y la respuesta es
/// 401. Comprobar y después escribir deja una ventana entre las dos
/// operaciones; esto no la deja.
/// </remarks>
public class DeviceChallenge
{
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }

    public Device? Device { get; set; }

    /// <summary>Treinta y dos bytes de un generador criptográfico, en Base64.</summary>
    public required string Nonce { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Caducidad, en UTC. Corta a propósito: un reto que vive horas es una
    /// firma que alguien puede capturar y usar más tarde.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }
}
