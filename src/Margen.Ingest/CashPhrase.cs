using System.Globalization;
using System.Text.RegularExpressions;
using Margen.Domain;

namespace Margen.Ingest;

/// <summary>Por qué no se pudo interpretar una frase.</summary>
public enum PhraseProblem
{
    None,
    Empty,
    TooLong,
    NoAmount,
    NoDescription,
    ForeignCurrency,
    AmountNotPositive,
}

/// <summary>Lo que dice una frase de gasto, ya separado.</summary>
/// <param name="Amount">El monto, en centavos enteros.</param>
/// <param name="Description">En qué se gastó, tal como lo escribió la persona.</param>
/// <param name="DayOffset">0 si es hoy, -1 si dijo «ayer».</param>
public readonly record struct CashEntry(Money Amount, string Description, int DayOffset);

/// <summary>El resultado de interpretar una frase.</summary>
public readonly record struct PhraseResult(CashEntry? Entry, PhraseProblem Problem, string? Message)
{
    public bool Ok => Entry is not null;
}

/// <summary>
/// Interpreta «Gasté 450 pesos en almuerzo».
/// </summary>
/// <remarks>
/// **El monto lo saca una expresión regular y nunca un modelo.** Es la decisión
/// de la fase, y el motivo es que un modelo que un día lea «450» donde decía
/// «45.0» mete un error de un orden de magnitud en una cifra que después se
/// resta del líquido, sin ninguna señal de que ha pasado: el número es
/// plausible, el gasto existe y la categoría es correcta. Se descubre semanas
/// más tarde cuadrando a mano.
///
/// El modelo sí puede opinar sobre la **categoría**, que es lo que hace la
/// cascada de la Fase 8 con la descripción que sale de aquí, y allí tampoco
/// decide solo.
///
/// Todo lo que no se entiende se rechaza diciendo qué falta. No hay ningún
/// camino en el que esta clase adivine un número.
/// </remarks>
public static partial class CashPhrase
{
    /// <summary>
    /// Tope de longitud de la frase. Lo que llega por aquí acaba en la base y
    /// en una pantalla, y una frase de gasto no llega a doscientos caracteres.
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>
    /// Interpreta la frase, o dice por qué no.
    /// </summary>
    public static PhraseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Fail(PhraseProblem.Empty, "Escribe en qué gastaste y cuánto.");
        }

        if (text.Length > MaxLength)
        {
            return Fail(
                PhraseProblem.TooLong,
                $"La frase no puede pasar de {MaxLength} caracteres.");
        }

        string clean = Collapse(text);

        if (ForeignCurrencyPattern().IsMatch(clean))
        {
            // No se guarda como pesos. La cuenta de efectivo lleva pesos, no hay
            // tasa de cambio en el sistema, y elegir una sería inventarse una
            // cifra que después se resta del dinero disponible.
            return Fail(
                PhraseProblem.ForeignCurrency,
                "Solo se registran gastos en pesos dominicanos.");
        }

        Match? amount = PickAmount(clean);
        if (amount is null)
        {
            return Fail(
                PhraseProblem.NoAmount,
                "No encontré el monto. Escríbelo en números, por ejemplo "
                + "«gasté 450 pesos en almuerzo».");
        }

        if (!TryReadCents(amount.Groups["entero"].Value, amount.Groups["cent"].Value, out long cents))
        {
            return Fail(PhraseProblem.NoAmount, "Ese monto no se entiende.");
        }

        if (cents <= 0)
        {
            return Fail(PhraseProblem.AmountNotPositive, "El monto tiene que ser mayor que cero.");
        }

        int dayOffset = YesterdayPattern().IsMatch(clean) ? -1 : 0;

        string description = Describe(clean, amount);
        if (description.Length == 0)
        {
            return Fail(
                PhraseProblem.NoDescription,
                "Falta en qué gastaste. Por ejemplo «gasté 450 en almuerzo».");
        }

        return new PhraseResult(
            new CashEntry(new Money(cents), description, dayOffset),
            PhraseProblem.None,
            null);
    }

    /// <summary>
    /// Cuál de los números de la frase es el monto.
    /// </summary>
    /// <remarks>
    /// **Gana el que lleve la moneda pegada**; si ninguno la lleva, el primero.
    ///
    /// Quedarse siempre con el primero parece razonable y falla en silencio con
    /// una frase perfectamente normal: «compré 2 panes de 25 pesos» habría
    /// registrado **2 pesos**. Y es el peor fallo posible de esta fase —el gasto
    /// existe, la descripción es correcta, la categoría es correcta y la cifra
    /// está mal—, así que no aparece hasta que alguien cuadra a mano semanas
    /// después.
    ///
    /// La regla no adivina: o hay una marca de moneda que lo dice, o se usa el
    /// primero, que es lo que dicen las dos formas más comunes —«gasté 450 en
    /// almuerzo» y «almuerzo 450»—.
    /// </remarks>
    private static Match? PickAmount(string clean)
    {
        MatchCollection all = AmountPattern().Matches(clean);
        if (all.Count == 0) return null;

        foreach (Match candidate in all)
        {
            string before = clean[..candidate.Index];
            string after = clean[(candidate.Index + candidate.Length)..];

            if (CurrencyBeforePattern().IsMatch(before) || CurrencyAfterPattern().IsMatch(after))
            {
                return candidate;
            }
        }

        return all[0];
    }

    /// <summary>
    /// Convierte «1,234» y «56» en centavos, sin pasar por coma flotante.
    /// </summary>
    /// <remarks>
    /// La parte entera se multiplica por cien y se le suman los centavos, los
    /// dos como <see cref="long"/>. Leer «1234.56» con `double` y multiplicar
    /// por cien da 123455,99999999999 con más frecuencia de la que parece, y el
    /// truncado se come un centavo.
    ///
    /// El separador de millares es la coma y el decimal el punto, que es como se
    /// escribe aquí. Un decimal de un solo dígito son décimas: «450.5» son 450
    /// pesos con 50 centavos y no con 5.
    /// </remarks>
    private static bool TryReadCents(string whole, string fraction, out long cents)
    {
        cents = 0;

        string digits = whole.Replace(",", string.Empty, StringComparison.Ordinal);
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long units))
        {
            return false;
        }

        long fractionCents = 0;
        if (fraction.Length > 0)
        {
            string padded = fraction.Length == 1 ? fraction + "0" : fraction;
            if (!long.TryParse(
                padded, NumberStyles.None, CultureInfo.InvariantCulture, out fractionCents))
            {
                return false;
            }
        }

        try
        {
            cents = checked((units * 100) + fractionCents);
            return true;
        }
        catch (OverflowException)
        {
            // Una cifra que no cabe en un long no es un gasto de efectivo. El
            // tope real lo pone el endpoint; aquí solo se evita el envolvimiento
            // silencioso, que convertiría un número enorme en uno negativo.
            return false;
        }
    }

    /// <summary>
    /// Lo que queda de la frase quitando el monto y las palabras de relleno.
    /// </summary>
    /// <remarks>
    /// Se mira **a los dos lados** del monto: se escribe tanto «gasté 450 en
    /// almuerzo» como «almuerzo 450». Gana el lado derecho cuando dice algo,
    /// porque es la forma que sale del dictado.
    /// </remarks>
    private static string Describe(string clean, Match amount)
    {
        string after = Strip(clean[(amount.Index + amount.Length)..]);
        if (after.Length > 0) return after;

        return Strip(clean[..amount.Index]);
    }

    private static string Strip(string part)
    {
        string result = FillerPattern().Replace(part, " ");
        return Collapse(result).Trim(' ', ',', '.', ';', ':');
    }

    private static string Collapse(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        bool lastWasSpace = true;

        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            builder.Append(c);
        }

        return builder.ToString().Trim();
    }

    private static PhraseResult Fail(PhraseProblem problem, string message) =>
        new(null, problem, message);

    /// <summary>
    /// El monto: dígitos con millares opcionales y hasta dos decimales.
    /// </summary>
    /// <remarks>
    /// No admite numerales en palabras a propósito. El dictado de iOS ya
    /// convierte «cuatrocientos cincuenta» en «450», así que el caso real llega
    /// en dígitos; y media implementación —«mil» sí, «mil doscientos» no—
    /// enseñaría que funciona para fallar un día cualquiera.
    /// </remarks>
    [GeneratedRegex(
        @"(?<!\d)(?<entero>\d{1,3}(?:,\d{3})+|\d+)(?:\.(?<cent>\d{1,2}))?(?!\d)",
        RegexOptions.None, 2000)]
    private static partial Regex AmountPattern();

    /// <summary>
    /// Palabras que no describen nada: el verbo, la moneda y los conectores.
    /// </summary>
    /// <remarks>
    /// «RD» y «RD$» entran aquí y no en el patrón de moneda extranjera: son la
    /// moneda de casa y sobran en la descripción.
    /// </remarks>
    [GeneratedRegex(
        @"\b(?:gast[eé]|pagu[eé]|compr[eé]|pago|gasto|compra|rd|pesos?|peso|"
        + @"hoy|ayer|en|de|del|la|el|los|las|un|una|unos|unas|por|para|mi|al)\b|\$|RD\$",
        RegexOptions.IgnoreCase, 2000)]
    private static partial Regex FillerPattern();

    [GeneratedRegex(@"\bayer\b", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex YesterdayPattern();

    /// <summary>La moneda escrita justo antes del número: «RD$450», «$350».</summary>
    [GeneratedRegex(@"(?:RD\s*\$|\$)\s*$", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex CurrencyBeforePattern();

    /// <summary>La moneda escrita justo después del número: «450 pesos».</summary>
    [GeneratedRegex(@"^\s*(?:pesos?|RD\s*\$?)\b", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex CurrencyAfterPattern();

    /// <summary>
    /// Moneda que no es el peso dominicano.
    /// </summary>
    /// <remarks>
    /// `US$` y `USD` van antes que el `$` suelto porque el `$` suelto aquí
    /// significa pesos: en la República Dominicana el precio con `$` es en
    /// pesos, y `RD$` es la forma explícita de lo mismo.
    /// </remarks>
    [GeneratedRegex(
        @"\b(?:d[oó]lar(?:es)?|usd|eur(?:os?)?|euros?)\b|US\$|\bU\$",
        RegexOptions.IgnoreCase, 2000)]
    private static partial Regex ForeignCurrencyPattern();
}
