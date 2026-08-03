using System.Globalization;
using Margen.Domain;

namespace Margen.Classify;

/// <summary>Algo que merece que alguien lo mire.</summary>
/// <param name="Kind">Qué clase de alerta.</param>
/// <param name="Title">Una línea, la que se lee en la lista.</param>
/// <param name="Detail">El porqué, con las cifras.</param>
/// <param name="DedupeKey">
/// Con qué se reconoce que esta alerta ya existe. Es lo que impide que mirar el
/// reloj diez veces genere diez alertas.
/// </param>
/// <param name="IsUrgent">Si sube al panel principal.</param>
/// <param name="TransactionId">El movimiento que la causó, si fue uno.</param>
/// <param name="RecurringPaymentId">El compromiso que la causó, si fue uno.</param>
/// <remarks>
/// Los dos identificadores viajan aquí y **no se sacan luego de la clave de
/// deduplicación**. Reconstruirlos partiendo la clave por los dos puntos
/// obligaba a quien escribe la alerta a conocer el formato que inventó quien la
/// detecta, y el día que una clave lleve un dato con dos puntos dentro, la
/// alerta se guarda apuntando a la nada sin que falle nada.
/// </remarks>
public readonly record struct Anomaly(
    AlertKind Kind,
    string Title,
    string Detail,
    string DedupeKey,
    bool IsUrgent,
    Guid? TransactionId = null,
    Guid? RecurringPaymentId = null);

/// <summary>
/// Las cuatro detecciones de la fase, cada una sin saber nada de tablas.
/// </summary>
/// <remarks>
/// Todas devuelven <c>null</c> cuando no hay nada que decir, y ninguna asume
/// nada cuando le faltan datos. Una alerta que se equivoca dos veces se deja de
/// leer, y a partir de ahí da igual lo bien que detecte la tercera.
/// </remarks>
public static class Anomalies
{
    /// <summary>
    /// Variación de precio a partir de la cual una suscripción se avisa: 500
    /// puntos básicos, un cinco por ciento.
    /// </summary>
    /// <remarks>
    /// Por debajo de eso, un cargo en dólares que cambia de tasa parece una
    /// subida de precio y no lo es.
    /// </remarks>
    public const int SubscriptionChangeThreshold = 500;

    /// <summary>
    /// Días de cortesía antes de avisar de un recurrente que no llegó.
    /// </summary>
    /// <remarks>
    /// El banco no notifica el mismo día, y un aviso el día del vencimiento es
    /// un aviso que casi siempre se equivoca.
    /// </remarks>
    public const int GraceDays = 3;

    /// <summary>
    /// Un cargo que se sale del rango normal de su comercio.
    /// </summary>
    /// <remarks>
    /// El rango entra ya calculado y como <see cref="Outcome{T}"/>: si no hay
    /// suficiente historial, no hay alerta. Ese es el camino más frecuente los
    /// primeros meses y tiene que ser barato y silencioso.
    ///
    /// Solo se avisa **por arriba**. Un cargo más barato de lo normal en el
    /// súper no es un problema de nadie, y avisarlo enseña a ignorar la lista.
    /// </remarks>
    public static Anomaly? UnusualAmount(
        Guid transactionId,
        string merchantNormalized,
        Money amount,
        Outcome<NormalRange> range)
    {
        ArgumentNullException.ThrowIfNull(merchantNormalized);
        ArgumentNullException.ThrowIfNull(range);

        if (!range.IsComputed) return null;

        NormalRange normal = range.Value;
        if (amount <= normal.High) return null;

        return new Anomaly(
            AlertKind.UnusualAmount,
            $"Cargo inusual en {merchantNormalized}",
            $"Se cobró {Format(amount)} y lo normal aquí ronda {Format(normal.Center)} "
            + $"({normal.SampleCount} cargos anteriores).",
            $"unusual:{transactionId:D}",
            IsUrgent: false,
            TransactionId: transactionId);
    }

    /// <summary>
    /// Una suscripción que empezó a cobrar otra cosa.
    /// </summary>
    /// <remarks>
    /// El importe nuevo entra en la clave de deduplicación a propósito: si sube
    /// otra vez, es una alerta nueva y no la misma. Sin eso, la segunda subida
    /// sería invisible por culpa de la primera.
    /// </remarks>
    public static Anomaly? SubscriptionPriceChange(
        Guid recurringPaymentId,
        string label,
        Money expected,
        Money charged)
    {
        ArgumentNullException.ThrowIfNull(label);

        Outcome<int> change = Statistics.ChangeInBasisPoints(expected, charged);
        if (!change.IsComputed) return null;

        int basisPoints = change.Value;
        if (Math.Abs(basisPoints) < SubscriptionChangeThreshold) return null;

        bool subio = basisPoints > 0;

        return new Anomaly(
            AlertKind.SubscriptionChange,
            $"{label} {(subio ? "subió" : "bajó")} de precio",
            $"Se esperaba {Format(expected)} y se cobró {Format(charged)}: "
            + $"{FormatBasisPoints(basisPoints)}.",
            $"subprice:{recurringPaymentId:D}:{charged.Cents.ToString(CultureInfo.InvariantCulture)}",

            // Una subida pide decisión —seguir pagándola o darse de baja—; una
            // bajada es una buena noticia que puede esperar.
            IsUrgent: subio,
            RecurringPaymentId: recurringPaymentId);
    }

    /// <summary>
    /// Una factura recurrente que venció y no apareció.
    /// </summary>
    /// <remarks>
    /// <paramref name="dueDate"/> y <paramref name="today"/> son fechas
    /// **locales**, y esa es la decisión de zona horaria de esta detección: una
    /// factura que vence el 31 no está vencida a las 20:00 del 31 en Santo
    /// Domingo aunque en UTC ya sea día 1. Convertir aquí sería tarde; el
    /// llamador convierte y pasa el día que ve el usuario.
    /// </remarks>
    public static Anomaly? MissingRecurring(
        Guid recurringPaymentId,
        string label,
        DateOnly dueDate,
        DateOnly today,
        bool alreadyPaid)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (alreadyPaid) return null;

        DateOnly deadline = dueDate.AddDays(GraceDays);
        if (today <= deadline) return null;

        return new Anomaly(
            AlertKind.MissingRecurring,
            $"No llegó el cargo de {label}",
            $"Vencía el {dueDate:dd/MM/yyyy} y han pasado "
            + $"{today.DayNumber - dueDate.DayNumber} días sin cargo.",

            // El vencimiento entra en la clave, en fecha local: la factura de
            // agosto no es la misma alerta que la de julio.
            $"missing:{recurringPaymentId:D}:{dueDate:yyyy-MM-dd}",
            IsUrgent: true,
            RecurringPaymentId: recurringPaymentId);
    }

    private static string Format(Money amount) =>
        (amount.Cents / 100).ToString("N0", CultureInfo.InvariantCulture)
        + "."
        + Math.Abs(amount.Cents % 100).ToString("D2", CultureInfo.InvariantCulture);

    private static string FormatBasisPoints(int basisPoints)
    {
        int whole = Math.Abs(basisPoints) / 100;
        int fraction = Math.Abs(basisPoints) % 100;
        string sign = basisPoints < 0 ? "-" : "+";

        return fraction == 0
            ? $"{sign}{whole.ToString(CultureInfo.InvariantCulture)} %"
            : $"{sign}{whole.ToString(CultureInfo.InvariantCulture)},"
              + $"{fraction.ToString("D2", CultureInfo.InvariantCulture)} %";
    }
}
