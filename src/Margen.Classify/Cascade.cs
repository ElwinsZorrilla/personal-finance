using Margen.Domain;

namespace Margen.Classify;

/// <summary>
/// Todo lo que la cascada necesita saber de un movimiento para clasificarlo.
/// </summary>
/// <param name="MerchantNormalized">El comercio, ya normalizado.</param>
/// <param name="Rules">Las reglas del usuario, en cualquier orden.</param>
/// <param name="ConfirmedCategoryIds">
/// Las categorías que el usuario **confirmó** antes en este mismo comercio. Solo
/// confirmadas: ver <see cref="MerchantHistory"/>.
/// </param>
/// <param name="CategoryIdsByName">
/// Las categorías que existen, por nombre. Es lo que traduce la respuesta del
/// clasificador local y la del modelo a un identificador de verdad.
/// </param>
public sealed record ClassificationRequest(
    string MerchantNormalized,
    IReadOnlyList<MerchantRuleSpec> Rules,
    IReadOnlyList<Guid> ConfirmedCategoryIds,
    IReadOnlyDictionary<string, Guid> CategoryIdsByName);

/// <summary>
/// Los seis escalones, en orden. Es el único sitio donde se decide una
/// categoría.
/// </summary>
/// <remarks>
/// Gana el primero que responda. El orden no es una preferencia estética: va de
/// lo que dijo una persona a lo que adivinó una máquina, y cada escalón sabe
/// menos que el anterior sobre este usuario en concreto.
/// </remarks>
public static class Cascade
{
    /// <summary>
    /// A partir de aquí se asigna la categoría sin preguntar: 8000 de 10000.
    /// </summary>
    public const int AutoAssignThreshold = 8_000;

    /// <summary>Confianza de una regla exacta del usuario. No hay más arriba.</summary>
    public const int UserRuleConfidence = 10_000;

    /// <summary>Confianza de una regla por patrón del usuario.</summary>
    /// <remarks>
    /// Un poco por debajo de la exacta: «contiene UBER» lo escribió la misma
    /// persona, pero describe una familia de comercios y no uno.
    /// </remarks>
    public const int PatternRuleConfidence = 9_500;

    /// <summary>
    /// Confianza de una sugerencia del modelo. **Tope duro.**
    /// </summary>
    /// <remarks>
    /// Es la decisión principal de la fase escrita en un número. Un modelo que
    /// se equivoca de categoría mueve dinero de un presupuesto a otro, y el
    /// «cuánto puedo gastar» de la pantalla sale mal por un motivo que nadie
    /// puede ver.
    ///
    /// El escalón existe para que la pantalla de revisión llegue con la
    /// respuesta ya escrita y se confirme con un toque, que es muy distinto de
    /// elegir a mano entre veinte categorías. No existe para ahorrarse la
    /// confirmación.
    ///
    /// Y no basta con que este número esté por debajo del umbral:
    /// <see cref="Classification.IsAutomatic"/> comprueba además el origen, así
    /// que bajar el umbral no abre esta puerta.
    /// </remarks>
    public const int ModelConfidence = 5_000;

    /// <summary>
    /// Clasifica sin llamar al modelo. Es el camino que recorre la ingesta.
    /// </summary>
    public static Outcome<Classification> Classify(ClassificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1 y 2 · Lo que dijo el usuario.
        MerchantRuleSpec? rule = RuleMatcher.BestMatch(request.MerchantNormalized, request.Rules);
        if (rule is not null)
        {
            bool exact = rule.Value.MatchKind == MatchKind.Exact;
            return Outcome.Computed(new Classification(
                rule.Value.CategoryId,
                exact ? UserRuleConfidence : PatternRuleConfidence,
                exact ? ClassifiedBy.UserRule : ClassifiedBy.PatternRule,
                exact
                    ? "Regla del usuario para este comercio."
                    : $"Regla del usuario por patrón «{rule.Value.Pattern}»."));
        }

        // 3 · Lo que el usuario confirmó otras veces aquí mismo.
        Outcome<MerchantVerdict> history =
            MerchantHistory.Dominant(request.ConfirmedCategoryIds);
        if (history.IsComputed)
        {
            MerchantVerdict verdict = history.Value;
            return Outcome.Computed(new Classification(
                verdict.CategoryId,
                verdict.DominanceBasisPoints,
                ClassifiedBy.History,
                $"Así se clasificó {verdict.SampleCount} veces en este comercio."));
        }

        // 4 · La tabla de palabras.
        string? suggested = LocalClassifier.Suggest(request.MerchantNormalized);
        if (suggested is not null
            && request.CategoryIdsByName.TryGetValue(suggested, out Guid localId))
        {
            return Outcome.Computed(new Classification(
                localId,
                LocalClassifier.Confidence,
                ClassifiedBy.LocalTable,
                $"El nombre del comercio se parece a «{suggested}»."));
        }

        return Outcome.Insufficient<Classification>(
            "Ninguna regla, ningún historial y ninguna palabra conocida.");
    }

    /// <summary>
    /// Clasifica, y si nada respondió le pregunta al modelo.
    /// </summary>
    /// <remarks>
    /// El modelo se consulta **solo cuando los cuatro escalones anteriores se
    /// quedaron callados**, que es lo que hace que una corrección del usuario
    /// evite la siguiente llamada: en cuanto existe la regla, este método
    /// devuelve en el escalón 1 y no hay llamada que hacer.
    ///
    /// Si el modelo falla, tarda o responde algo que no está en la lista, el
    /// resultado es el mismo que si no se hubiera llamado: revisión humana. Un
    /// proveedor caído no puede impedir que un movimiento entre.
    /// </remarks>
    public static async Task<Outcome<Classification>> ClassifyAsync(
        ClassificationRequest request,
        ICategorySuggester? suggester,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Outcome<Classification> local = Classify(request);
        if (local.IsComputed || suggester is null) return local;

        string[] names = [.. request.CategoryIdsByName.Keys];
        if (names.Length == 0) return local;

        string? answer;
        try
        {
            answer = await suggester
                .SuggestAsync(request.MerchantNormalized, names, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // La cancelación la pidió quien llama; no es cosa del modelo.
            throw;
        }
#pragma warning disable CA1031 // Un proveedor externo puede fallar de cualquier forma.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Un modelo caído no bloquea la ingesta. El movimiento entra y se
            // revisa a mano, que es exactamente lo que pasaba antes de que
            // existiera este escalón.
            return local;
        }

        // Una categoría que no está en la lista no existe. Un modelo que la
        // inventa inventaría también el presupuesto donde meterla.
        if (answer is null || !request.CategoryIdsByName.TryGetValue(answer, out Guid modelId))
        {
            return local;
        }

        return Outcome.Computed(new Classification(
            modelId,
            ModelConfidence,
            ClassifiedBy.Model,
            $"Sugerencia por el nombre del comercio: «{answer}». Confírmala."));
    }
}
