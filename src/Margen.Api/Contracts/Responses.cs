namespace Margen.Api.Contracts;

/// <summary>Una página de resultados. El cursor evita el desplazamiento por número.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, int Count);

public sealed record RuleView(
    Guid Id,
    string Pattern,
    string MatchKind,
    Guid CategoryId,
    string? CategoryName,
    int Weight,
    bool IsUserDefined,
    bool IsActive,
    long TimesApplied,
    DateTime? LastAppliedAt);

public sealed record IncomingEmailView(
    Guid Id,
    string Sender,
    string Subject,
    DateTime ReceivedAt,
    DateTime? ProcessedAt,
    string Status,
    string? ParserName,
    int? ParserVersion,
    string? FailureReason,
    int TransactionCount);

public sealed record ReconciliationLineView(
    Guid TransactionId,
    string Merchant,
    long AmountCents,
    DateOnly OccurredOn,
    string Status);

public sealed record ReconciliationSummaryView(
    int Reconciled,
    int Pending,
    int NeedsReview,
    int Duplicate,
    int Rejected,
    IReadOnlyList<ReconciliationLineView> Outstanding);

public sealed record BudgetLineView(
    Guid CategoryId,
    string CategoryName,
    string Priority,
    long AllocatedCents,
    long AdjustmentCents,
    long EffectiveCents,
    long SpentCents,
    long AvailableCents,
    bool CanBeTrimmed);

public sealed record BudgetView(
    Guid PeriodId,
    DateOnly Start,
    DateOnly End,
    bool IsClosed,
    long TotalAllocatedCents,
    long TotalSpentCents,
    long TrimmableCents,
    IReadOnlyList<BudgetLineView> Lines);
