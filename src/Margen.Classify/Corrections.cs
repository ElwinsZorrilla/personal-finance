using Margen.Domain;

namespace Margen.Classify;

/// <summary>Qué hacer con la regla tras una corrección del usuario.</summary>
public enum CorrectionAction
{
    /// <summary>No había regla para este comercio: se crea.</summary>
    Create,

    /// <summary>Había una y apuntaba a otra categoría: se le cambia el destino.</summary>
    Update,

    /// <summary>Ya decía exactamente esto. No se toca nada.</summary>
    None,
}

/// <summary>El efecto de una corrección sobre las reglas.</summary>
/// <param name="Action">Crear, actualizar o nada.</param>
/// <param name="Pattern">Sobre qué comercio.</param>
/// <param name="CategoryId">A qué categoría.</param>
/// <param name="Weight">Con cuánto peso.</param>
public readonly record struct CorrectionPlan(
    CorrectionAction Action,
    string Pattern,
    Guid CategoryId,
    int Weight);

/// <summary>
/// Convierte una corrección del usuario en la regla que evita la siguiente.
/// </summary>
/// <remarks>
/// Es el mecanismo que hace que la aplicación deje de preguntar: corregir una
/// vez «este comercio es esta categoría» resuelve todas las compras futuras en
/// el primer escalón de la cascada, sin historial y sin modelo.
/// </remarks>
public static class Corrections
{
    /// <summary>
    /// Peso con el que nace una regla del usuario.
    /// </summary>
    /// <remarks>
    /// Por encima de cualquier regla deducida de un proceso automático: la
    /// corrección explícita de una persona pesa más que una estadística. Las
    /// deducidas se quedan por debajo de 1000 por convenio.
    /// </remarks>
    public const int UserWeight = 1_000;

    /// <summary>
    /// Qué hay que hacer con las reglas cuando el usuario corrige un comercio.
    /// </summary>
    /// <remarks>
    /// **Actualiza; no apila.** Corregir dos veces el mismo comercio a
    /// categorías distintas tiene que dejar una sola regla diciendo la última
    /// cosa. Con dos reglas del mismo peso y categorías distintas, cuál gana
    /// depende del orden en que las devuelva la base de datos, y eso es un
    /// resultado que cambia solo.
    ///
    /// Y si la regla ya decía exactamente esto, no se escribe nada:
    /// <see cref="CorrectionAction.None"/>. Reescribirla movería su fecha y su
    /// contador sin que haya pasado nada.
    /// </remarks>
    public static CorrectionPlan Plan(
        string merchantNormalized,
        Guid categoryId,
        IReadOnlyList<MerchantRuleSpec> existingRules)
    {
        ArgumentNullException.ThrowIfNull(merchantNormalized);
        ArgumentNullException.ThrowIfNull(existingRules);

        if (string.IsNullOrWhiteSpace(merchantNormalized))
        {
            // Sin comercio no hay nada que enseñar. Una regla con patrón vacío
            // no casaría con nada -RuleMatcher lo rechaza- y quedaría de basura
            // en la tabla para siempre.
            return new CorrectionPlan(CorrectionAction.None, merchantNormalized, categoryId, 0);
        }

        foreach (MerchantRuleSpec rule in existingRules)
        {
            bool sameMerchant = rule.MatchKind == MatchKind.Exact
                && rule.IsUserDefined
                && string.Equals(
                    rule.Pattern, merchantNormalized, StringComparison.OrdinalIgnoreCase);

            if (!sameMerchant) continue;

            return rule.CategoryId == categoryId
                ? new CorrectionPlan(
                    CorrectionAction.None, merchantNormalized, categoryId, rule.Weight)
                : new CorrectionPlan(
                    CorrectionAction.Update, merchantNormalized, categoryId, rule.Weight);
        }

        return new CorrectionPlan(
            CorrectionAction.Create, merchantNormalized, categoryId, UserWeight);
    }
}
