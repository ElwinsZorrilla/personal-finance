using System.Globalization;

namespace Margen.Domain;

/// <summary>
/// Cantidad monetaria en centavos. Contraparte exacta de <c>Money</c> en el
/// cliente Flutter.
/// </summary>
/// <remarks>
/// Nunca se usa <c>double</c>, <c>float</c> ni <c>decimal</c> para dinero.
/// El punto flotante binario no representa 0.10 exactamente y sumar cien
/// transacciones de RD$0.10 no da RD$10.00. <c>decimal</c> sí es exacto en
/// base diez, pero permite fracciones de centavo, y una fracción de centavo
/// que sobrevive tres operaciones es una conciliación que no cuadra sin que
/// nadie sepa por qué. Toda la aritmética ocurre sobre enteros y la conversión
/// a texto es el último paso.
/// </remarks>
public readonly record struct Money(long Cents) : IComparable<Money>
{
    public static readonly Money Zero = new(0);

    /// <summary>Construye desde unidades enteras: <c>FromUnits(50)</c> es RD$50.00.</summary>
    public static Money FromUnits(long units) => new(checked(units * 100));

    public bool IsNegative => Cents < 0;

    public bool IsZero => Cents == 0;

    public Money Abs => new(Math.Abs(Cents));

    public static Money operator +(Money a, Money b) => new(checked(a.Cents + b.Cents));

    public static Money operator -(Money a, Money b) => new(checked(a.Cents - b.Cents));

    public static Money operator -(Money a) => new(checked(-a.Cents));

    public static bool operator >(Money a, Money b) => a.Cents > b.Cents;

    public static bool operator <(Money a, Money b) => a.Cents < b.Cents;

    public static bool operator >=(Money a, Money b) => a.Cents >= b.Cents;

    public static bool operator <=(Money a, Money b) => a.Cents <= b.Cents;

    /// <summary>
    /// Multiplica por la fracción <paramref name="numerator"/>/<paramref name="denominator"/>.
    /// </summary>
    /// <remarks>
    /// Toma una fracción y no un <c>double</c> a propósito: «el 50 % del período
    /// anterior» es exactamente 1/2, no 0.5 aproximado en binario. El producto
    /// intermedio se calcula en <see cref="Int128"/> porque centavos por
    /// numerador desborda un <c>long</c> con cifras perfectamente normales.
    /// El redondeo es a la mitad alejándose del cero, y es explícito.
    /// </remarks>
    public Money Scale(long numerator, long denominator)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException("El denominador de un escalado no puede ser cero.");
        }

        Int128 product = (Int128)Cents * numerator;
        Int128 divisor = denominator;
        if (divisor < 0)
        {
            divisor = -divisor;
            product = -product;
        }

        Int128 quotient = product / divisor;
        Int128 remainder = product - (quotient * divisor);

        // La división entera de C# trunca hacia cero, así que el resto lleva el
        // signo del producto. Se redondea alejándose del cero cuando el resto
        // llega a la mitad del divisor.
        if (Int128.Abs(remainder) * 2 >= divisor)
        {
            quotient += product < 0 ? -1 : 1;
        }

        return new Money(checked((long)quotient));
    }

    /// <summary>
    /// Reparte en <paramref name="parts"/> partes iguales sin perder ni inventar
    /// centavos. Los centavos sobrantes van a las primeras partes.
    /// </summary>
    public Money[] Split(int parts)
    {
        if (parts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parts),
                parts,
                "Repartir en cero partes divide por cero.");
        }

        long baseAmount = Cents / parts;
        long remainder = Math.Abs(Cents % parts);
        long sign = Cents < 0 ? -1 : 1;

        var result = new Money[parts];
        for (int i = 0; i < parts; i++)
        {
            result[i] = new Money(baseAmount + (i < remainder ? sign : 0));
        }

        return result;
    }

    /// <summary>
    /// Reparte proporcionalmente a <paramref name="weights"/>. La suma de las
    /// partes es exactamente el total: los centavos que sobran del truncamiento
    /// se asignan por el método del resto mayor, no se descartan.
    /// </summary>
    public Money[] Prorate(IReadOnlyList<long> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count == 0)
        {
            throw new ArgumentException("Hace falta al menos un peso.", nameof(weights));
        }

        long total = 0;
        foreach (long weight in weights)
        {
            if (weight < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(weights),
                    weight,
                    "Un peso negativo invierte el reparto en silencio.");
            }

            total = checked(total + weight);
        }

        if (total == 0)
        {
            throw new ArgumentException(
                "Los pesos suman cero: no hay forma de repartir.",
                nameof(weights));
        }

        // Se reparte sobre el valor absoluto y el signo se reaplica al final,
        // para que el resto mayor no dependa de hacia dónde trunca la división.
        long magnitude = Math.Abs(Cents);
        long sign = Cents < 0 ? -1 : 1;

        var shares = new long[weights.Count];
        var remainders = new Int128[weights.Count];
        long assigned = 0;

        for (int i = 0; i < weights.Count; i++)
        {
            Int128 product = (Int128)magnitude * weights[i];
            Int128 share = product / total;
            shares[i] = (long)share;
            remainders[i] = product - (share * total);
            assigned = checked(assigned + shares[i]);
        }

        long leftover = magnitude - assigned;
        var order = new int[weights.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        // Resto mayor primero; a igualdad de resto, el índice menor. El
        // desempate por índice es lo que hace el reparto reproducible.
        Array.Sort(order, (a, b) =>
        {
            int byRemainder = remainders[b].CompareTo(remainders[a]);
            return byRemainder != 0 ? byRemainder : a.CompareTo(b);
        });

        for (int i = 0; i < leftover; i++)
        {
            shares[order[i]]++;
        }

        var result = new Money[weights.Count];
        for (int i = 0; i < shares.Length; i++)
        {
            result[i] = new Money(shares[i] * sign);
        }

        return result;
    }

    /// <summary>
    /// Promedio ponderado exacto de varios montos.
    /// </summary>
    /// <remarks>
    /// Se calcula como una sola división y no como suma de términos escalados.
    /// Escalar cada monto por su peso y sumar los resultados redondea tres
    /// veces y suma los tres errores; esto redondea una vez, al final. La
    /// diferencia es de céntimos, y este es el número del que cuelga el
    /// presupuesto recomendado del período siguiente.
    ///
    /// La longitud de <paramref name="weights"/> puede ser mayor que la de
    /// <paramref name="values"/>: se usan los primeros y el denominador se
    /// calcula sobre esos. Es lo que hace que 50/30/20 con solo dos períodos
    /// cerrados sea 50/80 y 30/80, y no 50/100 y 30/100 con un tercio del
    /// promedio inventado en cero.
    /// </remarks>
    public static Money WeightedAverage(IReadOnlyList<Money> values, IReadOnlyList<long> weights)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(weights);

        if (values.Count == 0)
        {
            throw new ArgumentException(
                "No hay nada que promediar. Devolver cero sería inventar un promedio.",
                nameof(values));
        }

        if (weights.Count < values.Count)
        {
            throw new ArgumentException(
                "Faltan pesos para algunos de los valores.",
                nameof(weights));
        }

        Int128 numerator = 0;
        long denominator = 0;

        for (int i = 0; i < values.Count; i++)
        {
            if (weights[i] < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(weights),
                    weights[i],
                    "Un peso negativo invierte el promedio en silencio.");
            }

            numerator += (Int128)values[i].Cents * weights[i];
            denominator = checked(denominator + weights[i]);
        }

        if (denominator == 0)
        {
            throw new ArgumentException(
                "Los pesos suman cero: no hay forma de promediar.",
                nameof(weights));
        }

        Int128 quotient = numerator / denominator;
        Int128 remainder = numerator - (quotient * denominator);

        if (Int128.Abs(remainder) * 2 >= denominator)
        {
            quotient += numerator < 0 ? -1 : 1;
        }

        return new Money(checked((long)quotient));
    }

    /// <summary>Suma una secuencia sin salirse de los enteros.</summary>
    public static Money Sum(IEnumerable<Money> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        long total = 0;
        foreach (Money value in values)
        {
            total = checked(total + value.Cents);
        }

        return new Money(total);
    }

    public int CompareTo(Money other) => Cents.CompareTo(other.Cents);

    /// <summary>
    /// Para registros y mensajes de error. El formato de pantalla vive en el
    /// cliente: el servidor entrega centavos y el cliente decide cómo se leen.
    /// </summary>
    /// <remarks>
    /// El signo se antepone al valor absoluto ya formateado. Dejar que lo
    /// coloque la división haría que -0.50 se imprimiera como «0.50», porque
    /// -50/100 trunca a cero y el menos desaparece.
    /// </remarks>
    public override string ToString()
    {
        long magnitude = Math.Abs(Cents);
        string sign = Cents < 0 ? "-" : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{magnitude / 100}.{magnitude % 100:D2}");
    }
}
