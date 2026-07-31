using Margen.Budget;

namespace Margen.Api.Budget;

/// <summary>
/// La única clase del sistema que convierte entre UTC y hora de Santo Domingo.
/// </summary>
/// <remarks>
/// La base guarda instantes en <c>timestamptz</c> y el motor trabaja en
/// <see cref="DateOnly"/> de hora local. La traducción entre las dos vive aquí y
/// solo aquí. Repartirla entre las consultas que la necesitan es garantizar que
/// una se olvide, y el síntoma de esa que se olvidó es una compra de las once de
/// la noche que aparece al día siguiente y cambia de período presupuestario.
/// </remarks>
public static class LocalTime
{
    /// <summary>
    /// República Dominicana está en UTC−4 todo el año: no cambia de horario.
    /// Aun así se resuelve por identificador y no con un desfase fijo, porque
    /// un desfase incrustado es una decisión que nadie vuelve a revisar el día
    /// que el país cambie de criterio.
    /// </summary>
    public static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    /// <summary>El día local al que pertenece un instante.</summary>
    public static DateOnly LocalDateOf(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), Zone));

    /// <summary>La hora local completa de un instante. Para mostrar, no para clasificar.</summary>
    public static DateTime LocalTimeOf(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), Zone);

    /// <summary>El instante UTC en que empieza un día local.</summary>
    public static DateTime StartOfLocalDay(DateOnly localDay) =>
        TimeZoneInfo.ConvertTimeToUtc(
            localDay.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            Zone);

    /// <summary>
    /// El rango UTC que cubre un ciclo, medio abierto: <c>[desde, hasta)</c>.
    /// </summary>
    /// <remarks>
    /// Medio abierto y no cerrado a propósito. Consultar
    /// <c>OccurredAt &lt;= fin</c> con el fin convertido a medianoche UTC
    /// dejaría fuera las cuatro últimas horas del último día del ciclo en hora
    /// local, que son las de más gasto. El límite superior es la medianoche
    /// local del día **siguiente** al cierre, exclusiva.
    /// </remarks>
    public static (DateTime From, DateTime UntilExclusive) UtcRangeOf(BudgetCycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        return (StartOfLocalDay(cycle.Start), StartOfLocalDay(cycle.End.AddDays(1)));
    }

    /// <summary>
    /// Normaliza a UTC rechazando lo que no se puede interpretar.
    /// </summary>
    /// <remarks>
    /// Un <see cref="DateTime"/> con <see cref="DateTimeKind.Unspecified"/> no
    /// dice qué instante es. Asumir que es UTC lo correría cuatro horas y
    /// asumir que es local lo correría en la otra dirección; las dos
    /// suposiciones producen una cifra creíble y equivocada. Todo lo que sale
    /// de la base viene con <c>Kind=Utc</c> porque las columnas son
    /// <c>timestamptz</c>, así que llegar aquí sin zona es un defecto de quien
    /// construyó el valor.
    /// </remarks>
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => throw new ArgumentException(
            "Un instante sin zona no se puede convertir sin inventarle una.",
            nameof(value)),
    };
}
