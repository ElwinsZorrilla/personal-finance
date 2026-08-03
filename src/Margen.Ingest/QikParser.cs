using System.Globalization;
using System.Text.RegularExpressions;
using Margen.Domain;

namespace Margen.Ingest;

/// <summary>
/// Lee las notificaciones de Qik Banco Digital Dominicano.
/// </summary>
/// <remarks>
/// **El asunto de Qik no dice si la transacción pasó.** Los tres asuntos que
/// manda —«Usaste tu tarjeta de débito Qik», «Se hizo una transacción con tu
/// tarjeta de débito Qik» y «Se intentó realizar una compra con tu tarjeta de
/// débito Qik»— aparecen tanto en compras aprobadas como en declinadas.
///
/// Eso lo cambia todo respecto del Popular, donde el asunto sí decidía el tipo.
/// Aquí **manda el cuerpo**: si hay una fila `Estatus` que dice `Declinado`, no
/// hubo gasto. Un parser que se fiara del asunto crearía movimientos por
/// compras que el banco rechazó e inflaría el gasto del período —que es
/// exactamente el defecto M4 de la Fase 7, en otro banco y por otro camino—.
///
/// La otra diferencia de forma: Qik escribe la tabla en filas de dos celdas, así
/// que al normalizar la etiqueta queda en una línea y el valor en la siguiente.
/// Se lee con <see cref="EmailText.ValueBelow"/>, que es un lector distinto del
/// del Popular y con su propio nombre.
/// </remarks>
public sealed partial class QikParser : IEmailParser
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    public string Name => "qik";

    public int Version => 1;

    public bool CanHandle(RawEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return email.Sender.Contains("qik.do", StringComparison.OrdinalIgnoreCase);
    }

    public ParseResult Parse(RawEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        string text = EmailText.Normalize(email.Body);

        // Primero el estatus, antes que nada. Si el banco lo declinó no hubo
        // gasto, y seguir leyendo solo sirve para crear un movimiento que no
        // existió.
        string? status = EmailText.ValueBelow(text, "Estatus");
        if (status is not null && RejectedPattern().IsMatch(status))
        {
            string reason = EmailText.ValueBelow(text, "Motivo") is string motivo
                ? $"El banco declinó la transacción: {motivo.ToLowerInvariant()}."
                : "El banco declinó la transacción.";

            return ParseResult.NeedsReview(reason);
        }

        Money? amount = ReadAmount(text);
        if (amount is null)
        {
            return ParseResult.NeedsReview("No se encontró el monto.");
        }

        DateTime? occurredAt = ReadInstant(text);
        if (occurredAt is null)
        {
            return ParseResult.NeedsReview("No se encontró la fecha y hora.");
        }

        string? lastFour = ReadLastFour(text);
        if (lastFour is null)
        {
            return ParseResult.NeedsReview("No se encontraron los cuatro dígitos de la tarjeta.");
        }

        string? merchant = ReadMerchant(text);
        if (merchant is null)
        {
            return ParseResult.NeedsReview("No se encontró el comercio.");
        }

        return ParseResult.Parsed(
            new ParsedTransaction(
                amount.Value,
                "DOP",
                occurredAt.Value,
                merchant,
                TxKind.Purchase,
                lastFour,
                Reference: null));
    }

    /// <summary>
    /// El monto de la fila «Monto», y si no, el de la frase.
    /// </summary>
    /// <remarks>
    /// Qik escribe `$ 1.11` y `RD$ 111.11` en el mismo correo y las dos son
    /// pesos: en la República Dominicana el precio con `$` está en pesos, y el
    /// `RD$` es la forma explícita de lo mismo. La fila «Balance Disponible»
    /// lleva otra cifra que **no** es la del movimiento, y por eso se busca la
    /// etiqueta en vez de la primera cifra del texto.
    /// </remarks>
    private static Money? ReadAmount(string text)
    {
        string? labelled = EmailText.ValueBelow(text, "Monto");
        if (labelled is not null && ParseMoney(labelled) is Money fromLabel) return fromLabel;

        Match sentence = SentenceAmountPattern().Match(text);

        return sentence.Success ? ParseMoney(sentence.Groups["monto"].Value) : null;
    }

    /// <summary>
    /// Convierte «RD$ 1,111.11» en centavos enteros.
    /// </summary>
    /// <remarks>
    /// Sin `decimal` y sin `double`: la parte entera por cien más los centavos.
    /// La coma separa millares y el punto los decimales, que es como escribe
    /// aquí todo el mundo.
    /// </remarks>
    private static Money? ParseMoney(string raw)
    {
        Match match = MoneyPattern().Match(raw);
        if (!match.Success) return null;

        string units = match.Groups["entero"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
        string cents = match.Groups["cent"].Value;

        if (!long.TryParse(units, NumberStyles.None, CultureInfo.InvariantCulture, out long whole))
        {
            return null;
        }

        long fraction = 0;
        if (cents.Length > 0
            && !long.TryParse(cents, NumberStyles.None, CultureInfo.InvariantCulture, out fraction))
        {
            return null;
        }

        try
        {
            return new Money(checked((whole * 100) + fraction));
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// El instante del movimiento, en UTC.
    /// </summary>
    /// <remarks>
    /// Qik sí manda la hora, al contrario que el Popular, y además dice la zona:
    /// `06-20-2026 11:57 AM (AST)`. AST es la hora de la República Dominicana,
    /// que no cambia en todo el año. Aun así se convierte con la zona por
    /// nombre y no restando cuatro horas a mano: el día que el país adopte
    /// horario de verano, restar a mano falla en silencio.
    ///
    /// **El mes va delante.** `06-20-2026` es el 20 de junio, no el 6 de
    /// diciembre. Se sabe porque el día pasa de doce en las muestras —`06-20` y
    /// `04-13`—, y se fija a propósito en vez de probar los dos órdenes: con
    /// `06-07-2026` los dos son válidos y elegir el que «parezca» correcto es
    /// meter movimientos en el período equivocado la mitad de las veces.
    ///
    /// Es la cuarta forma de escribir una fecha en este proyecto, y la primera
    /// que empieza por el mes.
    /// </remarks>
    private static DateTime? ReadInstant(string text)
    {
        string? raw = EmailText.ValueBelow(text, "Fecha y hora");
        if (raw is null) return null;

        Match match = InstantPattern().Match(raw);
        if (!match.Success) return null;

        string stamp = $"{match.Groups["fecha"].Value} {match.Groups["hora"].Value} "
            + match.Groups["meridiano"].Value.ToUpperInvariant();

        if (!DateTime.TryParseExact(
                stamp,
                "MM-dd-yyyy h:mm tt",
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
    /// Los últimos cuatro dígitos de la tarjeta.
    /// </summary>
    /// <remarks>
    /// Qik enmascara de dos formas en el mismo correo —`*1234` y
    /// `49*************1234`— y las dos terminan en los cuatro dígitos. Se toman
    /// los últimos cuatro de lo que siga a una máscara, que cubre las dos sin
    /// tener una regla por cada una.
    /// </remarks>
    private static string? ReadLastFour(string text)
    {
        Match match = MaskedCardPattern().Match(text);

        return match.Success ? match.Groups["cuatro"].Value : null;
    }

    /// <summary>
    /// Dónde se gastó.
    /// </summary>
    /// <remarks>
    /// La etiqueta cambia con el resultado: `Localidad` cuando la transacción
    /// pasó y `Lugar` cuando no. Se prueban las dos porque una plantilla puede
    /// llevar cualquiera de ellas, y como último recurso está la frase, que
    /// nombra el comercio entre «en» y «con tu Tarjeta».
    /// </remarks>
    private static string? ReadMerchant(string text)
    {
        string? labelled = EmailText.ValueBelow(text, "Localidad", "Lugar", "Comercio");
        if (labelled is not null) return Clean(labelled);

        Match sentence = SentenceAmountPattern().Match(text);

        return sentence.Success && sentence.Groups["comercio"].Value.Trim().Length > 0
            ? Clean(sentence.Groups["comercio"].Value)
            : null;
    }

    private static string Clean(string value) => value.Trim().Trim('.', ',', ';', ':').Trim();

    [GeneratedRegex(@"declinad|rechazad|no\s+aprobad|denegad|fallid", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex RejectedPattern();

    /// <summary>
    /// La frase: «… de RD$ 111.11 en PedidosYa*Papa Johns Ca con tu Tarjeta …».
    /// </summary>
    [GeneratedRegex(
        @"(?:de|por)\s+(?<monto>(?:RD)?\$\s*[\d,]+(?:\.\d{1,2})?)\s+en\s+"
        + @"(?<comercio>.+?)\s+con\s+tu\s+Tarjeta",
        RegexOptions.IgnoreCase, 2000)]
    private static partial Regex SentenceAmountPattern();

    [GeneratedRegex(
        @"(?:RD)?\$?\s*(?<entero>\d{1,3}(?:,\d{3})*|\d+)(?:\.(?<cent>\d{2}))?",
        RegexOptions.None, 2000)]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(
        @"(?<fecha>\d{2}-\d{2}-\d{4})\s+(?<hora>\d{1,2}:\d{2})\s*(?<meridiano>[AaPp]\.?[Mm]\.?)",
        RegexOptions.None, 2000)]
    private static partial Regex InstantPattern();

    [GeneratedRegex(@"[*x•]{1,}\s*\d*(?<cuatro>\d{4})(?!\d)", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex MaskedCardPattern();
}
