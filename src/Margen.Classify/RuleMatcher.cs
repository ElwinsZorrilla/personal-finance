using System.Text.RegularExpressions;
using Margen.Domain;

namespace Margen.Classify;

/// <summary>
/// Una regla de comercio, sin la entidad ni la base de datos detrás.
/// </summary>
/// <param name="CategoryId">Qué categoría asigna.</param>
/// <param name="Pattern">Contra qué se compara el comercio normalizado.</param>
/// <param name="MatchKind">Cómo se compara.</param>
/// <param name="IsUserDefined">La escribió una persona corrigiendo, no un proceso.</param>
/// <param name="Weight">Entre dos reglas que casan, gana la de más peso.</param>
public readonly record struct MerchantRuleSpec(
    Guid CategoryId,
    string Pattern,
    MatchKind MatchKind,
    bool IsUserDefined,
    int Weight);

/// <summary>
/// Compara un comercio normalizado contra las reglas del usuario.
/// </summary>
/// <remarks>
/// Las reglas por expresión regular son **texto que escribió una persona y que
/// aquí se compila a expresión regular**. Es la superficie expuesta de esta
/// fase, aunque la persona sea la dueña de sus propios datos: una expresión con
/// retroceso catastrófico no es un ataque, es un error de escritura, y el
/// resultado es el mismo —el proceso de ingesta colgado y ningún correo leído—.
///
/// Dos defensas, y las dos fallan cerrado: tope de longitud del patrón, y
/// tiempo límite en la comparación. Agotar el tiempo **cuenta como que no
/// casa**; no lanza y no interrumpe la clasificación de nada.
/// </remarks>
public static class RuleMatcher
{
    /// <summary>
    /// Tope de longitud de un patrón. Un nombre de comercio no llega a cien
    /// caracteres, así que doscientos deja margen de sobra para un patrón
    /// razonable y corta en seco los que no lo son.
    /// </summary>
    public const int MaxPatternLength = 200;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// La regla de mayor peso que case, o nulo si no casa ninguna.
    /// </summary>
    /// <remarks>
    /// A igualdad de peso gana la del usuario. Y a igualdad de las dos cosas
    /// gana la primera de la lista, que el llamador ordena: dos reglas
    /// idénticas asignando categorías distintas es un dato contradictorio y
    /// elegir «la mejor» de dos contradicciones sería inventarse un criterio.
    /// </remarks>
    public static MerchantRuleSpec? BestMatch(
        string merchantNormalized,
        IReadOnlyList<MerchantRuleSpec> rules)
    {
        ArgumentNullException.ThrowIfNull(merchantNormalized);
        ArgumentNullException.ThrowIfNull(rules);

        MerchantRuleSpec? best = null;

        foreach (MerchantRuleSpec rule in rules)
        {
            if (!Matches(merchantNormalized, rule)) continue;

            if (best is null
                || rule.Weight > best.Value.Weight
                || (rule.Weight == best.Value.Weight
                    && rule.IsUserDefined && !best.Value.IsUserDefined))
            {
                best = rule;
            }
        }

        return best;
    }

    /// <summary>
    /// Si una regla casa con un comercio.
    /// </summary>
    public static bool Matches(string merchantNormalized, MerchantRuleSpec rule)
    {
        ArgumentNullException.ThrowIfNull(merchantNormalized);

        if (string.IsNullOrWhiteSpace(rule.Pattern)) return false;

        // Un patrón vacío casaría con todo y mandaría el buzón entero a una
        // categoría. Un patrón desmedido no lo escribió nadie a mano.
        if (rule.Pattern.Length > MaxPatternLength) return false;

        return rule.MatchKind switch
        {
            MatchKind.Exact => string.Equals(
                merchantNormalized, rule.Pattern, StringComparison.OrdinalIgnoreCase),

            MatchKind.Contains => merchantNormalized.Contains(
                rule.Pattern, StringComparison.OrdinalIgnoreCase),

            MatchKind.Regex => MatchesRegex(merchantNormalized, rule.Pattern),

            // Un valor de enum que no está en la lista no casa con nada. Sin
            // rama por defecto que diga que sí.
            _ => false,
        };
    }

    private static bool MatchesRegex(string merchantNormalized, string pattern)
    {
        try
        {
            return Regex.IsMatch(
                merchantNormalized,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                MatchTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            // No casa. Una regla que tarda demasiado no clasifica nada, y el
            // movimiento sigue su camino hasta la revisión humana.
            return false;
        }
        catch (ArgumentException)
        {
            // Patrón mal escrito. Mismo trato: no casa. Que una regla rota
            // tumbe la ingesta del buzón entero sería peor que ignorarla.
            return false;
        }
    }
}
