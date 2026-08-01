using System.Globalization;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Margen.Ingest;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Margen.Worker.Ingestion;

/// <summary>
/// Baja unos cuantos correos del banco, les quita lo personal y los deja en
/// <c>docs/muestras/</c> para poder escribir el parser de la Fase 7.
/// </summary>
/// <remarks>
/// Se ejecuta una vez, a mano, desde la máquina de quien tiene la contraseña:
///
/// <code>
/// dotnet run --project src/Margen.Worker -- --capturar-muestras
/// </code>
///
/// Tres propiedades que no se negocian, y por eso están escritas aquí:
///
/// **Abre el buzón en solo lectura.** `FolderAccess.ReadOnly` significa que no
/// marca nada como leído, no mueve nada y no borra nada. Un correo del banco
/// que esta herramienta marcara como leído sería un correo que el worker no
/// vuelve a procesar.
///
/// **No escribe una sola fila en la base.** No es la tubería de ingesta: es una
/// captura para escribir el parser.
///
/// **No guarda nada sin pasar por el redactor.** El archivo que se escribe es
/// el resultado de <see cref="Redactor"/>, nunca el original.
/// </remarks>
public static class SampleCapture
{
    /// <summary>
    /// Cuántos correos como mucho. Con más, la carpeta se llena de variantes
    /// del mismo formato y escribir el parser se vuelve arqueología.
    /// </summary>
    private const int MaxSamples = 40;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);

        var options = services.GetRequiredService<IOptions<MailboxOptions>>().Value;

        if (!options.IsConfigured)
        {
            Console.Error.WriteLine(
                "Falta configurar el buzón. Hacen falta Imap__Host, Imap__User, "
                + "Imap__Password e Imap__AllowedSenders.");
            Console.Error.WriteLine("Ver docs/gmail.md.");
            return 1;
        }

        string destino = ResolveOutputDirectory(args);
        Directory.CreateDirectory(destino);

        var redactor = new Redactor(
            RedactionSettings.Of(Environment.GetEnvironmentVariable("Muestras__DatosPersonales")));

        Console.WriteLine($"Conectando a {options.Host}:{options.Port} como {options.User}…");

        using var client = new ImapClient();

        await client.ConnectAsync(
            options.Host,
            options.Port,
            MailKit.Security.SecureSocketOptions.SslOnConnect).ConfigureAwait(false);

        try
        {
            await client.AuthenticateAsync(options.User, options.Password).ConfigureAwait(false);
        }
        catch (MailKit.Security.AuthenticationException)
        {
            // El fallo más frecuente con Gmail, con mucha diferencia.
            Console.Error.WriteLine(
                "Gmail rechazó la contraseña. Con verificación en dos pasos activa, "
                + "la contraseña normal no sirve para IMAP: hace falta una "
                + "contraseña de aplicación. Ver docs/gmail.md.");
            return 1;
        }

        // SOLO LECTURA. No marca leídos, no mueve, no borra.
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly).ConfigureAwait(false);

        var escritos = new List<string>();
        int vistos = 0;

        foreach (string sender in options.Senders)
        {
            IList<UniqueId> encontrados = await client.Inbox
                .SearchAsync(SearchQuery.FromContains(sender))
                .ConfigureAwait(false);

            Console.WriteLine($"  {sender}: {encontrados.Count} correo(s).");

            // Del más reciente hacia atrás: las plantillas cambian y la que
            // importa es la de ahora.
            foreach (UniqueId uid in encontrados.Reverse())
            {
                if (escritos.Count >= MaxSamples) break;

                MimeMessage message = await client.Inbox.GetMessageAsync(uid)
                    .ConfigureAwait(false);

                vistos++;

                string cuerpo = message.TextBody ?? message.HtmlBody ?? string.Empty;
                if (string.IsNullOrWhiteSpace(cuerpo)) continue;

                string limpio = redactor.Redact(cuerpo);
                string asunto = redactor.Redact(message.Subject ?? string.Empty);

                string nombre = FileNameFor(message, escritos);
                string ruta = Path.Combine(destino, nombre);

                await File.WriteAllTextAsync(
                    ruta,
                    Compose(message, sender, asunto, limpio),
                    new UTF8Encoding(false)).ConfigureAwait(false);

                escritos.Add(nombre);
            }
        }

        await client.DisconnectAsync(true).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"Mirados {vistos}, escritos {escritos.Count} en {destino}");
        Console.WriteLine();
        Console.WriteLine("LÉELOS ANTES DE HACER COMMIT. El redactor cubre lo que sabe");
        Console.WriteLine("reconocer —correos, tarjetas, montos, referencias, cadenas");
        Console.WriteLine("largas de dígitos— y tu banco puede poner algo que no previó.");

        return 0;
    }

    /// <summary>
    /// El archivo lleva una cabecera con lo que el parser necesita saber
    /// —remitente y asunto— y el cuerpo debajo, separado por una línea.
    /// </summary>
    /// <remarks>
    /// El remitente se conserva sin redactar **a propósito**: es la dirección
    /// del banco, no un dato personal, y es lo que el parser usa para decidir
    /// si un correo es suyo. La fecha del correo se conserva por la misma
    /// razón: el formato de la fecha es justo lo que hay que aprender a leer.
    /// </remarks>
    private static string Compose(
        MimeMessage message,
        string sender,
        string subject,
        string body)
    {
        var builder = new StringBuilder();

        builder.AppendLine(CultureInfo.InvariantCulture, $"Remitente: {sender}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Asunto: {subject}");
        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"Fecha del correo: {message.Date:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine("---");
        builder.Append(body);

        return builder.ToString();
    }

    /// <summary>
    /// Un nombre por asunto, para que los seis tipos queden separados sin que
    /// nadie los ordene a mano.
    /// </summary>
    private static string FileNameFor(MimeMessage message, List<string> yaEscritos)
    {
        string subject = (message.Subject ?? "sin-asunto").ToLowerInvariant();

        string tipo = subject switch
        {
            var s when s.Contains("rechaz", StringComparison.Ordinal)
                || s.Contains("declin", StringComparison.Ordinal) => "compra-rechazada",
            var s when s.Contains("devoluc", StringComparison.Ordinal)
                || s.Contains("revers", StringComparison.Ordinal) => "devolucion",
            var s when s.Contains("retiro", StringComparison.Ordinal)
                || s.Contains("cajero", StringComparison.Ordinal) => "retiro",
            var s when s.Contains("pago", StringComparison.Ordinal) => "pago-tarjeta",
            var s when s.Contains("transferenc", StringComparison.Ordinal) => "transferencia",
            var s when s.Contains("compra", StringComparison.Ordinal)
                || s.Contains("consumo", StringComparison.Ordinal) => "compra-aprobada",
            _ => "sin-clasificar",
        };

        int n = 1;
        string nombre = $"{tipo}.txt";

        while (yaEscritos.Contains(nombre))
        {
            n++;
            nombre = $"{tipo}-{n}.txt";
        }

        return nombre;
    }

    private static string ResolveOutputDirectory(string[] args)
    {
        int i = Array.IndexOf(args, "--destino");
        if (i >= 0 && i + 1 < args.Length) return args[i + 1];

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Margen.slnx"))
                || File.Exists(Path.Combine(directory.FullName, "Margen.sln")))
            {
                return Path.Combine(directory.FullName, "docs", "muestras");
            }

            directory = directory.Parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "muestras");
    }
}
