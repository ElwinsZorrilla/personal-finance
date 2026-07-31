using System.Text;

namespace Margen.Domain;

/// <summary>
/// Lo que el dispositivo firma para probar que tiene la clave privada.
/// </summary>
/// <remarks>
/// Vive en el dominio y no en el servidor porque es un contrato entre tres
/// partes: quien firma en el iPhone, quien verifica en el API y quien escriba
/// la prueba. Si cada uno construye el mensaje por su cuenta, el día que uno
/// cambie un separador la firma deja de validar y el error dirá «firma
/// inválida», que es lo único que no fue.
/// </remarks>
public static class DeviceAuth
{
    /// <summary>
    /// Etiqueta de propósito y versión. Va delante para que una firma emitida
    /// aquí no pueda reutilizarse en otro protocolo que también firme con esta
    /// clave, y para que cambiar el formato mañana sea una versión nueva y no
    /// una ambigüedad.
    /// </summary>
    public const string Context = "margen-device-auth-v1";

    /// <summary>
    /// Bytes que se firman: contexto, dispositivo y reto, separados por
    /// caracteres que no pueden aparecer en ninguna de las tres partes.
    /// </summary>
    /// <remarks>
    /// El identificador del dispositivo entra en el mensaje a propósito. Sin
    /// él, una firma válida de un dispositivo sobre un reto podría presentarse
    /// como si fuera de otro; con él, la firma solo vale para quien dice ser.
    /// </remarks>
    public static byte[] BuildSigningPayload(Guid deviceId, string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        return Encoding.UTF8.GetBytes(
            $"{Context}\n{deviceId:D}\n{nonce}");
    }
}
