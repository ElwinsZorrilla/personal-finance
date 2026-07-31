using System.Collections.Immutable;
using Margen.Domain;

namespace Margen.Budget;

/// <summary>
/// La base histórica: lo que este usuario suele gastar, ponderado hacia lo
/// reciente.
/// </summary>
public static class HistoricalBaseline
{
    /// <summary>
    /// 50 % el último período cerrado, 30 % el anterior, 20 % el tercero.
    /// </summary>
    /// <remarks>
    /// El peso decrece porque lo de hace tres meses describe peor el mes que
    /// viene que lo del mes pasado, pero no se descarta: un solo período
    /// convierte cualquier mes raro —un viaje, una reparación— en la nueva
    /// normalidad.
    ///
    /// Es <see cref="ImmutableArray{T}"/> y no <c>long[]</c>. En un arreglo,
    /// <c>readonly</c> protege la referencia y no el contenido: cualquiera
    /// podría escribir <c>Weights[0] = 999</c> y cambiar en silencio todas las
    /// recomendaciones de presupuesto del sistema, sin tocar una línea de este
    /// archivo y sin que ninguna prueba lo notara.
    /// </remarks>
    public static readonly ImmutableArray<long> Weights = [50, 30, 20];

    /// <summary>
    /// Promedia los períodos cerrados, del más reciente al más antiguo.
    /// </summary>
    /// <remarks>
    /// Con menos de tres períodos los pesos se renormalizan sobre los que hay:
    /// con dos, 50 y 30 se convierten en 50/80 y 30/80. Tratar el tercero como
    /// cero haría que un usuario con dos meses de historia viera una base
    /// histórica un 20 % menor de lo que gasta, y esa cifra es la que propone
    /// el presupuesto del período siguiente.
    ///
    /// Con cero períodos cerrados no hay respuesta, y se dice. Devolver cero
    /// sería inventar una historia que no existe.
    /// </remarks>
    public static Outcome<Money> Average(IReadOnlyList<Money> closedPeriodsNewestFirst)
    {
        ArgumentNullException.ThrowIfNull(closedPeriodsNewestFirst);

        if (closedPeriodsNewestFirst.Count == 0)
        {
            return Outcome.Insufficient<Money>(
                "No hay ningún período cerrado del que sacar una base histórica.");
        }

        foreach (Money period in closedPeriodsNewestFirst)
        {
            if (period.IsNegative)
            {
                return Outcome.Invalid<Money>(
                    "Un período cerrado con gasto negativo no describe ningún hábito.");
            }
        }

        List<Money> considered = [.. closedPeriodsNewestFirst.Take(Weights.Length)];


        return Outcome.Computed(Money.WeightedAverage(considered, Weights));
    }

    /// <summary>
    /// Cuántos períodos entraron en el promedio. La pantalla lo necesita para
    /// poder decir «según tus últimos dos meses» y no fingir tres.
    /// </summary>
    public static int PeriodsConsidered(int available) => Math.Min(available, Weights.Length);
}
