using Margen.Domain;

namespace Margen.Classify;

/// <summary>
/// El rango de lo que es normal gastar en un comercio.
/// </summary>
/// <param name="Low">Extremo bajo, nunca negativo.</param>
/// <param name="High">Extremo alto.</param>
/// <param name="Center">La mediana de la que salió.</param>
/// <param name="SampleCount">Cuántos cargos lo respaldan.</param>
public readonly record struct NormalRange(Money Low, Money High, Money Center, int SampleCount)
{
    public bool Contains(Money amount) => amount >= Low && amount <= High;
}

/// <summary>
/// Construye el rango normal de un comercio a partir de sus cargos anteriores.
/// </summary>
public static class NormalRanges
{
    /// <summary>
    /// Por debajo de esto no hay rango. No es un número redondo elegido al
    /// azar: con cuatro muestras la desviación absoluta mediana se calcula sobre
    /// dos distancias, y eso no describe nada.
    /// </summary>
    public const int MinimumSamples = 5;

    /// <summary>Cuántas desviaciones caben dentro de lo normal.</summary>
    public const int Spread = 3;

    /// <summary>
    /// Holgura mínima, como fracción de la mediana: una décima parte.
    /// </summary>
    /// <remarks>
    /// Existe por el caso más común de todos: una suscripción cobra exactamente
    /// lo mismo cinco veces, la desviación absoluta mediana sale cero, y el
    /// rango sería un solo punto. El sexto cargo con un centavo de diferencia
    /// —un ajuste de impuesto, un cambio de divisa— saldría como anómalo.
    ///
    /// Una alerta que se equivoca dos veces se deja de leer, y a partir de ahí
    /// da igual lo bien que detecte la tercera.
    /// </remarks>
    public const int FloorNumerator = 1;

    /// <summary>Denominador de la holgura mínima. Ver <see cref="FloorNumerator"/>.</summary>
    public const int FloorDenominator = 10;

    /// <summary>
    /// El rango normal de una lista de cargos, o por qué no lo hay.
    /// </summary>
    /// <remarks>
    /// Devuelve <see cref="OutcomeKind.Insufficient"/> con menos de
    /// <see cref="MinimumSamples"/> cargos, y no un rango ancho «por si acaso».
    /// Un rango inventado con dos muestras marca como raro el tercer cargo
    /// normal; es peor que no mirar.
    /// </remarks>
    public static Outcome<NormalRange> Of(IReadOnlyList<Money> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (history.Count < MinimumSamples)
        {
            return Outcome.Insufficient<NormalRange>(
                $"Hacen falta {MinimumSamples} cargos y hay {history.Count}.");
        }

        Money center = Statistics.Median(history);
        Money deviation = Statistics.MedianAbsoluteDeviation(history);

        Money margin = deviation.Scale(Spread, 1);
        Money floor = center.Scale(FloorNumerator, FloorDenominator);
        if (margin < floor) margin = floor;

        Money low = center - margin;
        if (low.Cents < 0) low = Money.Zero;

        return Outcome.Computed(
            new NormalRange(low, center + margin, center, history.Count));
    }
}
