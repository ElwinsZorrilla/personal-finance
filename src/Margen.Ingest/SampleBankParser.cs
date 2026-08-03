using System.Globalization;
using System.Text.RegularExpressions;
using Margen.Domain;

namespace Margen.Ingest;

/// <summary>
/// Parser de un banco que no existe, con un formato inventado.
/// </summary>
/// <remarks>
/// **No es el parser del banco real.** Ese es la Fase 7 y está bloqueada a la
/// espera de correos reales anonimizados.
///
/// Existe para que el camino entero —bajar, guardar, elegir parser, detectar
/// duplicado, crear movimiento, reprocesar— se pueda ejercer y probar hoy. Si
/// el andamiaje solo se probara con el formato real, sería un andamiaje que
/// solo funciona con ese formato, y el segundo banco obligaría a rehacerlo.
///
/// Cuando llegue la muestra real, lo único que hará falta escribir es otra
/// clase como esta.
/// </remarks>
public sealed partial class SampleBankParser : IEmailParser
{
    public string Name => "muestra-sintetica";

    /// <summary>
    /// Se sube cuando el parser mejora. La herramienta de reproceso busca los
    /// correos interpretados con una versión anterior.
    /// </summary>
    public int Version => 1;

    /// <summary>
    /// La zona en la que escribe este banco. La conversión a UTC ocurre aquí,
    /// una sola vez: el parser es lo único del sistema que sabe en qué reloj
    /// mira su banco.
    /// </summary>
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    public bool CanHandle(RawEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        return email.Sender.Contains("banco-de-muestra", StringComparison.OrdinalIgnoreCase);
    }

    public ParseResult Parse(RawEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        if (!CanHandle(email)) return ParseResult.NotMine();

        TxKind? kind = KindOf(email.Subject);
        if (kind is null)
        {
            return ParseResult.NeedsReview(
                $"Asunto no reconocido: «{email.Subject}».");
        }

        // Cada dato se extrae por separado y **cada ausencia se dice**. Un
        // parser que devuelve ceros para lo que no encontró produce un
        // movimiento creíble y falso.
        Match amount = AmountPattern().Match(email.Body);
        if (!amount.Success)
        {
            return ParseResult.NeedsReview("No se encontró el monto en el cuerpo.");
        }

        Match account = AccountPattern().Match(email.Body);
        if (!account.Success)
        {
            return ParseResult.NeedsReview("No se encontraron los últimos cuatro dígitos.");
        }

        Match merchant = MerchantPattern().Match(email.Body);
        if (!merchant.Success)
        {
            return ParseResult.NeedsReview("No se encontró el comercio.");
        }

        Match when = WhenPattern().Match(email.Body);
        if (!when.Success)
        {
            // Y no se cae a la fecha de hoy: un movimiento con fecha inventada
            // cae en el período equivocado y descuadra los dos.
            return ParseResult.NeedsReview("No se encontró la fecha del movimiento.");
        }

        Money parsedAmount;
        try
        {
            // El formato es obligatorio: adivinar entre `1.234` y `1,234` es
            // una fuente de errores de mil a uno.
            parsedAmount = ParseMoney(amount.Groups["monto"].Value);
        }
        catch (FormatException)
        {
            return ParseResult.NeedsReview(
                $"El monto «{amount.Groups["monto"].Value}» no se pudo interpretar.");
        }

        if (parsedAmount.Cents <= 0)
        {
            return ParseResult.NeedsReview("El monto extraído no es positivo.");
        }

        if (!TryParseLocal(when.Groups["fecha"].Value, out DateTime utc))
        {
            return ParseResult.NeedsReview(
                $"La fecha «{when.Groups["fecha"].Value}» no se pudo interpretar.");
        }

        return ParseResult.Parsed(new ParsedTransaction(
            parsedAmount,
            "DOP",
            utc,
            merchant.Groups["comercio"].Value.Trim(),
            kind.Value,
            account.Groups["cuenta"].Value,
            Reference: ReferencePattern().Match(email.Body) is { Success: true } r
                ? r.Groups["referencia"].Value
                : null));
    }

    private static TxKind? KindOf(string subject) => subject switch
    {
        var s when s.Contains("Compra aprobada", StringComparison.OrdinalIgnoreCase)
            => TxKind.Purchase,
        var s when s.Contains("Retiro", StringComparison.OrdinalIgnoreCase)
            => TxKind.Withdrawal,
        var s when s.Contains("Devolución", StringComparison.OrdinalIgnoreCase)
            => TxKind.Refund,
        var s when s.Contains("Pago de tarjeta", StringComparison.OrdinalIgnoreCase)
            => TxKind.Payment,
        var s when s.Contains("Transferencia", StringComparison.OrdinalIgnoreCase)
            => TxKind.Transfer,
        var s when s.Contains("Depósito", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Deposito", StringComparison.OrdinalIgnoreCase)
            => TxKind.Deposit,
        _ => null,
    };

    /// <summary>
    /// `RD$ 2,450.00` con coma de miles y punto decimal, que es la convención
    /// dominicana. Se construye desde el entero, sin pasar por `decimal`.
    /// </summary>
    private static Money ParseMoney(string raw)
    {
        string cleaned = raw.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        string[] parts = cleaned.Split('.');

        if (parts.Length > 2 || parts[0].Length == 0)
        {
            throw new FormatException(raw);
        }

        if (!long.TryParse(parts[0], CultureInfo.InvariantCulture, out long units))
        {
            throw new FormatException(raw);
        }

        long hundredths = 0;
        if (parts.Length == 2)
        {
            string decimals = parts[1].PadRight(2, '0');
            if (decimals.Length > 2
                || !long.TryParse(decimals, CultureInfo.InvariantCulture, out hundredths))
            {
                throw new FormatException(raw);
            }
        }

        return new Money(checked((units * 100) + hundredths));
    }

    private static bool TryParseLocal(string raw, out DateTime utc)
    {
        utc = default;

        if (!DateTime.TryParseExact(
                raw.Trim(),
                "dd/MM/yyyy HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime local))
        {
            return false;
        }

        utc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            Zone);

        return true;
    }

    /// <summary>
    /// Captura el token del monto **entero**, con todos sus puntos y comas, y
    /// deja que <see cref="ParseMoney"/> decida si es válido.
    /// </summary>
    /// <remarks>
    /// La primera versión era `[\d,]+(?:\.\d{1,2})?`, que sobre `1.234.567`
    /// —formato europeo -- casaba solo `1.23` y lo daba por bueno: RD$1.23 en
    /// lugar de RD$1,234,567, sin error y sin aviso. Una expresión que casa un
    /// prefijo de algo que no entiende es peor que una que no casa nada.
    /// </remarks>
    [GeneratedRegex(@"Monto:\s*RD\$\s*(?<monto>[\d.,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AmountPattern();

    [GeneratedRegex(@"Tarjeta:\s*\*+(?<cuenta>\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex AccountPattern();

    [GeneratedRegex(@"Comercio:\s*(?<comercio>[^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MerchantPattern();

    [GeneratedRegex(@"Fecha:\s*(?<fecha>\d{2}/\d{2}/\d{4}\s+\d{2}:\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex WhenPattern();

    [GeneratedRegex(@"Referencia:\s*(?<referencia>[A-Z0-9-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencePattern();
}
