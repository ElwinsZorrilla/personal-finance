namespace Margen.Classify;

/// <summary>De qué escalón de la cascada salió una clasificación.</summary>
public enum ClassifiedBy
{
    /// <summary>Regla exacta escrita por el usuario al corregir.</summary>
    UserRule,

    /// <summary>Regla por patrón —contiene o expresión regular— del usuario.</summary>
    PatternRule,

    /// <summary>Lo que el usuario confirmó las últimas veces en este comercio.</summary>
    History,

    /// <summary>Tabla de palabras. Sin red y determinista.</summary>
    LocalTable,

    /// <summary>Sugerencia de un modelo. Nunca cierra la clasificación sola.</summary>
    Model,
}

/// <summary>
/// Qué categoría le toca a un movimiento, con cuánta confianza y por qué.
/// </summary>
/// <param name="CategoryId">La categoría.</param>
/// <param name="ConfidenceBasisPoints">Confianza de 0 a 10000. Entero, como el dinero.</param>
/// <param name="Source">Qué escalón respondió.</param>
/// <param name="Reason">En castellano, para que quepa en la pantalla de revisión.</param>
public readonly record struct Classification(
    Guid CategoryId,
    int ConfidenceBasisPoints,
    ClassifiedBy Source,
    string Reason)
{
    /// <summary>
    /// Si esta clasificación se puede aplicar sin preguntarle a nadie.
    /// </summary>
    /// <remarks>
    /// Dos condiciones, y la segunda no se deduce de la primera: hace falta
    /// llegar al umbral **y** no venir del modelo. Aunque alguien baje el
    /// umbral por debajo de <see cref="Cascade.ModelConfidence"/>, una
    /// sugerencia del modelo sigue sin poder cerrar una clasificación.
    /// </remarks>
    public bool IsAutomatic =>
        ConfidenceBasisPoints >= Cascade.AutoAssignThreshold
        && Source != ClassifiedBy.Model;
}
