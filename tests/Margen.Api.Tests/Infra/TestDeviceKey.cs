using System.Security.Cryptography;
using Margen.Domain;

namespace Margen.Api.Tests.Infra;

/// <summary>
/// El lado del iPhone: una clave P-256 que firma retos.
/// </summary>
/// <remarks>
/// Construye el mensaje con <see cref="DeviceAuth.BuildSigningPayload"/>, el
/// mismo método que usa el servidor para verificar. Si la prueba armara el
/// mensaje por su cuenta, comprobaría que dos implementaciones coinciden hoy y
/// dejaría de comprobar nada el día que una cambie.
/// </remarks>
public sealed class TestDeviceKey : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public string PublicKeySpkiBase64 =>
        Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    /// <summary>
    /// Firma en formato DER, que es el que produce <c>SecKeyCreateSignature</c>
    /// de iOS con <c>ecdsaSignatureMessageX962SHA256</c>.
    /// </summary>
    public string Sign(Guid deviceId, string nonce) =>
        Convert.ToBase64String(_key.SignData(
            DeviceAuth.BuildSigningPayload(deviceId, nonce),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));

    public void Dispose() => _key.Dispose();
}
