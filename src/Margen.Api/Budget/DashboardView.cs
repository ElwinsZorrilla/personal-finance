using Margen.Budget;
using Margen.Domain;

namespace Margen.Api.Budget;

/// <summary>
/// Una cifra que puede no existir.
/// </summary>
/// <remarks>
/// Es la forma en el cable de <see cref="Outcome{T}"/>. Cuando el motor no
/// puede calcular, viaja <c>cents = null</c> con el motivo, nunca un cero. En
/// esta pantalla, cero pesos significa «no gastes nada», que es una respuesta
/// concreta a una pregunta que no se pudo responder.
/// </remarks>
public sealed record MoneyValue(long? Cents, string? Unavailable)
{
    public static MoneyValue Of(Domain.Money money) => new(money.Cents, null);

    public static MoneyValue From(Outcome<Domain.Money> outcome) =>
        outcome.IsComputed
            ? new MoneyValue(outcome.Value.Cents, null)
            : new MoneyValue(null, outcome.Reason);
}

public sealed record DeductionView(string Kind, long Cents);

public sealed record PeriodView(
    DateOnly Start,
    DateOnly End,
    DateOnly Today,
    int TotalDays,
    int DaysRemaining,
    int ElapsedDays);

/// <param name="ProjectedCents">
/// Dónde cierra esta categoría al ritmo actual. La calcula el motor, no el
/// cliente: extrapolar en la pantalla sería el segundo sitio donde vive la
/// misma fórmula, y el que se desincroniza.
/// </param>
public sealed record CategoryLineView(
    Guid CategoryId,
    string Name,
    string Priority,
    long AllocatedCents,
    long SpentCents,
    long AvailableCents,
    long ProjectedCents,
    bool WillOverrun,
    bool CanBeTrimmed);

public sealed record CommitmentView(
    Guid Id,
    string Label,
    long AmountCents,
    string Priority,
    DateOnly? DueOn);

public sealed record AlertView(
    Guid Id,
    string Kind,
    string Title,
    string Detail,
    bool IsUrgent,
    DateTime CreatedAt);

public sealed record TransactionView(
    Guid Id,
    string Merchant,
    long AmountCents,
    string Currency,
    DateTime OccurredAt,
    DateOnly OccurredOn,
    string Kind,

    /// <summary>`Inflow`, `Outflow` o `Internal`.</summary>
    string Direction,

    /// <summary>«Ingreso», «Egreso» o «Traspaso». Lo escribe el servidor.</summary>
    string DirectionLabel,

    /// <summary>Entra dinero. Es lo que decide el signo en pantalla.</summary>
    bool IsIncome,

    string Status,
    string Source,
    Guid? CategoryId,
    string? CategoryName,
    string AccountLastFour,
    int ConfidenceBasisPoints,

    /// <summary>
    /// La categoría la puso una persona, no un automatismo.
    /// </summary>
    /// <remarks>
    /// La pantalla de revisión necesita distinguirlo: «lo pusiste tú» y «se
    /// parece a Supermercado» piden atención distinta y sin esto se ven igual.
    /// </remarks>
    bool IsCategoryConfirmed,

    /// <summary>
    /// Qué escalón de la cascada la puso: `UserRule`, `PatternRule`, `History`,
    /// `LocalTable`, `Model`, `Usuario`. Nulo si no tiene categoría.
    /// </summary>
    string? ClassificationSource);

/// <summary>
/// Todo lo que la pantalla principal necesita, ya resuelto.
/// </summary>
/// <remarks>
/// Cifras finales, no ingredientes. El cliente presenta y no calcula: dos
/// implementaciones del mismo cálculo en dos lenguajes es garantía de que se
/// desincronizan. Por eso aquí no hay ninguna lista que haya que sumar en
/// pantalla para obtener una de las cifras de arriba.
/// </remarks>
public sealed record DashboardView(
    PeriodView Period,
    long SafeToSpendCents,
    bool IsOverdrawn,
    long ShortfallCents,
    IReadOnlyList<DeductionView> Deductions,

    // El total de las restas viaja resuelto aunque el desglose vaya al lado.
    // Sumarlo en la pantalla sería calcular en el cliente, y la suma de cinco
    // enteros es exactamente el tipo de cálculo que se cuela sin que nadie lo
    // llame cálculo.
    long TotalDeductedCents,
    long LiquidCents,
    long SafeTodayCents,
    long SpentSoFarCents,
    long ExpectedByNowCents,
    long PaceDeviationCents,
    long ProjectedCloseCents,
    DateOnly? ProjectedDepletion,
    MoneyValue HistoricalBaseline,
    int HistoricalPeriodsConsidered,
    IReadOnlyList<CategoryLineView> Categories,
    IReadOnlyList<CommitmentView> Commitments,
    IReadOnlyList<AlertView> Attention,
    IReadOnlyList<TransactionView> Recent);

/// <summary>Un perfil de lectura de estado de cuenta.</summary>
public sealed record StatementProfileView(
    Guid Id,
    string Name,
    Guid AccountId,
    string? Delimiter,
    int SkipRows,
    int DateColumn,
    string DateFormat,
    int DescriptionColumn,
    int? AmountColumn,
    int? DebitColumn,
    int? CreditColumn,
    string Decimals,
    bool InvertSign);

/// <summary>Una línea del estado de cuenta y en qué situación está.</summary>
public sealed record StatementLineView(
    int LineNumber,
    DateOnly Date,
    string Description,
    long AmountCents,

    /// <summary>`Inflow` u `Outflow`.</summary>
    string Direction,

    /// <summary>`Matched`, `Missing`, `Discrepant`, `Duplicate`, `Pending` o `Ignored`.</summary>
    string State,

    Guid? TransactionId,

    /// <summary>Cuánto se diferencian, cuando son discrepantes.</summary>
    long DifferenceCents);

/// <summary>Una fila que no se pudo leer, y por qué.</summary>
public sealed record RejectedLineView(int LineNumber, string Raw, string Reason);

/// <summary>
/// Lo que se vería al importar, sin haber importado nada.
/// </summary>
/// <remarks>
/// Las filas rechazadas viajan enteras y con su motivo. Un importador que se
/// come en silencio lo que no entiende deja un estado de cuenta que parece
/// cuadrado y no lo está.
/// </remarks>
public sealed record StatementPreviewView(
    string Delimiter,
    IReadOnlyList<StatementLineView> Lines,
    IReadOnlyList<RejectedLineView> Rejected,
    int PendingCount);

/// <summary>Qué hizo una importación.</summary>
public sealed record ImportReportView(
    int Reconciled,
    int Created,
    int Discrepant,
    int Duplicate,
    int Pending,
    int Rejected);

/// <summary>Cuánto asignar a una categoría en el período que viene.</summary>
public sealed record RecommendationView(
    Guid CategoryId,
    string CategoryName,
    long RecommendedCents,

    /// <summary>De qué salió la cifra, en castellano.</summary>
    string Basis);

/// <summary>
/// El presupuesto recomendado para el período que viene.
/// </summary>
/// <remarks>
/// Sale de lo que se gastó de verdad, no de lo que se asignó. Un presupuesto
/// que se copia a sí mismo mes tras mes repite el error del primer mes para
/// siempre.
/// </remarks>
public sealed record RecommendedBudgetView(
    IReadOnlyList<RecommendationView> Categories,
    long TotalCents,

    /// <summary>Lo que sobra del ingreso. **Negativo si no cabe.**</summary>
    long UnallocatedCents,

    /// <summary>
    /// Los compromisos no caben en el ingreso esperado. No es un fallo del
    /// cálculo: es un hecho que hay que ver.
    /// </summary>
    bool DoesNotFit);
