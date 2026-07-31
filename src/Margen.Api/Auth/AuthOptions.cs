namespace Margen.Api.Auth;

/// <summary>Ajustes de autenticación. Llegan por variables de entorno.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Código que hay que presentar para dar de alta un dispositivo. Llega por
    /// <c>Auth__EnrollmentCode</c>.
    /// </summary>
    /// <remarks>
    /// Vacío por defecto y a propósito. Sin código configurado el alta responde
    /// 503 y no registra nada: un valor por defecto aquí sería una puerta
    /// abierta con la llave puesta en cualquier despliegue donde a alguien se
    /// le olvide la variable.
    /// </remarks>
    public string EnrollmentCode { get; set; } = string.Empty;

    /// <summary>Vida del reto. Corta: una firma capturada caduca enseguida.</summary>
    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Vida del token de la app.</summary>
    public TimeSpan DeviceTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Vida del token del Atajo. Larga porque reemplazarlo obliga a editar la
    /// automatización a mano en el teléfono; lo que lo hace tolerable es que
    /// solo alcanza para registrar efectivo y que se puede revocar.
    /// </summary>
    public TimeSpan ShortcutTokenLifetime { get; set; } = TimeSpan.FromDays(365);
}
