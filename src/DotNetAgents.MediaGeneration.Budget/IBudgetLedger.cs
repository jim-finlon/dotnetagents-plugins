using DotNetAgents.MediaGeneration.Adapters;

namespace DotNetAgents.MediaGeneration.Budget;

/// <summary>
/// Append-only cost ledger. Every successful render / training / audio job emits a row;
/// failed jobs emit a row with <see cref="LedgerEntryStatus.Failed"/> and best-effort GpuSeconds.
/// FR-SP701 + FR-SP703. DB persistence lands in P7.5 follow-up; default impl is in-memory.
/// </summary>
public interface IBudgetLedger
{
    /// <summary>Append one entry (called at job completion). Returns the persisted entry (with Id assigned).</summary>
    Task<BudgetEntry> AppendAsync(BudgetEntry entry, CancellationToken ct);

    /// <summary>Read an entry by id; null when not found.</summary>
    Task<BudgetEntry?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Roll up entries per <see cref="BudgetReportQuery.GroupBy"/>. FR-SP703.</summary>
    Task<IReadOnlyList<CostReportRow>> QueryAsync(BudgetReportQuery query, CancellationToken ct);

    /// <summary>Sum of GpuSeconds for a given actor within [since, until]; used by <see cref="IBudgetGuard"/>.</summary>
    Task<long> SumGpuSecondsForActorAsync(string actorId, DateTimeOffset sinceUtc, DateTimeOffset? untilUtc, CancellationToken ct);
}

/// <summary>
/// Pre-flight budget gate. Caller estimates the request's GpuSeconds (via <see cref="ICostEstimator"/>)
/// and asks the guard whether to admit. Implementations consult a configured per-actor envelope and
/// the actor's recent ledger usage. FR-SP701 acceptance language.
/// </summary>
public interface IBudgetGuard
{
    /// <summary>Inspect an envelope + projected cost; return a verdict (logged on the caller).</summary>
    Task<BudgetVerdict> CheckAsync(string actorId, long projectedGpuSeconds, CancellationToken ct);
}

/// <summary>
/// Projects a request's GpuSeconds before submit (FR-SP702). v1 target: ±20% accuracy when historical
/// samples exist; otherwise the estimator falls back to a defaults table keyed by (QualityTier, LoraStackSize).
/// </summary>
public interface ICostEstimator
{
    Task<CostEstimate> EstimateAsync(CostEstimateRequest request, CancellationToken ct);
}

/// <summary>Input to <see cref="ICostEstimator.EstimateAsync"/>.</summary>
/// <param name="Kind">Kind of work the request will produce.</param>
/// <param name="QualityTier">Target quality tier.</param>
/// <param name="LoraStackSize">Number of LoRAs in the stack (drives VRAM + load time).</param>
/// <param name="DurationMs">Target render duration (ignored for training).</param>
/// <param name="GatewayHost">Optional host hint (mid-flight retake against a known-good slot).</param>
public sealed record CostEstimateRequest(
    LedgerEntryKind Kind,
    QualityTier QualityTier,
    int LoraStackSize,
    int DurationMs,
    string? GatewayHost);
