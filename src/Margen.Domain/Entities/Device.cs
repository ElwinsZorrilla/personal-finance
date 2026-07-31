namespace Margen.Domain.Entities;

/// <summary>
/// Un iPhone dado de alta. Se identifica por su clave pública; la privada nunca
/// sale del Enclave Seguro del teléfono, así que no hay contraseña que robar
/// del servidor ni que el usuario pueda repetir en otro sitio.
/// </summary>
public class Device
{
    public Guid Id { get; set; }

    /// <summary>Nombre legible: «iPhone de Elwin». Solo para poder revocarlo a ojo.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Clave pública en formato SubjectPublicKeyInfo DER, codificada en Base64.
    /// La curva es P-256 porque es la única que genera el Enclave Seguro de
    /// iOS. Una clave fuera del Enclave es una clave que viaja en el respaldo
    /// del teléfono.
    /// </summary>
    public required string PublicKeySpki { get; set; }

    /// <summary>
    /// SHA-256 de la clave pública, en hexadecimal. Índice único: impide dar de
    /// alta dos veces el mismo teléfono y sirve para reconocerlo en un registro
    /// sin volcar la clave entera.
    /// </summary>
    public required string PublicKeyFingerprint { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastSeenAt { get; set; }

    /// <summary>
    /// Revocado. No se borra la fila: los tokens y los movimientos que apuntan
    /// a este dispositivo tienen que seguir teniendo a quién apuntar.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null;

    public ICollection<DeviceChallenge> Challenges { get; set; } = [];

    public ICollection<AccessToken> Tokens { get; set; } = [];
}
