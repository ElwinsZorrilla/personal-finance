namespace Margen.Api;

/// <summary>
/// Mensajes de registro, generados en tiempo de compilación.
/// </summary>
/// <remarks>
/// Con <c>[LoggerMessage]</c> la plantilla se compila una vez y la
/// comprobación de nivel ocurre antes de evaluar los argumentos. Escribirlos
/// aquí, juntos, también deja ver de un vistazo qué sale al registro: importa
/// porque ninguno de estos mensajes puede llevar un token, una clave ni una
/// cadena de conexión.
/// </remarks>
internal static partial class Logs
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Alta de dispositivo rechazada: falta Auth__EnrollmentCode en la configuración.")]
    public static partial void EnrollmentNotConfigured(ILogger logger);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Dispositivo dado de alta: {DeviceId} con huella {Fingerprint}.")]
    public static partial void DeviceRegistered(ILogger logger, Guid deviceId, string fingerprint);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Warning,
        Message = "Reto ya consumido para el dispositivo {DeviceId}.")]
    public static partial void ChallengeAlreadyConsumed(ILogger logger, Guid deviceId);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Firma inválida en el canje del dispositivo {DeviceId}.")]
    public static partial void InvalidSignature(ILogger logger, Guid deviceId);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Warning,
        Message = "Firma con formato ilegible.")]
    public static partial void MalformedSignature(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Warning,
        Message = "Clave pública almacenada ilegible.")]
    public static partial void UnreadableStoredKey(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Esquema al día tras {Attempt} intento(s).")]
    public static partial void MigrationsApplied(ILogger logger, int attempt);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Intento {Attempt} de {Max} de aplicar migraciones falló. "
            + "El servidor sigue respondiendo 503 en /health/ready.")]
    public static partial void MigrationAttemptFailed(
        ILogger logger,
        Exception exception,
        int attempt,
        int max);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Error,
        Message = "No se pudieron aplicar las migraciones tras {Max} intentos.")]
    public static partial void MigrationsGaveUp(ILogger logger, int max);
}
