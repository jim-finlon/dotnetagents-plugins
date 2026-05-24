using DotNetAgents.MediaGeneration.Adapters;

namespace DotNetAgents.MediaGeneration.Budget;

/// <summary>
/// Default <see cref="ICostEstimator"/>. Projects GpuSeconds from historical samples in the
/// <see cref="IBudgetLedger"/> filtered by (Kind, QualityTier, LoraStackSize bucket). When the
/// sample set is empty, falls back to <see cref="DefaultGpuSecondsTable"/> keyed by
/// (Kind, QualityTier). FR-SP702: v1 target ±20% accuracy with sufficient history.
/// </summary>
public sealed class HistoricalMeanCostEstimator : ICostEstimator
{
    /// <summary>How far back to look for historical samples by default.</summary>
    public static readonly TimeSpan DefaultLookback = TimeSpan.FromDays(30);

    /// <summary>
    /// Fallback table keyed by (kind, quality tier). Values are conservative seed estimates and
    /// surface as <see cref="CostEstimate.Method"/> = "defaults-table" so callers can tell the
    /// estimate didn't come from history.
    /// </summary>
    public static readonly IReadOnlyDictionary<(LedgerEntryKind Kind, QualityTier Tier), long> DefaultGpuSecondsTable =
        new Dictionary<(LedgerEntryKind, QualityTier), long>
        {
            { (LedgerEntryKind.Render, QualityTier.Draft),    8 },
            { (LedgerEntryKind.Render, QualityTier.Standard), 15 },
            { (LedgerEntryKind.Render, QualityTier.High),     40 },
            { (LedgerEntryKind.Render, QualityTier.Cinema),   75 },
            { (LedgerEntryKind.LoraTraining, QualityTier.Draft),    600 },
            { (LedgerEntryKind.LoraTraining, QualityTier.Standard), 2_400 },
            { (LedgerEntryKind.LoraTraining, QualityTier.High),     4_500 },
            { (LedgerEntryKind.LoraTraining, QualityTier.Cinema),   4_500 },
            { (LedgerEntryKind.Audio, QualityTier.Draft),    2 },
            { (LedgerEntryKind.Audio, QualityTier.Standard), 5 },
            { (LedgerEntryKind.Audio, QualityTier.High),     10 },
            { (LedgerEntryKind.Audio, QualityTier.Cinema),   15 },
        };

    private readonly IBudgetLedger _ledger;
    private readonly TimeProvider _time;
    private readonly TimeSpan _lookback;

    public HistoricalMeanCostEstimator(IBudgetLedger ledger, TimeProvider? time = null, TimeSpan? lookback = null)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _time = time ?? TimeProvider.System;
        _lookback = lookback ?? DefaultLookback;
    }

    public async Task<CostEstimate> EstimateAsync(CostEstimateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var since = _time.GetUtcNow() - _lookback;
        // Sample historical entries for the same (Kind, QualityTier). LoraStackSize bucket and
        // duration adjustment happen below so a thin history still produces a usable projection.
        var query = new BudgetReportQuery(
            SinceUtc: since,
            UntilUtc: _time.GetUtcNow(),
            GroupBy: CostReportGroupBy.QualityTier,
            KindFilter: request.Kind);
        var rows = await _ledger.QueryAsync(query, ct).ConfigureAwait(false);

        var match = rows.FirstOrDefault(r => string.Equals(r.GroupKey, request.QualityTier.ToString(), StringComparison.Ordinal));
        if (match is { EntryCount: > 0 })
        {
            // Per-entry mean × duration scaling (for renders; training/audio scale differently).
            var perEntryMean = (double)match.GpuSecondsTotal / match.EntryCount;
            var scaled = request.Kind == LedgerEntryKind.Render
                ? perEntryMean * (Math.Max(1, request.DurationMs) / 8_000.0)   // anchor: 8s clip baseline.
                : perEntryMean;
            // LoRA stack size correction: +5% per LoRA beyond 1.
            var stackMultiplier = 1.0 + (Math.Max(0, request.LoraStackSize - 1) * 0.05);
            var projected = (long)Math.Round(scaled * stackMultiplier);
            return new CostEstimate(projected, match.EntryCount, "historical-mean");
        }

        var fallback = DefaultGpuSecondsTable.TryGetValue((request.Kind, request.QualityTier), out var baseSeconds)
            ? baseSeconds
            : 30;
        if (request.Kind == LedgerEntryKind.Render && request.DurationMs > 0)
        {
            fallback = (long)Math.Round(fallback * (request.DurationMs / 8_000.0));
        }
        return new CostEstimate(fallback, 0, "defaults-table");
    }
}
