namespace Margen.Classify;

/// <summary>
/// El puerto por el que un modelo puede sugerir una categoría.
/// </summary>
/// <remarks>
/// **La firma es la barrera.** El riesgo 3 del `LOOP.md` dice que el modelo no
/// toca cifras, y aquí eso no es una norma escrita en un comentario que alguien
/// tiene que acordarse de respetar: es que no hay por dónde pasarle una.
///
/// Recibe el nombre normalizado del comercio y los nombres de las categorías
/// que existen. No recibe <c>Money</c>, ni fecha, ni cuenta, ni saldo, ni el
/// movimiento. Meterle una cifra obliga a cambiar esta firma, y cambiar esta
/// firma se ve en la revisión del cambio.
///
/// Devuelve **un nombre de la lista o nulo**. No devuelve un número, no devuelve
/// un importe y no devuelve una categoría nueva: la implementación que responda
/// algo que no esté en <paramref name="categoryNames"/> se descarta en
/// <see cref="Cascade"/>, porque un modelo que inventa una categoría inventaría
/// también un presupuesto donde meterla.
///
/// Y aunque acierte, su respuesta **no clasifica sola**: ver
/// <see cref="Cascade.ModelConfidence"/>.
/// </remarks>
public interface ICategorySuggester
{
    /// <summary>
    /// Sugiere una de las categorías dadas para el comercio, o nulo.
    /// </summary>
    /// <param name="merchantNormalized">El nombre del comercio, ya normalizado.</param>
    /// <param name="categoryNames">Las categorías entre las que puede elegir.</param>
    /// <param name="cancellationToken">Para no esperar indefinidamente.</param>
    Task<string?> SuggestAsync(
        string merchantNormalized,
        IReadOnlyList<string> categoryNames,
        CancellationToken cancellationToken);
}
