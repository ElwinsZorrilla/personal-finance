using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Auth;

/// <summary>Emite, verifica y revoca tokens de acceso opacos.</summary>
public sealed class TokenService(MargenDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Treinta y dos bytes de entropía. Con menos, un token empieza a ser
    /// adivinable por fuerza bruta contra un endpoint sin límite de intentos.
    /// </summary>
    private const int TokenBytes = 32;

    /// <summary>
    /// Emite un token y devuelve el valor en claro. Es la única vez que existe:
    /// en la base solo queda su hash.
    /// </summary>
    public async Task<IssuedToken> IssueAsync(
        Device device,
        IReadOnlyList<string> scopes,
        string label,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Count == 0)
        {
            throw new ArgumentException(
                "Un token sin alcances no sirve para nada y parece que sirve para todo.",
                nameof(scopes));
        }

        string plaintext = GeneratePlaintext();
        DateTime now = clock.GetUtcNow().UtcDateTime;

        var token = new AccessToken
        {
            Id = Guid.CreateVersion7(),
            DeviceId = device.Id,
            TokenHash = Hash(plaintext),
            Scopes = string.Join(' ', scopes),
            Label = label,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        };

        db.AccessTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new IssuedToken(token.Id, plaintext, token.ExpiresAt, scopes);
    }

    /// <summary>
    /// Resuelve un token en claro. Devuelve nulo si no existe, si caducó, si lo
    /// revocaron o si el dispositivo dueño está revocado.
    /// </summary>
    public async Task<AccessToken?> ResolveAsync(
        string plaintext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return null;
        }

        string hash = Hash(plaintext);
        DateTime now = clock.GetUtcNow().UtcDateTime;

        AccessToken? token = await db.AccessTokens
            .Include(t => t.Device)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
            .ConfigureAwait(false);

        if (token is null || !token.IsUsableAt(now) || token.Device?.IsActive != true)
        {
            return null;
        }

        // La última vez que se usó se guarda con un día de resolución. Escribir
        // en cada petición convierte toda lectura autenticada en una escritura,
        // y el dato solo sirve para saber si un token quedó muerto.
        if (token.LastUsedAt is null || now - token.LastUsedAt.Value > TimeSpan.FromHours(24))
        {
            token.LastUsedAt = now;
            token.Device!.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return token;
    }

    /// <summary>
    /// Revoca un token del dispositivo indicado. Idempotente: revocar dos veces
    /// no es un error.
    /// </summary>
    /// <remarks>
    /// El dispositivo es parte de la condición y no una comprobación previa.
    /// Hoy hay un solo usuario, pero un endpoint que revoca por identificador
    /// suelto revoca el token de cualquiera en cuanto haya dos.
    /// </remarks>
    public async Task<bool> RevokeAsync(
        Guid deviceId,
        Guid tokenId,
        CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;

        int affected = await db.AccessTokens
            .Where(t => t.Id == tokenId && t.DeviceId == deviceId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        return affected > 0;
    }

    /// <summary>SHA-256 en hexadecimal minúsculo.</summary>
    public static string Hash(string plaintext) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));

    private static string GeneratePlaintext() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
}

/// <summary>
/// Un token recién emitido. El valor en claro solo existe aquí y en la
/// respuesta HTTP que lo entrega.
/// </summary>
public sealed record IssuedToken(
    Guid Id,
    string Plaintext,
    DateTime ExpiresAt,
    IReadOnlyList<string> Scopes);
