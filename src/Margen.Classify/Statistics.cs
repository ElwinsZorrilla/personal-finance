using Margen.Domain;

namespace Margen.Classify;

/// <summary>
/// Centro y dispersión de una lista de importes, con aritmética entera.
/// </summary>
/// <remarks>
/// Mediana y desviación absoluta mediana, no media y desviación típica. Por dos
/// motivos, y el segundo pesa más que el primero:
///
/// 1. La desviación típica lleva una raíz cuadrada, y eso mete coma flotante en
///    el camino del dinero.
/// 2. **Con pocas muestras la media miente.** Cinco compras de 300 pesos y una
///    de 40 000 dan una media de 6 900: ni una sola compra se parece a eso, y el
///    rango que salga de ahí no marcará como raro justamente el cargo raro. La
///    mediana de esas seis es 300, que sí describe lo que pasa de verdad.
/// </remarks>
public static class Statistics
{
    /// <summary>
    /// La mediana de una lista no vacía.
    /// </summary>
    /// <remarks>
    /// Con un número par de elementos es el promedio de los dos centrales, y
    /// ese promedio se calcula como <c>a + (b - a) / 2</c> y no como
    /// <c>(a + b) / 2</c>: la suma de dos importes grandes puede desbordar
    /// <see cref="long"/> y la resta de dos importes nunca lo hace.
    ///
    /// La división entera trunca hacia cero. Con importes siempre positivos
    /// —que es lo que guarda <see cref="Money"/>— eso es truncar hacia abajo, y
    /// un centavo de sesgo en la mediana no cambia si un cargo es raro.
    /// </remarks>
    public static Money Median(IReadOnlyList<Money> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("No hay mediana de una lista vacía.", nameof(values));
        }

        long[] ordered = values.Select(v => v.Cents).ToArray();
        Array.Sort(ordered);

        return MedianOfSorted(ordered);
    }

    /// <summary>
    /// La desviación absoluta mediana: la mediana de las distancias a la
    /// mediana.
    /// </summary>
    /// <remarks>
    /// Es la dispersión que no se deja arrastrar por un valor extremo, que es
    /// justo lo que hace falta aquí: el cargo raro que se busca detectar no debe
    /// participar en la definición de lo que es normal.
    /// </remarks>
    public static Money MedianAbsoluteDeviation(IReadOnlyList<Money> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("No hay desviación de una lista vacía.", nameof(values));
        }

        Money center = Median(values);

        long[] distances = values
            .Select(v => Math.Abs(v.Cents - center.Cents))
            .ToArray();
        Array.Sort(distances);

        return MedianOfSorted(distances);
    }

    /// <summary>
    /// La variación de <paramref name="from"/> a <paramref name="to"/> en puntos
    /// básicos: 10000 es el doble, -5000 es la mitad.
    /// </summary>
    /// <remarks>
    /// En puntos básicos enteros y no en porcentaje con coma por lo de siempre:
    /// esto se compara contra un umbral, y un umbral en coma flotante da
    /// respuestas distintas según por dónde entró el número.
    ///
    /// El producto se hace en <see cref="Int128"/> porque un importe grande por
    /// diez mil desborda <see cref="long"/> mucho antes de lo que parece: a
    /// partir de unos 92 000 millones de centavos, que son 920 millones de
    /// pesos. Improbable en unas finanzas personales; gratis de evitar.
    ///
    /// Devuelve <see cref="OutcomeKind.Invalid"/> si el punto de partida es
    /// cero: no existe «cuánto subió» respecto de nada.
    /// </remarks>
    public static Outcome<int> ChangeInBasisPoints(Money from, Money to)
    {
        if (from.Cents == 0)
        {
            return Outcome.Invalid<int>("No hay variación respecto de cero.");
        }

        Int128 delta = (Int128)to.Cents - from.Cents;
        Int128 basisPoints = delta * 10_000 / Int128.Abs(from.Cents);

        if (basisPoints > int.MaxValue || basisPoints < int.MinValue)
        {
            return Outcome.Invalid<int>("La variación no cabe en el rango esperado.");
        }

        return Outcome.Computed((int)basisPoints);
    }

    private static Money MedianOfSorted(long[] sorted)
    {
        int middle = sorted.Length / 2;

        if (sorted.Length % 2 == 1)
        {
            return new Money(sorted[middle]);
        }

        long low = sorted[middle - 1];
        long high = sorted[middle];

        // low + (high - low) / 2, no (low + high) / 2: la suma puede desbordar,
        // la resta de dos valores ordenados no.
        return new Money(low + ((high - low) / 2));
    }
}
