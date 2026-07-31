namespace Margen.Worker;

/// <summary>
/// Lector del buzón de notificaciones del banco.
/// </summary>
/// <remarks>
/// Esqueleto. El compose de la Fase 1 ya declara este servicio y ya le pasa las
/// variables de IMAP, así que el proyecto existe desde ahora: un servicio
/// nombrado en el stack que no existe en la solución es una discrepancia que se
/// descubre en el primer despliegue.
///
/// El trabajo —conectar por IMAP, filtrar por remitente, guardar el correo con
/// su identificador de mensaje y su huella— es la Fase 6.
/// </remarks>
public sealed partial class MailboxWorker(ILogger<MailboxWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Started(logger);
        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Worker en pie. La lectura del buzón se implementa en la Fase 6.")]
    private static partial void Started(ILogger logger);
}
