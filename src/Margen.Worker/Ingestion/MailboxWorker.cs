using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Margen.Domain;
using Margen.Infrastructure;
using Margen.Infrastructure.Classification;
using Margen.Ingest;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Margen.Worker.Ingestion;

/// <summary>
/// Lee el buzón de notificaciones del banco y mete lo que encuentra en la
/// tubería de ingesta.
/// </summary>
/// <remarks>
/// La lista blanca de remitentes se aplica **antes de leer el cuerpo**: un
/// correo de fuera de la lista ni se descarga entero ni se guarda. El buzón es
/// una dirección de correo, y una dirección de correo la conoce cualquiera.
/// </remarks>
public sealed partial class MailboxWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MailboxOptions> options,
    ILogger<MailboxWorker> logger) : BackgroundService
{
    private readonly MailboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured)
        {
            // Fallo cerrado. Sin buzón o sin lista blanca no se lee nada, y se
            // dice por qué: un worker en silencio parece uno que funciona.
            NotConfigured(logger);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int nuevos = await PollAsync(stoppingToken).ConfigureAwait(false);
                Polled(logger, nuevos);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Una tanda que falla no puede matar al worker:
            // el buzón sigue ahí y la siguiente vuelta lo intenta otra vez.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                PollFailed(logger, ex);
            }

            try
            {
                await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<int> PollAsync(CancellationToken cancellationToken)
    {
        using var client = new ImapClient();

        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            MailKit.Security.SecureSocketOptions.SslOnConnect,
            cancellationToken).ConfigureAwait(false);

        await client.AuthenticateAsync(_options.User, _options.Password, cancellationToken)
            .ConfigureAwait(false);

        await client.Inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken)
            .ConfigureAwait(false);

        IList<UniqueId> nuevos = await client.Inbox
            .SearchAsync(SearchQuery.NotSeen, cancellationToken)
            .ConfigureAwait(false);

        int procesados = 0;

        foreach (UniqueId uid in nuevos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MimeMessage message = await client.Inbox
                .GetMessageAsync(uid, cancellationToken)
                .ConfigureAwait(false);

            string sender = message.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty;

            if (!_options.Allows(sender))
            {
                // Ni se guarda ni se registra el contenido: solo que se
                // descartó y de quién.
                Rejected(logger, sender);

                // Se marca leído igual, para no volver a bajarlo en cada tanda.
                await MarkSeenAsync(client, uid, logger, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var raw = new RawEmail(
                message.MessageId ?? $"sin-id-{uid.Id}@{_options.Host}",
                sender,
                message.Subject ?? string.Empty,
                message.TextBody ?? message.HtmlBody ?? string.Empty,
                message.Date.UtcDateTime);

            using IServiceScope scope = scopeFactory.CreateScope();
            var ingestor = new EmailIngestor(
                scope.ServiceProvider.GetRequiredService<MargenDbContext>(),
                scope.ServiceProvider.GetRequiredService<ParserRegistry>(),
                scope.ServiceProvider.GetRequiredService<TimeProvider>(),
                detector: null,
                classifier: scope.ServiceProvider.GetRequiredService<TransactionClassifier>());

            IngestReport report = await ingestor.IngestAsync(raw, cancellationToken)
                .ConfigureAwait(false);

            Ingested(logger, report.Outcome);
            procesados++;

            // Se marca leído **después** de guardarlo. Al revés, una caída
            // entre las dos operaciones perdería el correo para siempre: el
            // banco no lo manda dos veces.
            await MarkSeenAsync(client, uid, logger, cancellationToken).ConfigureAwait(false);
        }

        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);

        return procesados;
    }

    /// <summary>
    /// Marca un correo como leído, y **no revienta la tanda si no puede**.
    /// </summary>
    /// <remarks>
    /// Gmail responde `NO System Error (Failure)` a algunos `STORE`, y ese
    /// rechazo tumbaba la tanda entera: la vuelta siguiente empezaba por el
    /// mismo correo, volvía a fallar, y el worker se quedaba en bucle sin
    /// avanzar nunca. Lo peor es que los correos **sí** se estaban guardando:
    /// el sistema parecía parado y estaba funcionando a medias.
    ///
    /// Marcar como leído es una comodidad, no la garantía de nada. Contra el
    /// reproceso hay tres defensas en la base desde la Fase 6 —identificador de
    /// mensaje, hash del cuerpo y huella del movimiento—, y las tres siguen en
    /// pie aunque el correo se vuelva a bajar mil veces.
    /// </remarks>
    private static async Task MarkSeenAsync(
        ImapClient client,
        UniqueId uid,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.Inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ImapCommandException e)
        {
            NotMarked(logger, uid.Id, e.Message);
        }
    }

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Error,
        Message = "El buzón no está configurado: faltan Imap__Host, Imap__User "
            + "o Imap__AllowedSenders. No se lee nada.")]
    private static partial void NotConfigured(ILogger logger);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Information,
        Message = "Tanda terminada: {Count} correo(s) procesado(s).")]
    private static partial void Polled(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Warning,
        Message = "La tanda falló. Se reintenta en la siguiente vuelta.")]
    private static partial void PollFailed(ILogger logger, Exception exception);

    // El remitente sí se registra —hace falta para añadirlo a la lista si es
    // legítimo—; el contenido, no.
    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Information,
        Message = "Correo descartado: {Sender} no está en la lista blanca.")]
    private static partial void Rejected(ILogger logger, string sender);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Information,
        Message = "Correo procesado con resultado {Outcome}.")]
    private static partial void Ingested(ILogger logger, IngestOutcome outcome);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Debug,
        Message = "No se pudo marcar como leído el correo {Uid}: {Reason}. "
            + "Se volverá a bajar y las defensas contra el duplicado lo descartarán.")]
    private static partial void NotMarked(ILogger logger, uint uid, string reason);
}
