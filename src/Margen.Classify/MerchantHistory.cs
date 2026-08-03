using Margen.Domain;

namespace Margen.Classify;

/// <summary>Cuántas veces se clasificó un comercio en la categoría que más manda.</summary>
/// <param name="CategoryId">La categoría dominante.</param>
/// <param name="DominanceBasisPoints">Qué parte del historial ocupa, en puntos básicos.</param>
/// <param name="SampleCount">Cuántos movimientos confirmados lo respaldan.</param>
public readonly record struct MerchantVerdict(
    Guid CategoryId,
    int DominanceBasisPoints,
    int SampleCount);

/// <summary>
/// Qué categoría le ha puesto el usuario a un comercio hasta ahora.
/// </summary>
/// <remarks>
/// **Solo cuentan los movimientos confirmados**: los que salieron de una regla
/// del usuario, de una corrección o de una confirmación en la pantalla de
/// revisión. Nunca los que puso el automatismo por su cuenta.
///
/// Sin esa condición el historial se muerde la cola: el clasificador local pone
/// diez movimientos en una categoría equivocada, el historial los lee, se
/// confirma a sí mismo con confianza alta, y a partir de ahí el error es
/// permanente y nadie puede ver de dónde salió. Un automatismo que se cita a sí
/// mismo como fuente no es historial, es un eco.
///
/// El filtro lo aplica quien llama —esta capa no sabe de tablas—, y por eso el
/// parámetro se llama como se llama.
/// </remarks>
public static class MerchantHistory
{
    /// <summary>
    /// Mínimo de movimientos confirmados para que el historial opine.
    /// </summary>
    /// <remarks>
    /// Con dos, la primera corrección equivocada del usuario se convierte
    /// inmediatamente en «lo de siempre».
    /// </remarks>
    public const int MinimumSamples = 3;

    /// <summary>
    /// Dominancia mínima, en puntos básicos: seis de cada diez.
    /// </summary>
    /// <remarks>
    /// Por debajo de esto el comercio no tiene una categoría habitual, tiene
    /// varias. Un colmado donde a veces se compra comida y a veces se recarga el
    /// teléfono es exactamente eso, y responder «comida» porque va ganando 5 a 4
    /// es responder con ruido.
    /// </remarks>
    public const int MinimumDominance = 6_000;

    /// <summary>Tope de confianza que puede dar el historial.</summary>
    /// <remarks>
    /// Diez de diez no es certeza: es que las diez veces anteriores fueron
    /// iguales. El tope deja por debajo de la regla explícita, que sí es una
    /// persona diciendo qué quiere.
    /// </remarks>
    public const int MaxConfidence = 9_000;

    /// <summary>
    /// La categoría dominante del comercio, o por qué no la hay.
    /// </summary>
    public static Outcome<MerchantVerdict> Dominant(IReadOnlyList<Guid> confirmedCategoryIds)
    {
        ArgumentNullException.ThrowIfNull(confirmedCategoryIds);

        if (confirmedCategoryIds.Count < MinimumSamples)
        {
            return Outcome.Insufficient<MerchantVerdict>(
                $"Hacen falta {MinimumSamples} movimientos confirmados y hay "
                + $"{confirmedCategoryIds.Count}.");
        }

        var counts = new Dictionary<Guid, int>();
        foreach (Guid id in confirmedCategoryIds)
        {
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }

        int total = confirmedCategoryIds.Count;
        int top = counts.Values.Max();

        // Un empate no es una respuesta. Elegir uno de los dos por el orden del
        // diccionario daría respuestas distintas con los mismos datos.
        if (counts.Values.Count(c => c == top) > 1)
        {
            return Outcome.Insufficient<MerchantVerdict>(
                "El historial está empatado entre dos categorías.");
        }

        Guid winner = counts.First(pair => pair.Value == top).Key;
        int dominance = (int)((long)top * 10_000 / total);

        if (dominance < MinimumDominance)
        {
            return Outcome.Insufficient<MerchantVerdict>(
                $"La categoría dominante solo cubre {dominance} de 10000.");
        }

        return Outcome.Computed(
            new MerchantVerdict(winner, Math.Min(dominance, MaxConfidence), total));
    }
}
