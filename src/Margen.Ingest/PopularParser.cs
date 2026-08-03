using System.Globalization;
using System.Text.RegularExpressions;
using Margen.Domain;

namespace Margen.Ingest;

/// <summary>
/// Lee las notificaciones del Banco Popular Dominicano.
/// </summary>
/// <remarks>
/// Escrito contra los correos reales anonimizados de <c>docs/muestras/</c>. Lo
/// que sabe leer hoy son los cuatro avisos que aparecieron en el buzón:
///
/// | Asunto | Tipo |
/// |---|---|
/// | Notificación de Consumo | compra |
/// | Notificación de Retiro | retiro en cajero |
/// | Notificaciones Pagos al Instante transferencia enviada | transferencia |
/// | Depósito por ATM | depósito |
///
/// Faltan compra rechazada, devolución y pago de tarjeta: no había ninguno en
/// doscientos nueve correos. Cuando aparezcan se añaden aquí, se sube
/// <see cref="Version"/> y la herramienta de reproceso reinterpreta lo guardado
/// sin duplicar nada.
///
/// **El cuerpo no trae la hora**, solo el día. La hora se toma de la cabecera
/// del correo, y por qué eso es correcto está en <see cref="ResolveInstant"/>.
/// </remarks>
public sealed partial class PopularParser : IEmailParser
{
    public string Name => "banco-popular";

    /// <summary>
    /// Se sube al añadir un formato o corregir una extracción. La herramienta
    /// de reproceso busca los correos leídos con una versión anterior.
    /// </summary>
    public int Version => 1;

    /// <summary>La zona en la que escribe el banco. Es lo único que la sabe.</summary>
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    private const string Domain = "popularenlinea.com";

    public bool CanHandle(RawEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return email.Sender.Contains(Domain, StringComparison.OrdinalIgnoreCase);
    }

    public ParseResult Parse(RawEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        if (!CanHandle(email)) return ParseResult.NotMine();

        TxKind? kind = KindOf(email.Subject);
        if (kind is null)
        {
            // Fallo cerrado: un aviso que no se reconoce no se convierte en
            // compra por defecto. Va a revisión con el asunto, que es lo que
            // hace falta para añadirlo aquí.
            return ParseResult.NeedsReview($"Asunto no reconocido: «{email.Subject}».");
        }

        string text = EmailText.Normalize(email.Body);

        Money? amount = ReadAmount(text);
        if (amount is null)
        {
            return ParseResult.NeedsReview("No se encontró el monto.");
        }

        if (amount.Value.Cents <= 0)
        {
            return ParseResult.NeedsReview("El monto extraído no es positivo.");
        }

        DateOnly? day = ReadDay(text);
        if (day is null)
        {
            // No se cae al día del correo: un aviso que llega con retraso caería
            // en el período equivocado y descuadraría los dos.
            return ParseResult.NeedsReview("No se encontró la fecha del movimiento.");
        }

        string? lastFour = ReadLastFour(text);
        if (lastFour is null)
        {
            return ParseResult.NeedsReview(
                "No se encontraron los últimos cuatro dígitos de la cuenta.");
        }

        string? merchant = ReadMerchant(text, kind.Value);
        if (merchant is null)
        {
            return ParseResult.NeedsReview("No se encontró el comercio ni el destino.");
        }

        // «Consumo declinado» y parecidos: si el estatus dice que no se aprobó,
        // no hubo movimiento de dinero y crear uno inflaría el gasto.
        //
        // El estatus se lee de la **fila de valores**, no de la etiqueta: en la
        // tabla aplanada «Estatus» está en la fila de encabezados y no le sigue
        // ningún valor en la misma línea, así que buscar por etiqueta devolvía
        // nulo y la comprobación no llegaba a hacerse nunca.
        string? status = ReadStatus(text);
        if (status is not null && DeclinedPattern().IsMatch(status))
        {
            return ParseResult.NeedsReview(
                $"El banco reporta la transacción como «{status}».");
        }

        return ParseResult.Parsed(new ParsedTransaction(
            amount.Value,
            "DOP",
            ResolveInstant(day.Value, email.ReceivedAtUtc),
            merchant,
            kind.Value,
            lastFour,
            EmailText.ValueAfter(text, "Referencia", "Autorización", "No. Transacción")));
    }

    /// <summary>
    /// El instante del movimiento, combinando el día del cuerpo con la hora de
    /// la cabecera del correo.
    /// </summary>
    /// <remarks>
    /// El cuerpo del Popular trae `26/07/2026` y ninguna hora. La cabecera del
    /// correo sí la trae, y el banco avisa en cuanto ocurre la transacción.
    ///
    /// Si el día del cuerpo coincide con el día local del correo, la hora del
    /// correo es la del movimiento con un margen de segundos. Es lo que hace
    /// que una compra de las 11:30 de la noche se guarde a esa hora y no al
    /// mediodía, que es la diferencia entre caer en un período o en el
    /// siguiente.
    ///
    /// Si no coinciden —el correo llegó con retraso, o el banco lo envió de
    /// madrugada por una compra de la noche anterior— se usa el **mediodía**
    /// del día del cuerpo. El mediodía y no la medianoche: medianoche está a
    /// cuatro horas de la frontera del día en UTC y cualquier desajuste la
    /// empuja al día equivocado. El mediodía está a doce de las dos fronteras.
    /// </remarks>
    internal static DateTime ResolveInstant(DateOnly bodyDay, DateTime emailReceivedUtc)
    {
        DateTime emailLocal = TimeZoneInfo.ConvertTimeFromUtc(
            emailReceivedUtc.Kind == DateTimeKind.Utc
                ? emailReceivedUtc
                : emailReceivedUtc.ToUniversalTime(),
            Zone);

        TimeOnly time = DateOnly.FromDateTime(emailLocal) == bodyDay
            ? TimeOnly.FromDateTime(emailLocal)
            : new TimeOnly(12, 0);

        return TimeZoneInfo.ConvertTimeToUtc(
            bodyDay.ToDateTime(time, DateTimeKind.Unspecified),
            Zone);
    }

    private static TxKind? KindOf(string subject)
    {
        string s = subject ?? string.Empty;

        // El orden es el conservador: «Consumo rechazado» lleva las dos
        // palabras, y lo rechazado gana.
        if (Contains(s, "rechaz") || Contains(s, "declin") || Contains(s, "no aprobad"))
        {
            return null;
        }

        if (Contains(s, "devoluc") || Contains(s, "revers")) return TxKind.Refund;
        if (Contains(s, "depósit") || Contains(s, "deposit")) return TxKind.Deposit;
        if (Contains(s, "retiro") || Contains(s, "cajero")) return TxKind.Withdrawal;

        // «Pagos al Instante» es como el Popular llama a sus transferencias, y
        // lleva la palabra «pago»: sin mirarlo antes, caería en pago de tarjeta.
        if (Contains(s, "transferenc") || Contains(s, "al instante")) return TxKind.Transfer;

        if (Contains(s, "pago de tarjeta")) return TxKind.Payment;
        if (Contains(s, "consumo") || Contains(s, "compra")) return TxKind.Purchase;

        return null;
    }

    /// <summary>
    /// El monto. Acepta las tres formas que se vieron: `RD$11.11`, `RD$ 11.11`
    /// y `RD 11.11` sin el signo.
    /// </summary>
    private static Money? ReadAmount(string text)
    {
        Match match = AmountPattern().Match(text);
        if (!match.Success) return null;

        return ParseMoney(match.Groups["monto"].Value);
    }

    /// <summary>
    /// La fecha, en las tres formas que usa el banco.
    /// </summary>
    /// <remarks>
    /// `26/07/2026` en el texto plano, `12/6/2026` en el HTML de transferencia
    /// —sin cero delante— y `20260618` en el de depósito, ocho dígitos pegados.
    ///
    /// Que un mismo banco escriba la fecha de tres maneras en cuatro plantillas
    /// no es una anécdota: es el motivo por el que este parser se escribe contra
    /// correos reales y no contra un formato supuesto.
    /// </remarks>
    private static readonly string[] DayFormats =
    [
        "dd/MM/yyyy", "d/M/yyyy", "dd/M/yyyy", "d/MM/yyyy", "yyyyMMdd",
    ];

    private static DateOnly? ReadDay(string text)
    {
        foreach (Match match in DayPattern().Matches(text))
        {
            string raw = match.Groups["fecha"].Value;

            foreach (string format in DayFormats)
            {
                if (!DateOnly.TryParseExact(
                        raw,
                        format,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateOnly day))
                {
                    continue;
                }

                // Ocho dígitos seguidos pueden ser cualquier cosa. Se exige un
                // año creíble para no tomar por fecha un número de referencia
                // que casualmente empiece por veinte.
                if (day.Year is < 2000 or > 2100) continue;

                return day;
            }
        }

        return null;
    }

    /// <summary>
    /// Los últimos cuatro dígitos, en las dos formas que usa el banco:
    /// «terminada en 2074» en el texto plano y `******_0024` en el HTML.
    /// </summary>
    private static string? ReadLastFour(string text)
    {
        Match spelled = SpelledCardPattern().Match(text);
        if (spelled.Success) return spelled.Groups["cuenta"].Value;

        Match masked = MaskedAccountPattern().Match(text);
        return masked.Success ? masked.Groups["cuenta"].Value : null;
    }

    /// <summary>
    /// De dónde salió o a dónde fue el dinero.
    /// </summary>
    /// <remarks>
    /// Cada tipo de aviso lo pone en un sitio distinto y con otro nombre: la
    /// compra en «Comercio», el retiro en «Cajero Automatico», la transferencia
    /// en «Beneficiario» y el depósito en «Canal». No es un detalle cosmético:
    /// es la clave por la que se agrupa el historial de un comercio y por la que
    /// aplican las reglas de clasificación.
    ///
    /// En el texto plano la tabla llega aplanada y el valor viene **debajo** de
    /// su encabezado, no al lado, así que se busca por la fila de valores.
    /// </remarks>
    private static string? ReadMerchant(string text, TxKind kind)
    {
        string? labelled = kind switch
        {
            TxKind.Transfer => EmailText.ValueAfter(text, "Beneficiario", "Destinatario"),
            TxKind.Deposit => EmailText.ValueAfter(text, "Canal", "Sucursal"),
            _ => EmailText.ValueAfter(text, "Comercio", "Establecimiento"),
        };

        if (labelled is not null && !IsTableHeader(labelled)) return Clean(labelled);

        // El depósito tiene su propia tabla aplanada —monto, fecha sin
        // separadores y canal— y no lleva estatus, así que la fila de valores
        // general no la reconoce.
        if (kind == TxKind.Deposit)
        {
            Match deposit = DepositRowPattern().Match(text);
            if (deposit.Success)
            {
                string canal = Clean(deposit.Groups["canal"].Value);
                if (canal.Length > 0) return canal;
            }
        }

        // Tabla aplanada: la fila de valores va después de la de encabezados y
        // el nombre puede partirse en dos líneas.
        Match row = FlattenedRowPattern().Match(text);
        if (row.Success)
        {
            string candidate = Clean(row.Groups["comercio"].Value);
            if (candidate.Length > 0) return candidate;
        }

        return kind == TxKind.Withdrawal ? "CAJERO AUTOMATICO" : null;
    }

    /// <summary>
    /// El estatus que reporta el banco, de donde esté.
    /// </summary>
    private static string? ReadStatus(string text)
    {
        Match row = FlattenedRowPattern().Match(text);
        if (row.Success && row.Groups["estatus"].Success)
        {
            return row.Groups["estatus"].Value.Trim();
        }

        string? labelled = EmailText.ValueAfter(text, "Estatus", "Estado");

        return labelled is null || IsTableHeader(labelled) ? null : labelled;
    }

    /// <summary>
    /// El valor leído es en realidad la fila de encabezados.
    /// </summary>
    /// <remarks>
    /// Pasa con la tabla aplanada: «Comercio» aparece en la línea de
    /// encabezados —`Monto Moneda Fecha Comercio Estatus`— antes que en
    /// ninguna otra, así que buscar por etiqueta devuelve «Estatus».
    /// </remarks>
    private static bool IsTableHeader(string value) =>
        Contains(value, "Estatus") || Contains(value, "Moneda");

    private static string Clean(string value) =>
        value.Trim().Trim('|', '-', ':').Trim();

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// `2,450.75` a centavos, desde el entero y sin pasar por `decimal`.
    /// </summary>
    /// <remarks>
    /// Coma de miles y punto decimal, que es la convención dominicana y la que
    /// usa el banco. Un valor que no encaje devuelve nulo y el movimiento va a
    /// revisión: interpretar `1.234.567` como `1.23` es el defecto que ya se
    /// coló una vez.
    /// </remarks>
    internal static Money? ParseMoney(string raw)
    {
        string cleaned = raw.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        string[] parts = cleaned.Split('.');

        if (parts.Length > 2 || parts[0].Length == 0) return null;

        if (!long.TryParse(parts[0], CultureInfo.InvariantCulture, out long units)) return null;

        long hundredths = 0;
        if (parts.Length == 2)
        {
            string decimals = parts[1].PadRight(2, '0');
            if (decimals.Length > 2
                || !long.TryParse(decimals, CultureInfo.InvariantCulture, out hundredths))
            {
                return null;
            }
        }

        try
        {
            return new Money(checked((units * 100) + hundredths));
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    [GeneratedRegex(
        @"(?:RD\$?|US\$?)\s*(?<monto>\d[\d,]*(?:\.\d{1,2})?)",
        RegexOptions.IgnoreCase, 2000)]
    private static partial Regex AmountPattern();

    [GeneratedRegex(
        @"(?<fecha>\d{1,2}/\d{1,2}/\d{4}|(?<!\d)20\d{6}(?!\d))",
        RegexOptions.None, 2000)]
    private static partial Regex DayPattern();

    [GeneratedRegex(
        @"terminad[ao]s?\s+en\s+(?<cuenta>\d{4})",
        RegexOptions.IgnoreCase, 2000)]
    private static partial Regex SpelledCardPattern();

    [GeneratedRegex(
        @"[*x•]{2,}[\s_.\-]{0,2}(?<cuenta>\d{4})",
        RegexOptions.IgnoreCase, 2000)]
    private static partial Regex MaskedAccountPattern();

    /// <summary>
    /// La fila de valores de la tabla aplanada: monto, moneda, fecha y después
    /// el comercio, que puede seguir en la línea siguiente.
    /// </summary>
    [GeneratedRegex(
        @"(?:RD\$?|US\$?)\s*[\d,]+(?:\.\d{1,2})?\s*(?:Peso dominicano|D[oó]lar[^\d]{0,20})?\s*\d{1,2}/\d{1,2}/\d{4}\s*(?<comercio>[^\r\n]*(?:\r?\n[^\r\n]*?)?)\s*(?<estatus>Aprobad\w*|Declinad\w*|Rechazad\w*|Procesad\w*|Denegad\w*)",
        RegexOptions.IgnoreCase, 2000)]
    private static partial Regex FlattenedRowPattern();

    /// <summary>
    /// La fila de valores del depósito: monto, fecha pegada y canal.
    /// </summary>
    [GeneratedRegex(
        @"(?:RD\$?|US\$?)\s*[\d,]+(?:\.\d{1,2})?\s*(?<fecha>20\d{6})\s*(?<canal>[^

]+)",
        RegexOptions.IgnoreCase, 2000)]
    private static partial Regex DepositRowPattern();

    [GeneratedRegex(
        @"declinad|rechazad|no\s+aprobad|denegad",
        RegexOptions.IgnoreCase, 2000)]
    private static partial Regex DeclinedPattern();
}
