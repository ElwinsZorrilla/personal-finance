using System.Globalization;
using System.Text.RegularExpressions;
using Margen.Domain;

namespace Margen.Ingest;

/// <summary>
/// Lee las notificaciones del Banco de Reservas.
/// </summary>
/// <remarks>
/// **El asunto de Banreservas no dice absolutamente nada**: los seis correos de
/// muestra llevan el mismo, «Notificaciones Banreservas». Ni el tipo, ni el
/// resultado, ni el importe. Todo sale del cuerpo.
///
/// Es el tercer banco y el tercer reparto distinto: en el Popular el asunto
/// decide el tipo, en Qik decide poco y el estatus manda, y aquí el asunto no
/// sirve para nada. La lección de las tres es la misma —leer el cuerpo— pero
/// solo se ve teniendo los tres delante.
///
/// La forma de la tabla es la de Qik: etiqueta en una línea y valor en la
/// siguiente, así que se lee con <see cref="EmailText.ValueBelow"/>.
/// </remarks>
public sealed partial class BanreservasParser : IEmailParser
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    public string Name => "banreservas";

    public int Version => 1;

    public bool CanHandle(RawEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return email.Sender.Contains("banreservas.com", StringComparison.OrdinalIgnoreCase);
    }

    public ParseResult Parse(RawEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        string text = EmailText.Normalize(email.Body);

        // El estado, antes que nada. **Se exige que diga aprobado**: no hay
        // ninguna muestra de una transacción declinada de este banco, así que no
        // se sabe cómo la escribe. Aceptar todo lo que no diga «declinado» sería
        // apostar a que la palabra que use está en una lista que nadie ha visto,
        // y el precio de perder esa apuesta es un gasto que nunca ocurrió.
        string? state = EmailText.ValueBelow(text, "Estado", "Estatus");
        if (state is null)
        {
            return ParseResult.NeedsReview("No se encontró el estado de la transacción.");
        }

        if (!ApprovedPattern().IsMatch(state))
        {
            return ParseResult.NeedsReview($"El estado no es aprobado, sino «{state}».");
        }

        TxKind kind = KindOf(text);

        Money? amount = ReadAmount(text);
        if (amount is null)
        {
            return ParseResult.NeedsReview("No se encontró el monto.");
        }

        DateTime? occurredAt = ReadInstant(text);
        if (occurredAt is null)
        {
            return ParseResult.NeedsReview("No se encontró la fecha de la transacción.");
        }

        string? lastFour = ReadLastFour(text);
        if (lastFour is null)
        {
            return ParseResult.NeedsReview(
                "No se encontraron los cuatro dígitos de la tarjeta.");
        }

        string? merchant = EmailText.ValueBelow(text, "Comercio", "Establecimiento");
        if (string.IsNullOrWhiteSpace(merchant))
        {
            return ParseResult.NeedsReview("No se encontró el comercio.");
        }

        return ParseResult.Parsed(
            new ParsedTransaction(
                amount.Value,
                "DOP",
                occurredAt.Value,
                merchant.Trim(),
                kind,
                lastFour,
                EmailText.ValueBelow(text, "Número de aprobación", "Aprobación")));
    }

    /// <summary>
    /// Qué clase de movimiento es, según lo que dice la frase.
    /// </summary>
    /// <remarks>
    /// «Su tarjeta ESTANDAR ••1234 presenta **un retiro de cajero automatico**»
    /// o «presenta **un consumo**». Es lo único que distingue una cosa de la
    /// otra: el asunto es el mismo y la tabla también.
    ///
    /// Lo que no encaje se queda como compra, que es el caso conservador: una
    /// compra cuenta como gasto, y equivocarse por ahí no hace que aparezca
    /// dinero que no está.
    /// </remarks>
    private static TxKind KindOf(string text) =>
        WithdrawalPattern().IsMatch(text) ? TxKind.Withdrawal : TxKind.Purchase;

    /// <summary>
    /// El importe, que este banco escribe con el código de moneda.
    /// </summary>
    /// <remarks>
    /// `DOP 9,876.54`, no `RD$ 9,876.54`. Es la forma que se le escapó al
    /// redactor y dejó un importe real en el disco.
    /// </remarks>
    private static Money? ReadAmount(string text)
    {
        string? raw = EmailText.ValueBelow(text, "Monto", "Valor", "Importe");
        if (raw is null) return null;

        Match match = MoneyPattern().Match(raw);
        if (!match.Success) return null;

        string units = match.Groups["entero"].Value
            .Replace(",", string.Empty, StringComparison.Ordinal);

        if (!long.TryParse(units, NumberStyles.None, CultureInfo.InvariantCulture, out long whole))
        {
            return null;
        }

        long cents = 0;
        string fraction = match.Groups["cent"].Value;
        if (fraction.Length > 0
            && !long.TryParse(fraction, NumberStyles.None, CultureInfo.InvariantCulture, out cents))
        {
            return null;
        }

        try
        {
            return new Money(checked((whole * 100) + cents));
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// El instante de la transacción, en UTC.
    /// </summary>
    /// <remarks>
    /// **El reloj es de veinticuatro horas y el AM/PM es decorativo.** Las seis
    /// muestras traen `19:43 PM`, `18:40 PM`, `21:30 PM`, `09:40 AM`: ninguna de
    /// las tres primeras es una hora válida de doce, y el marcador se deduce de
    /// la hora en vez de definirla. Leerlo como formato de doce horas falla en
    /// la mitad de los correos; hacerle caso al marcador sobre una hora de
    /// veinticuatro sumaría doce a las tardes.
    ///
    /// Se comprobó contra la hora de recepción del correo en las seis: la
    /// transacción y el aviso coinciden al minuto una vez convertida la zona.
    ///
    /// **La fecha es `dd/MM/yyyy`**, al revés que Qik, que la escribe `MM-dd`.
    /// Es la quinta forma de escribir una fecha en este proyecto y **los dos
    /// bancos no se ponen de acuerdo en el orden**, que es justo por lo que
    /// ningún parser adivina el formato.
    /// </remarks>
    private static DateTime? ReadInstant(string text)
    {
        string? raw = EmailText.ValueBelow(text, "Fecha de transacción", "Fecha");
        if (raw is null) return null;

        Match match = InstantPattern().Match(raw);
        if (!match.Success) return null;

        string stamp = $"{match.Groups["fecha"].Value} {match.Groups["hora"].Value}";

        if (!DateTime.TryParseExact(
                stamp,
                "dd/MM/yyyy HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime local))
        {
            return null;
        }

        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);
    }

    /// <summary>
    /// Los cuatro dígitos de la tarjeta, que aquí llevan viñetas por máscara.
    /// </summary>
    /// <remarks>
    /// `••1234`. Cada banco elige su carácter: asteriscos el Popular, viñetas
    /// este.
    /// </remarks>
    private static string? ReadLastFour(string text)
    {
        Match match = MaskedCardPattern().Match(text);

        return match.Success ? match.Groups["cuatro"].Value : null;
    }

    [GeneratedRegex(@"aprobad|autorizad|exitos", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex ApprovedPattern();

    [GeneratedRegex(@"retiro|cajero|avance\s+de\s+efectivo", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex WithdrawalPattern();

    [GeneratedRegex(
        @"(?:DOP|RD\$?|\$)?\s*(?<entero>\d{1,3}(?:,\d{3})*|\d+)(?:\.(?<cent>\d{2}))?",
        RegexOptions.IgnoreCase, 2000)]
    private static partial Regex MoneyPattern();

    /// <summary>
    /// La fecha y la hora. El marcador de meridiano se captura para no estorbar
    /// y **no se usa**: ver <see cref="ReadInstant"/>.
    /// </summary>
    [GeneratedRegex(
        @"(?<fecha>\d{2}/\d{2}/\d{4})\s+(?<hora>\d{1,2}:\d{2})(?:\s*[AaPp]\.?[Mm]\.?)?",
        RegexOptions.None, 2000)]
    private static partial Regex InstantPattern();

    [GeneratedRegex(@"[*x•·]{1,}\s*(?<cuatro>\d{4})(?!\d)", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex MaskedCardPattern();
}
