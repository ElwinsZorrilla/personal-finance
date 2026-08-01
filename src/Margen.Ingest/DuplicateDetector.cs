using Margen.Domain;

namespace Margen.Ingest;

/// <summary>Un movimiento ya guardado, visto por el detector.</summary>
public sealed record ExistingTransaction(
    Guid Id,
    Guid AccountId,
    Money Amount,
    DateTime OccurredAtUtc,
    string MerchantNormalized,
    string Fingerprint);

public enum DuplicateVerdict
{
    /// <summary>Movimiento nuevo. Se crea.</summary>
    New,

    /// <summary>
    /// Huella idéntica: es el mismo movimiento. No se crea nada y nadie tiene
    /// que mirarlo.
    /// </summary>
    Exact,

    /// <summary>
    /// Se parece mucho, pero podrían ser dos cargos reales. Se crea marcado y
    /// va a Revisión: quien decide es una persona.
    /// </summary>
    Probable,
}

public sealed record DuplicateCheck(
    DuplicateVerdict Verdict,
    Guid? MatchId,
    string? Reason);

/// <summary>
/// Decide si un movimiento entrante ya existe.
/// </summary>
/// <remarks>
/// La coincidencia exacta se resuelve sola: misma huella, mismo movimiento.
///
/// La aproximada **no** se resuelve sola, y ese es todo el criterio de diseño.
/// Dos cargos iguales en el mismo sitio con minutos de diferencia pueden ser un
/// duplicado del banco o dos cafés seguidos. Descartar el segundo perdería un
/// gasto real y el saldo saldría mayor de lo que es; crearlo sin más duplicaría
/// el gasto. Se crea marcado como <see cref="DuplicateVerdict.Probable"/>, que
/// en el esquema es `TxStatus.Duplicate`: no cuenta para el gasto y aparece en
/// Revisión hasta que alguien diga qué era.
/// </remarks>
public sealed class DuplicateDetector(TimeSpan window)
{
    /// <summary>
    /// Ventana por defecto. Cuatro minutos porque es el orden de magnitud de la
    /// diferencia entre la notificación de una compra y su confirmación, y
    /// sigue siendo poco para dos compras deliberadas en el mismo comercio.
    /// </summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(4);

    private readonly TimeSpan _window = window;

    public DuplicateDetector()
        : this(DefaultWindow)
    {
    }

    public DuplicateCheck Check(
        ParsedTransaction incoming,
        Guid accountId,
        string fingerprint,
        IReadOnlyCollection<ExistingTransaction> existing)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(existing);

        foreach (ExistingTransaction candidate in existing)
        {
            if (string.Equals(candidate.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return new DuplicateCheck(
                    DuplicateVerdict.Exact,
                    candidate.Id,
                    "Ya hay un movimiento con la misma huella.");
            }
        }

        string merchant = Fingerprints.NormalizeMerchant(incoming.MerchantRaw);

        foreach (ExistingTransaction candidate in existing)
        {
            if (candidate.AccountId != accountId) continue;
            if (candidate.Amount != incoming.Amount) continue;
            if (!string.Equals(candidate.MerchantNormalized, merchant, StringComparison.Ordinal))
            {
                continue;
            }

            TimeSpan apart = (candidate.OccurredAtUtc - incoming.OccurredAtUtc).Duration();
            if (apart > _window) continue;

            return new DuplicateCheck(
                DuplicateVerdict.Probable,
                candidate.Id,
                $"Mismo importe y comercio con {apart.TotalMinutes:0} minuto(s) "
                + "de diferencia.");
        }

        return new DuplicateCheck(DuplicateVerdict.New, null, null);
    }
}
