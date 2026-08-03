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
    /// Cuántas muestras **por tipo de aviso**.
    /// </summary>
    /// <remarks>
    /// Por tipo y no en total, y ese es el cambio que importa. Con un tope
    /// global sobre los correos más recientes, la primera captura real trajo
    /// treinta y siete notificaciones de consumo y tres de retiro: faltaban las
    /// cuatro formas que el parser también tiene que saber leer, y estaban ahí,
    /// más atrás en el buzón.
    ///
    /// Seis por tipo alcanza para ver si el banco usa una plantilla o varias, y
    /// no llena la carpeta de copias de lo mismo.
    /// </remarks>
    private const int PerType = 6;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);

        var options = services.GetRequiredService<IOptions<MailboxOptions>>().Value;
        var config = services.GetRequiredService<IConfiguration>();

        if (!options.IsConfigured)
        {
            // Se dice cuál falta, no «falta configuración». Con cuatro
            // variables, «falta alguna» obliga a revisarlas todas.
            Console.Error.WriteLine("No se puede capturar: falta configuración del buzón.");
            Console.Error.WriteLine();

            Report("Imap__Host", options.Host);
            Report("Imap__User", options.User);
            Report("Imap__Password", options.Password, secret: true);
            Report("Imap__AllowedSenders", options.AllowedSenders);

            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "Ponlas en infra/.env —que está en .gitignore— o en el entorno.");
            Console.Error.WriteLine("Los pasos completos están en docs/gmail.md.");
            return 1;
        }

        string destino = ResolveOutputDirectory(args);
        Directory.CreateDirectory(destino);

        string? personales = config["Muestras:DatosPersonales"];
        var redactor = new Redactor(RedactionSettings.Of(personales));

        if (string.IsNullOrWhiteSpace(personales))
        {
            // No se aborta: las reglas por patrón siguen funcionando sin esto.
            // Pero conviene decirlo, porque el nombre es justo lo que ninguna
            // expresión regular puede adivinar.
            Console.WriteLine(
                "AVISO: no se dio Muestras__DatosPersonales. Tu nombre no se "
                + "quitará de las muestras salvo que coincida con otra regla.");
            Console.WriteLine();
        }

        Console.WriteLine($"Conectando a {options.Host}:{options.Port} como {options.User}…");

        using var client = new ImapClient();

        // Los tres fallos de conexión se traducen a una frase cada uno. Una
        // traza de MailKit dice dónde reventó, no qué hacer.
        try
        {
            await client.ConnectAsync(
                options.Host,
                options.Port,
                MailKit.Security.SecureSocketOptions.SslOnConnect).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException
            or IOException
            or MailKit.Security.SslHandshakeException)
        {
            Console.Error.WriteLine($"No se pudo conectar a {options.Host}:{options.Port}.");
            Console.Error.WriteLine($"  {ex.Message}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Comprueba el host y el puerto, y que IMAP esté activado");
            Console.Error.WriteLine("en la configuración de Gmail. Ver docs/gmail.md.");
            return 1;
        }

        try
        {
            await client.AuthenticateAsync(options.User, options.Password).ConfigureAwait(false);
        }
        catch (MailKit.Security.AuthenticationException)
        {
            // El fallo más frecuente con Gmail, con mucha diferencia.
            Console.Error.WriteLine("Gmail rechazó la contraseña.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Con verificación en dos pasos activa, la contraseña normal");
            Console.Error.WriteLine("no sirve para IMAP: hace falta una contraseña de");
            Console.Error.WriteLine("aplicación, y va sin los espacios con que Google la enseña.");
            Console.Error.WriteLine("Ver docs/gmail.md.");
            return 1;
        }

        // SOLO LECTURA. No marca leídos, no mueve, no borra.
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly).ConfigureAwait(false);

        var escritos = new List<string>();
        int vistos = 0;

        var porTipo = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string sender in options.Senders)
        {
            IList<UniqueId> encontrados = await client.Inbox
                .SearchAsync(SearchQuery.FromContains(sender))
                .ConfigureAwait(false);

            Console.WriteLine($"  {sender}: {encontrados.Count} correo(s).");

            if (encontrados.Count == 0) continue;

            // Se piden solo los sobres primero: traen el asunto, que es lo que
            // decide el tipo, y pesan una fracción del mensaje entero. Bajar
            // doscientos correos completos para quedarse con treinta es
            // esperar de más por lo que no se va a usar.
            IList<IMessageSummary> sobres = await client.Inbox
                .FetchAsync(encontrados, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId)
                .ConfigureAwait(false);

            // Del más reciente hacia atrás: las plantillas cambian y la que
            // importa es la de ahora.
            foreach (IMessageSummary sobre in sobres.Reverse())
            {
                string tipo = TypeOf(sobre.Envelope?.Subject);

                porTipo.TryGetValue(tipo, out int cuantos);
                if (cuantos >= PerType) continue;

                MimeMessage message = await client.Inbox.GetMessageAsync(sobre.UniqueId)
                    .ConfigureAwait(false);

                vistos++;

                string cuerpo = message.TextBody ?? message.HtmlBody ?? string.Empty;
                if (string.IsNullOrWhiteSpace(cuerpo)) continue;

                string limpio = redactor.Redact(cuerpo);
                string asunto = redactor.Redact(message.Subject ?? string.Empty);

                string nombre = FileNameFor(tipo, escritos);
                string ruta = Path.Combine(destino, nombre);

                await File.WriteAllTextAsync(
                    ruta,
                    Compose(message, sender, asunto, limpio),
                    new UTF8Encoding(false)).ConfigureAwait(false);

                escritos.Add(nombre);
                porTipo[tipo] = cuantos + 1;
            }
        }

        Console.WriteLine();
        Console.WriteLine("Por tipo:");
        foreach ((string tipo, int cuantos) in porTipo.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {tipo,-20} {cuantos}");
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
    /// Dice si una variable está puesta, sin enseñar su valor si es secreta.
    /// </summary>
    private static void Report(string name, string value, bool secret = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"  FALTA  {name}");
            return;
        }

        Console.Error.WriteLine(
            secret
                ? $"  puesta {name} ({value.Length} caracteres)"
                : $"  puesta {name} = {value}");
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
    /// El tipo de aviso, deducido del asunto.
    /// </summary>
    /// <remarks>
    /// Los términos salen de los asuntos reales del Banco Popular
    /// —«Notificación de Consumo», «Notificación de Retiro»— más las formas que
    /// usan otros bancos dominicanos. Un asunto que no encaje va a
    /// `sin-clasificar`, que sigue siendo una muestra útil: dice que hay una
    /// forma de aviso que nadie previó.
    /// </remarks>
    public static string TypeOf(string? subject)
    {
        string s = (subject ?? string.Empty).ToLowerInvariant();

        return s switch
        {
            _ when s.Contains("rechaz", StringComparison.Ordinal)
                || s.Contains("declin", StringComparison.Ordinal)
                || s.Contains("no aprobad", StringComparison.Ordinal) => "compra-rechazada",

            _ when s.Contains("devoluc", StringComparison.Ordinal)
                || s.Contains("revers", StringComparison.Ordinal)
                || s.Contains("reembols", StringComparison.Ordinal) => "devolucion",

            _ when s.Contains("retiro", StringComparison.Ordinal)
                || s.Contains("cajero", StringComparison.Ordinal) => "retiro",

            _ when s.Contains("transferenc", StringComparison.Ordinal) => "transferencia",

            _ when s.Contains("pago", StringComparison.Ordinal) => "pago-tarjeta",

            _ when s.Contains("consumo", StringComparison.Ordinal)
                || s.Contains("compra", StringComparison.Ordinal) => "compra-aprobada",

            _ => "sin-clasificar",
        };
    }

    private static string FileNameFor(string tipo, List<string> yaEscritos)
    {
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
