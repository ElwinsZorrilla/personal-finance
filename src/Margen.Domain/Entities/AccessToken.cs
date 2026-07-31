namespace Margen.Domain.Entities;

/// <summary>
/// Token de acceso opaco.
/// </summary>
/// <remarks>
/// No es un JWT. Un JWT no se revoca: se espera a que caduque, y el token del
/// Atajo de iOS tiene que poder morir el día que el teléfono se pierda. Aquí
/// revocar es un <c>UPDATE</c>.
///
/// El valor en claro se devuelve una sola vez, al emitirlo. En la base vive
/// solo su SHA-256, igual que una contraseña: quien lea la base no obtiene
/// ningún token utilizable.
/// </remarks>
public class AccessToken
{
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }

    public Device? Device { get; set; }

    /// <summary>SHA-256 del token en hexadecimal. Índice único.</summary>
    public required string TokenHash { get; set; }

    /// <summary>
    /// Alcances separados por espacio. Un token del Atajo lleva
    /// <c>cash:create</c> y nada más.
    /// </summary>
    public required string Scopes { get; set; }

    /// <summary>
    /// Etiqueta para reconocerlo en la lista de revocación: «Atajo de iOS»,
    /// «app».
    /// </summary>
    public required string Label { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public bool IsUsableAt(DateTime utcNow) => RevokedAt is null && ExpiresAt > utcNow;
}
