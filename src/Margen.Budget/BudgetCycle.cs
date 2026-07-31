namespace Margen.Budget;

/// <summary>
/// El período presupuestario: de un ingreso al siguiente.
/// </summary>
/// <remarks>
/// No va del 1 al 30. Una persona asalariada cobra el 25, y del 25 al 24 es su
/// ciclo real: es cuando el dinero entra y cuando se acaba. Un período de mes
/// natural parte la quincena en dos y produce un «disponible diario» que no se
/// parece a nada.
///
/// Todas las fechas son <see cref="DateOnly"/> en hora de Santo Domingo. El
/// ciclo no tiene hora porque un período empieza un día, no en un instante;
/// darle hora obligaría a elegir una y a defenderla. Quien convierte de UTC a
/// hora local es la capa que llama, y lo hace una sola vez.
/// </remarks>
public sealed record BudgetCycle
{
    private BudgetCycle(DateOnly start, DateOnly end)
    {
        Start = start;
        End = end;
    }

    /// <summary>Primer día, inclusive. Es el día del ingreso que abre.</summary>
    public DateOnly Start { get; }

    /// <summary>Último día, inclusive. Es el día anterior al ingreso siguiente.</summary>
    public DateOnly End { get; }

    public int TotalDays => End.DayNumber - Start.DayNumber + 1;

    /// <summary>
    /// Construye el ciclo entre dos ingresos. El día del ingreso siguiente ya
    /// pertenece al ciclo siguiente.
    /// </summary>
    public static Outcome<BudgetCycle> Between(DateOnly income, DateOnly nextIncome)
    {
        if (nextIncome <= income)
        {
            return Outcome.Invalid<BudgetCycle>(
                "El ingreso siguiente no puede ser anterior ni igual al que abre el ciclo.");
        }

        return Outcome.Computed(
            new BudgetCycle(income, nextIncome.AddDays(-1)));
    }

    /// <summary>Construye a partir de los dos extremos, ambos inclusive.</summary>
    public static Outcome<BudgetCycle> Inclusive(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            return Outcome.Invalid<BudgetCycle>(
                "El último día del ciclo no puede ser anterior al primero.");
        }

        return Outcome.Computed(new BudgetCycle(start, end));
    }

    /// <summary>
    /// Días que faltan contando hoy. Nunca menos de 1.
    /// </summary>
    /// <remarks>
    /// El último día del ciclo, <c>disponible / díasRestantes</c> daría
    /// infinito con cero y una pantalla rota. Fuera del ciclo se acota a los
    /// extremos en lugar de devolver un número sin sentido: un «hoy» posterior
    /// al cierre significa que el ciclo terminó, no que queden -3 días.
    /// </remarks>
    public int DaysRemainingFrom(DateOnly today)
    {
        int remaining = End.DayNumber - today.DayNumber + 1;
        return Math.Clamp(remaining, 1, TotalDays);
    }

    /// <summary>Días transcurridos contando hoy. Entre 1 y <see cref="TotalDays"/>.</summary>
    public int ElapsedDaysAt(DateOnly today) => TotalDays - DaysRemainingFrom(today) + 1;

    public bool Contains(DateOnly date) => date >= Start && date <= End;

    /// <summary>
    /// Fracción del ciclo consumida, como par de enteros.
    /// </summary>
    /// <remarks>
    /// Par de enteros y no <c>double</c>. El cliente usa un <c>double</c> para
    /// dibujar una barra y allí está bien; aquí este número multiplica dinero,
    /// y 0.1 no existe exactamente en binario.
    /// </remarks>
    public (int Elapsed, int Total) ProgressAt(DateOnly today) =>
        (ElapsedDaysAt(today), TotalDays);
}
