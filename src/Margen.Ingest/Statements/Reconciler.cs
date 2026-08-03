using Margen.Domain;

namespace Margen.Ingest.Statements;

/// <summary>En qué situación está una línea del estado de cuenta.</summary>
public enum ReconcileState
{
    /// <summary>Conciliado: la línea y un movimiento son lo mismo.</summary>
    Matched,

    /// <summary>Ausente: está en el estado de cuenta y no en la aplicación.</summary>
    Missing,

    /// <summary>Pendiente: está en la aplicación y todavía no en el estado de cuenta.</summary>
    Pending,

    /// <summary>Discrepante: son el mismo movimiento y el monto no coincide.</summary>
    Discrepant,

    /// <summary>Duplicado: dos candidatos igual de buenos, o dos líneas al mismo movimiento.</summary>
    Duplicate,

    /// <summary>Ignorado: alguien dijo que se salte.</summary>
    Ignored,
}

/// <summary>Un movimiento ya registrado, visto desde la conciliación.</summary>
public readonly record struct BookedTransaction(
    Guid Id,
    DateOnly LocalDay,
    string MerchantNormalized,
    Money Amount,
    TxDirection Direction);

/// <summary>El veredicto de una línea del estado de cuenta.</summary>
/// <param name="Line">La línea.</param>
/// <param name="State">En qué situación está.</param>
/// <param name="TransactionId">Con qué movimiento cuadró, si cuadró.</param>
/// <param name="Difference">Cuánto se diferencian, cuando son discrepantes.</param>
public readonly record struct ReconcileVerdict(
    StatementLine Line,
    ReconcileState State,
    Guid? TransactionId,
    Money Difference);

/// <summary>El resultado de cuadrar un estado de cuenta entero.</summary>
/// <param name="Verdicts">Una por línea del archivo.</param>
/// <param name="PendingTransactionIds">Movimientos que el estado de cuenta no menciona.</param>
public sealed record ReconcileReport(
    IReadOnlyList<ReconcileVerdict> Verdicts,
    IReadOnlyList<Guid> PendingTransactionIds)
{
    public int CountOf(ReconcileState state) => Verdicts.Count(v => v.State == state);
}

/// <summary>
/// Cuadra un estado de cuenta contra lo que hay registrado.
/// </summary>
/// <remarks>
/// Es la última red del sistema. La ingesta de correo puede perderse un
/// movimiento —un correo que no llegó, una plantilla nueva, un banco sin parser
/// todavía— y el efectivo depende de que alguien se acuerde de decirlo. El
/// estado de cuenta es la única fuente que lo tiene **todo**, así que lo que
/// salga «ausente» de aquí es exactamente lo que al sistema le faltaba.
///
/// Lógica pura: no sabe de tablas ni de fechas de hoy. Todo entra por parámetro.
/// </remarks>
public static class Reconciler
{
    /// <summary>
    /// Cuántos días de diferencia se toleran entre el día del banco y el nuestro.
    /// </summary>
    /// <remarks>
    /// El banco asienta una compra uno o dos días después de que ocurra, y el
    /// correo de notificación llega el día que ocurre. Sin holgura, cada compra
    /// del fin de semana saldría por duplicado: ausente en el estado de cuenta y
    /// pendiente en la aplicación.
    /// </remarks>
    public const int DayTolerance = 3;

    /// <summary>
    /// Cuadra las líneas contra los movimientos.
    /// </summary>
    /// <remarks>
    /// Un movimiento cuadra con **una sola** línea: en cuanto se usa, deja de
    /// estar disponible. Sin eso, dos cargos idénticos del mismo comercio el
    /// mismo día cuadrarían los dos con el mismo movimiento y el segundo no
    /// saldría como ausente, que es justo el que falta registrar.
    /// </remarks>
    public static ReconcileReport Reconcile(
        IReadOnlyList<StatementLine> lines,
        IReadOnlyList<BookedTransaction> booked)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(booked);

        var used = new HashSet<Guid>();
        var verdicts = new List<ReconcileVerdict>(lines.Count);

        // Por monto exacto primero y por aproximación después: si se resuelve
        // línea por línea en orden, una línea aproximada puede quedarse con el
        // movimiento que le tocaba exacto a otra.
        foreach (StatementLine line in lines)
        {
            verdicts.Add(Judge(line, booked, used, exactOnly: true));
        }

        for (int i = 0; i < verdicts.Count; i++)
        {
            if (verdicts[i].State != ReconcileState.Missing) continue;

            verdicts[i] = Judge(verdicts[i].Line, booked, used, exactOnly: false);
        }

        Guid[] pending = [.. booked.Select(b => b.Id).Where(id => !used.Contains(id))];

        return new ReconcileReport(verdicts, pending);
    }

    private static ReconcileVerdict Judge(
        StatementLine line,
        IReadOnlyList<BookedTransaction> booked,
        HashSet<Guid> used,
        bool exactOnly)
    {
        string needle = Fingerprints.NormalizeMerchant(line.Description);

        List<BookedTransaction> nearby =
        [
            .. booked.Where(b =>
                !used.Contains(b.Id)
                && b.Direction == line.Direction
                && Math.Abs(b.LocalDay.DayNumber - line.Date.DayNumber) <= DayTolerance),
        ];

        List<BookedTransaction> sameAmount =
            [.. nearby.Where(b => b.Amount == line.Amount)];

        if (sameAmount.Count > 0)
        {
            // Varios candidatos con el mismo monto y el mismo día: se prefiere
            // el que además se parece en el nombre. Si ni eso los distingue, es
            // un empate de verdad y se dice, en vez de elegir uno.
            List<BookedTransaction> alike =
                [.. sameAmount.Where(b => Resembles(b.MerchantNormalized, needle))];

            List<BookedTransaction> pool = alike.Count > 0 ? alike : sameAmount;

            if (pool.Count > 1 && alike.Count != 1)
            {
                return new ReconcileVerdict(line, ReconcileState.Duplicate, null, Money.Zero);
            }

            BookedTransaction match = pool[0];
            used.Add(match.Id);

            return new ReconcileVerdict(line, ReconcileState.Matched, match.Id, Money.Zero);
        }

        if (exactOnly)
        {
            return new ReconcileVerdict(line, ReconcileState.Missing, null, Money.Zero);
        }

        // Mismo comercio y mismo día, otro monto: es el mismo movimiento con una
        // cifra distinta —una propina añadida después, un ajuste de divisa— y no
        // un movimiento que falta. Decirlo así es lo que permite corregirlo en
        // vez de duplicarlo.
        BookedTransaction? similar = nearby
            .Where(b => Resembles(b.MerchantNormalized, needle))
            .OrderBy(b => Math.Abs(b.Amount.Cents - line.Amount.Cents))
            .Select(b => (BookedTransaction?)b)
            .FirstOrDefault();

        if (similar is BookedTransaction found)
        {
            used.Add(found.Id);

            return new ReconcileVerdict(
                line,
                ReconcileState.Discrepant,
                found.Id,
                new Money(line.Amount.Cents - found.Amount.Cents));
        }

        return new ReconcileVerdict(line, ReconcileState.Missing, null, Money.Zero);
    }

    /// <summary>
    /// Si dos nombres de comercio son el mismo sitio.
    /// </summary>
    /// <remarks>
    /// Uno contiene al otro, en cualquiera de los dos sentidos. El estado de
    /// cuenta y el correo del mismo banco escriben el mismo comercio con
    /// distinto largo —`SM NACIONAL` y `SM NACIONAL CHARLES`—, y con bancos
    /// distintos la diferencia es mayor todavía.
    ///
    /// Se exige un mínimo de cuatro caracteres: `POS` contenido en cualquier
    /// cosa emparejaría medio estado de cuenta.
    /// </remarks>
    internal static bool Resembles(string a, string b)
    {
        if (a.Length < 4 || b.Length < 4) return false;

        return a.Contains(b, StringComparison.OrdinalIgnoreCase)
            || b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }
}
