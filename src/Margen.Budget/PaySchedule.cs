using System.Collections.Immutable;
using Margen.Domain;

namespace Margen.Budget;

/// <summary>
/// Los días del mes en que entra el sueldo. Uno, dos o los que hagan falta.
/// </summary>
/// <remarks>
/// La Fase 3 dio por hecho **un solo cobro al mes**, y con eso el ciclo era
/// trivial: del día de cobro al día anterior al del mes siguiente. Con quincena
/// y fin de mes hay dos ciclos por mes, y eso cambia dónde empieza el período,
/// cuántos días tiene y, por tanto, el reparto diario. La fórmula del dinero
/// seguro no se entera: parte del saldo real, no del ingreso esperado.
///
/// **Lo variable no entra aquí.** Un ingreso que no se sabe cuándo llega no
/// tiene día de mes, y meterlo en este calendario obligaría a inventarle uno.
/// Cuando cae en la cuenta sube el saldo, y el dinero seguro sube con él. Nunca
/// antes: proyectar dinero que quizá no aparezca es el error más caro que puede
/// cometer esta app.
///
/// Los días se guardan ordenados y sin repetidos. Es lo que permite que
/// <see cref="CycleAround"/> sea una búsqueda y no un caso especial por cada
/// combinación.
/// </remarks>
public sealed record PaySchedule
{
    private PaySchedule(ImmutableArray<int> days) => Days = days;

    /// <summary>Días del mes, ordenados y sin repetir.</summary>
    public ImmutableArray<int> Days { get; }

    /// <summary>
    /// Construye el calendario a partir de los días de cobro.
    /// </summary>
    /// <remarks>
    /// Se rechaza lo que no tiene sentido en vez de arreglarlo callando: un día
    /// 0 o 45 casi siempre es un error de quien lo escribió, y aceptarlo
    /// «corrigiéndolo» produce un ciclo que nadie pidió y que además parece
    /// correcto.
    /// </remarks>
    public static Outcome<PaySchedule> Of(IEnumerable<int> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        ImmutableArray<int> limpios = [.. days.Distinct().Order()];

        if (limpios.IsEmpty)
        {
            return Outcome.Invalid<PaySchedule>(
                "Hace falta al menos un día de cobro.");
        }

        foreach (int dia in limpios)
        {
            if (dia is < 1 or > 31)
            {
                return Outcome.Invalid<PaySchedule>(
                    $"El día de cobro {dia} no existe: va de 1 a 31.");
            }
        }

        return Outcome.Computed(new PaySchedule(limpios));
    }

    /// <summary>
    /// El ciclo que contiene <paramref name="today"/>.
    /// </summary>
    /// <remarks>
    /// Empieza en el cobro más reciente que no sea posterior a hoy, y termina el
    /// día antes del cobro siguiente. Con un solo día al mes esto da el mismo
    /// resultado que antes; con dos, dos ciclos por mes.
    /// </remarks>
    public BudgetCycle CycleAround(DateOnly today)
    {
        DateOnly inicio = LastPayOnOrBefore(today);
        DateOnly siguiente = FirstPayAfter(inicio);

        // `Between` solo falla si el siguiente no es posterior, y `FirstPayAfter`
        // garantiza que lo es. Se desenvuelve aquí para que quien llama no tenga
        // que tratar un fallo que no puede ocurrir.
        return BudgetCycle.Between(inicio, siguiente).Value;
    }

    /// <summary>El ciclo que sigue al que contiene <paramref name="today"/>.</summary>
    public BudgetCycle NextCycleAfter(DateOnly today)
    {
        BudgetCycle actual = CycleAround(today);
        return CycleAround(actual.End.AddDays(1));
    }

    /// <summary>
    /// Las fechas de cobro de un mes concreto, ordenadas y sin repetir.
    /// </summary>
    /// <remarks>
    /// **Un día que no existe en el mes se corre al último.** Un cobro «el 31»
    /// es como se escribe «fin de mes», y sin este ajuste los meses de treinta
    /// días —y febrero entero— se quedarían sin ese cobro.
    ///
    /// El ajuste puede juntar dos días distintos en la misma fecha: con cobros
    /// el 30 y el 31, en febrero los dos caen en el 28. Se quitan los repetidos
    /// **después** de ajustar, porque si no saldría un ciclo de cero días y
    /// todas las divisiones por días que vienen detrás reventarían.
    /// </remarks>
    public ImmutableArray<DateOnly> PayDatesIn(int year, int month)
    {
        int ultimo = DateTime.DaysInMonth(year, month);

        return
        [
            .. Days
                .Select(d => new DateOnly(year, month, Math.Min(d, ultimo)))
                .Distinct()
                .Order(),
        ];
    }

    private DateOnly LastPayOnOrBefore(DateOnly day)
    {
        foreach (DateOnly fecha in PayDatesIn(day.Year, day.Month).Reverse())
        {
            if (fecha <= day) return fecha;
        }

        // Antes del primer cobro del mes: el ciclo abierto es el que empezó el
        // último cobro del mes anterior.
        DateOnly anterior = day.AddMonths(-1);
        return PayDatesIn(anterior.Year, anterior.Month)[^1];
    }

    private DateOnly FirstPayAfter(DateOnly day)
    {
        foreach (DateOnly fecha in PayDatesIn(day.Year, day.Month))
        {
            if (fecha > day) return fecha;
        }

        DateOnly siguiente = day.AddMonths(1);
        return PayDatesIn(siguiente.Year, siguiente.Month)[0];
    }
}
