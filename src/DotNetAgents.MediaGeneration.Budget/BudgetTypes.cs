using DotNetAgents.MediaGeneration.Adapters;

namespace DotNetAgents.MediaGeneration.Budget;

/// <summary>Kind of work the ledger entry records. Drives grouping in <c>get_cost_report</c>-style queries.</summary>
public enum LedgerEntryKind
{
    /// <summary>A single shot/clip render.</summary>
    Render,
    /// <summary>A LoRA training job.</summary>
    LoraTraining,
    /// <summary>An audio render (XTTS-v2 fallback or composed track).</summary>
    Audio,
}

/// <summary>Final status the ledger persists for an entry. Pending entries are projections, not actuals.</summary>
public enum LedgerEntryStatus
{
    Pending,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>
/// One ledger row per render / training / audio job. FR-SP701: GPU-seconds, gatewayHost, slot,
/// model/adapter metadata, qualityTier, success/failure, operator actorId, storyboardId/characterId.
/// </summary>
public sealed record BudgetEntry(
    Guid Id,
    LedgerEntryKind Kind,
    LedgerEntryStatus Status,
    string ActorId,
    Guid? StoryboardId,
    Guid? CharacterId,
    int? ShotIndex,
    string GatewayHost,
    int Slot,
    QualityTier QualityTier,
    string? LoraStackRef,
    long GpuSeconds,
    int? FrameCount,
    int? DurationMs,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? ErrorCode,
    IReadOnlyDictionary<string, string>? AdditionalMetadata);

/// <summary>Group-by dimension for <see cref="IBudgetLedger.QueryAsync"/>.</summary>
public enum CostReportGroupBy
{
    Character,
    Storyboard,
    GatewayHost,
    Day,
    ActorId,
    QualityTier,
}

/// <summary>Time / scope filter for ledger queries. FR-SP703.</summary>
public sealed record BudgetReportQuery(
    DateTimeOffset SinceUtc,
    DateTimeOffset? UntilUtc,
    CostReportGroupBy GroupBy,
    string? ActorIdFilter = null,
    Guid? CharacterIdFilter = null,
    Guid? StoryboardIdFilter = null,
    string? GatewayHostFilter = null,
    LedgerEntryKind? KindFilter = null);

/// <summary>Single row of a cost report.</summary>
/// <param name="GroupKey">String form of the grouping value (e.g. "baldur", "2026-05-10", "High").</param>
/// <param name="EntryCount">Number of ledger entries rolled up in this row.</param>
/// <param name="GpuSecondsTotal">Sum of GPU-seconds across the rolled-up entries.</param>
/// <param name="SuccessfulEntryCount">Subset of <paramref name="EntryCount"/> with status=Succeeded.</param>
/// <param name="FailedEntryCount">Subset with status=Failed.</param>
public sealed record CostReportRow(
    string GroupKey,
    int EntryCount,
    long GpuSecondsTotal,
    int SuccessfulEntryCount,
    int FailedEntryCount);

/// <summary>Result of a <see cref="ICostEstimator.EstimateAsync"/> call. v1 target: ±20% of measured per FR-SP702.</summary>
/// <param name="ProjectedGpuSeconds">Predicted GPU-seconds for the request.</param>
/// <param name="SampleSize">Number of historical entries the projection was derived from. 0 → no history; estimator falls back to a defaults table.</param>
/// <param name="Method">Free-form identifier for the estimator method used (e.g. "historical-mean", "defaults-table").</param>
public sealed record CostEstimate(long ProjectedGpuSeconds, int SampleSize, string Method);

/// <summary>Per-actor budget envelope consulted by <see cref="IBudgetGuard"/>.</summary>
/// <param name="ActorId">Actor the budget applies to.</param>
/// <param name="GpuSecondsBudget">Hard upper bound on GpuSeconds remaining for this actor in the current window.</param>
/// <param name="WindowStartUtc">When the current accounting window began.</param>
public sealed record BudgetEnvelope(string ActorId, long GpuSecondsBudget, DateTimeOffset WindowStartUtc);

/// <summary>Verdict returned by <see cref="IBudgetGuard.CheckAsync"/>.</summary>
/// <param name="Allowed">True when the request fits within the actor's remaining budget.</param>
/// <param name="Reason">Human-readable rationale (always populated, even on Allowed=true).</param>
/// <param name="RemainingGpuSeconds">Estimated remaining budget after this request would be admitted; null when no envelope exists for the actor.</param>
public sealed record BudgetVerdict(bool Allowed, string Reason, long? RemainingGpuSeconds);
