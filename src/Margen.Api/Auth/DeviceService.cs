using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Margen.Api.Auth;

/// <summary>Alta de dispositivos, emisión de retos y canje de firmas.</summary>
public sealed class DeviceService(
    MargenDbContext db,
    TokenService tokens,
    TimeProvider clock,
    IOptions<AuthOptions> options,
    ILogger<DeviceService> logger)
{
    /// <summary>Identificador de la curva P-256, la única del Enclave Seguro de iOS.</summary>
    private const string P256Oid = "1.2.840.10045.3.1.7";

    private readonly AuthOptions _options = options.Value;

    /// <summary>Da de alta un dispositivo previa presentación del código de alta.</summary>
    public async Task<DeviceRegistration> RegisterAsync(
        string enrollmentCode,
        string name,
        string publicKeySpkiBase64,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.EnrollmentCode))
        {
            // Fallo cerrado. Sin código configurado no se registra nada: un
            // servidor recién desplegado al que se le olvidó la variable no
            // puede quedar aceptando dispositivos de quien pase por ahí.
            Logs.EnrollmentNotConfigured(logger);
            return DeviceRegistration.NotConfigured;
        }

        if (!FixedTimeEquals(enrollmentCode, _options.EnrollmentCode))
        {
            return DeviceRegistration.WrongCode;
        }

        byte[] spki;
        try
        {
            spki = Convert.FromBase64String(publicKeySpkiBase64);
        }
        catch (FormatException)
        {
            return DeviceRegistration.BadKey("La clave pública no es Base64 válido.");
        }

        string? keyProblem = ValidatePublicKey(spki);
        if (keyProblem is not null)
        {
            return DeviceRegistration.BadKey(keyProblem);
        }

        string fingerprint = Convert.ToHexStringLower(SHA256.HashData(spki));
        DateTime now = clock.GetUtcNow().UtcDateTime;

        Device? existing = await db.Devices
            .FirstOrDefaultAsync(d => d.PublicKeyFingerprint == fingerprint, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // Volver a dar de alta el mismo teléfono devuelve el mismo
            // identificador en lugar de fallar: reinstalar la app no debería
            // dejar al usuario sin poder entrar. Lo que no hace es reactivar un
            // dispositivo revocado.
            if (!existing.IsActive)
            {
                return DeviceRegistration.Revoked;
            }

            return DeviceRegistration.Ok(existing.Id);
        }

        var device = new Device
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            PublicKeySpki = publicKeySpkiBase64,
            PublicKeyFingerprint = fingerprint,
            CreatedAt = now,
        };

        db.Devices.Add(device);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Paga la deuda m9 de CR-002. Dos altas del mismo teléfono a la vez
            // chocaban contra el índice único y salían como 500 con traza. La
            // comprobación de más arriba no basta: entre leer y escribir cabe
            // otra petición. Aquí se resuelve donde de verdad se decide, que es
            // en el índice, y el resultado es el mismo que el del camino sin
            // carrera.
            db.Entry(device).State = EntityState.Detached;

            Device? winner = await db.Devices
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.PublicKeyFingerprint == fingerprint, cancellationToken)
                .ConfigureAwait(false);

            // Si no hay ganador, el fallo no era la carrera y no se disfraza:
            // se deja subir y sale como 500, que es lo que es.
            if (winner is null)
            {
                throw;
            }

            return winner.IsActive
                ? DeviceRegistration.Ok(winner.Id)
                : DeviceRegistration.Revoked;
        }

        Logs.DeviceRegistered(logger, device.Id, fingerprint);

        return DeviceRegistration.Ok(device.Id);
    }

    /// <summary>Emite un reto de un solo uso para un dispositivo activo.</summary>
    public async Task<DeviceChallenge?> CreateChallengeAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;

        bool active = await db.Devices
            .AnyAsync(d => d.Id == deviceId && d.RevokedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (!active)
        {
            return null;
        }

        var challenge = new DeviceChallenge
        {
            Id = Guid.CreateVersion7(),
            DeviceId = deviceId,
            Nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)),
            CreatedAt = now,
            ExpiresAt = now.Add(_options.ChallengeLifetime),
        };

        db.DeviceChallenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return challenge;
    }

    /// <summary>
    /// Canjea la firma de un reto por un token de alcance total.
    /// </summary>
    /// <remarks>
    /// El reto se consume antes de verificar la firma, con un
    /// <c>UPDATE ... WHERE ConsumedAt IS NULL</c> que tiene que afectar
    /// exactamente una fila. Ese orden es deliberado: verificar primero y
    /// marcar después deja una ventana en la que dos peticiones con la misma
    /// firma pasan las dos la verificación antes de que ninguna haya escrito.
    /// Un intento fallido gasta el reto, y eso está bien: pedir otro cuesta una
    /// petición y evita que se pueda probar mil firmas contra el mismo.
    /// </remarks>
    public async Task<TokenRedemption> RedeemAsync(
        Guid deviceId,
        string nonce,
        string signatureBase64,
        CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;

        DeviceChallenge? challenge = await db.DeviceChallenges
            .FirstOrDefaultAsync(
                c => c.Nonce == nonce && c.DeviceId == deviceId,
                cancellationToken)
            .ConfigureAwait(false);

        if (challenge is null || challenge.ExpiresAt <= now)
        {
            return TokenRedemption.Denied;
        }

        int consumed = await db.DeviceChallenges
            .Where(c => c.Id == challenge.Id && c.ConsumedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.ConsumedAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        if (consumed != 1)
        {
            // Cero filas: alguien ya lo canjeó. Es exactamente el caso de la
            // firma repetida.
            Logs.ChallengeAlreadyConsumed(logger, deviceId);
            return TokenRedemption.Denied;
        }

        Device? device = await db.Devices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.RevokedAt == null, cancellationToken)
            .ConfigureAwait(false);

        if (device is null)
        {
            return TokenRedemption.Denied;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return TokenRedemption.Denied;
        }

        if (!VerifySignature(device, nonce, signature))
        {
            Logs.InvalidSignature(logger, deviceId);
            return TokenRedemption.Denied;
        }

        IssuedToken issued = await tokens.IssueAsync(
            device,
            [Scopes.Full],
            "app",
            _options.DeviceTokenLifetime,
            cancellationToken).ConfigureAwait(false);

        return TokenRedemption.Granted(issued);
    }

    private bool VerifySignature(Device device, string nonce, byte[] signature)
    {
        byte[] payload = DeviceAuth.BuildSigningPayload(device.Id, nonce);

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(
                Convert.FromBase64String(device.PublicKeySpki),
                out _);

            // **Se aceptan los dos formatos de firma que existen para ECDSA.**
            //
            // `SecKeyCreateSignature` de iOS produce una secuencia DER, y era
            // el único formato contemplado cuando la app iba a ser nativa.
            // ADR-001 la convirtió en PWA, y **WebCrypto produce el otro**: el
            // par de enteros concatenados en crudo, sin envoltura.
            //
            // Exigir solo DER hacía que ninguna firma del navegador validara
            // jamás, con el mensaje «firma inválida» —que es exactamente lo que
            // no era: la firma era correcta y estaba bien hecha, solo venía
            // envuelta de otra manera—.
            //
            // Aceptar los dos no debilita nada: la verificación criptográfica
            // es la misma y lo único que cambia es cómo se leen los dos enteros.
            return ecdsa.VerifyData(
                       payload,
                       signature,
                       HashAlgorithmName.SHA256,
                       DSASignatureFormat.Rfc3279DerSequence)
                   || ecdsa.VerifyData(
                       payload,
                       signature,
                       HashAlgorithmName.SHA256,
                       DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException ex)
        {
            // Una firma con forma inválida es una firma inválida, no un fallo
            // del servidor. Se registra y se deniega.
            Logs.MalformedSignature(logger, ex);
            return false;
        }
        catch (FormatException ex)
        {
            Logs.UnreadableStoredKey(logger, ex);
            return false;
        }
    }

    /// <summary>
    /// Devuelve el motivo por el que la clave no sirve, o nulo si sirve.
    /// </summary>
    private static string? ValidatePublicKey(byte[] spki)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out int read);

            if (read != spki.Length)
            {
                return "La clave pública lleva bytes de más al final.";
            }

            ECParameters parameters = ecdsa.ExportParameters(includePrivateParameters: false);

            // Se exige P-256 en el alta y no en la verificación: una clave de
            // otra curva entra una sola vez y se rechaza aquí, en vez de en
            // cada intento de entrar durante el resto de la vida del sistema.
            return IsP256(parameters.Curve, ecdsa.KeySize)
                ? null
                : "La clave tiene que ser de la curva P-256.";
        }
        catch (CryptographicException)
        {
            return "La clave pública no es un SubjectPublicKeyInfo de ECDSA.";
        }
    }

    /// <summary>
    /// Determina si la curva es P-256, sin depender de cómo la nombre el
    /// proveedor criptográfico de la plataforma.
    /// </summary>
    /// <remarks>
    /// Windows devuelve la curva con nombre y OpenSSL con identificador, y no
    /// siempre rellenan los dos campos. Mirar solo <c>Oid.Value</c> hacía que
    /// la comprobación pasara en Windows —donde corren las pruebas— y quedara
    /// reducida a «256 bits» en Linux, que es donde corre el servidor. Una
    /// comprobación que se comporta distinto en producción que en la prueba no
    /// es una comprobación.
    ///
    /// Sin nombre ni identificador reconocibles se rechaza: una curva de 256
    /// bits que no sea P-256 no la genera el Enclave Seguro, así que la clave
    /// vendría de otro sitio.
    /// </remarks>
    private static bool IsP256(ECCurve curve, int keySize)
    {
        if (keySize != 256 || !curve.IsNamed)
        {
            return false;
        }

        Oid oid = curve.Oid;

        if (!string.IsNullOrEmpty(oid.Value))
        {
            return oid.Value == P256Oid;
        }

        return oid.FriendlyName is not null
            && P256Names.Contains(oid.FriendlyName);
    }

    /// <summary>
    /// Los nombres con los que las distintas plataformas llaman a la misma
    /// curva. No es una lista de curvas admitidas: es una lista de alias de
    /// una sola.
    /// </summary>
    private static readonly HashSet<string> P256Names =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "nistP256",
            "ECDSA_P256",
            "prime256v1",
            "secp256r1",
        };

    /// <summary>
    /// Compara en tiempo constante. Una comparación normal de cadenas devuelve
    /// en cuanto encuentra una diferencia, y el tiempo que tarda dice cuántos
    /// caracteres iniciales eran correctos.
    /// </summary>
    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a),
            Encoding.UTF8.GetBytes(b));
}

public enum RegistrationOutcome
{
    Ok,
    NotConfigured,
    WrongCode,
    BadKey,
    Revoked,
}

public sealed record DeviceRegistration(RegistrationOutcome Outcome, Guid DeviceId, string? Problem)
{
    public static readonly DeviceRegistration NotConfigured =
        new(RegistrationOutcome.NotConfigured, Guid.Empty, null);

    public static readonly DeviceRegistration WrongCode =
        new(RegistrationOutcome.WrongCode, Guid.Empty, null);

    public static readonly DeviceRegistration Revoked =
        new(RegistrationOutcome.Revoked, Guid.Empty, null);

    public static DeviceRegistration Ok(Guid deviceId) =>
        new(RegistrationOutcome.Ok, deviceId, null);

    public static DeviceRegistration BadKey(string problem) =>
        new(RegistrationOutcome.BadKey, Guid.Empty, problem);
}

public sealed record TokenRedemption(bool Success, IssuedToken? Token)
{
    public static readonly TokenRedemption Denied = new(false, null);

    public static TokenRedemption Granted(IssuedToken token) => new(true, token);
}
