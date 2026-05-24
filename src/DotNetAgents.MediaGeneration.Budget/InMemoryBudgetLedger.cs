using System.Collections.Concurrent;

namespace DotNetAgents.MediaGeneration.Budget;

/// <summary>
/// Thread-safe in-memory <see cref="IBudgetLedger"/>. Composition-friendly default so the agent
/// can boot without DB plumbing. Production P7.5 follow-up swaps this for an EF-backed impl.
/// Append-only: entries are immutable once added.
/// </summary>
public sealed class InMemoryBudgetLedger : IBudgetLedger
{
    private readonly ConcurrentDictionary<Guid, BudgetEntry> _entries = new();

    public Task<BudgetEntry> AppendAsync(BudgetEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // Assign an id when the caller passed Guid.Empty.
        var withId = entry.Id == Guid.Empty ? entry with { Id = Guid.NewGuid() } : entry;
        if (!_entries.TryAdd(withId.Id, withId))
        {
            throw new InvalidOperationException($"Ledger entry '{withId.Id}' already exists (ledger is append-only).");
        }
        return Task.FromResult(withId);
    }

    public Task<BudgetEntry?> GetAsync(Guid id, CancellationToken ct)
    {
        _entries.TryGetValue(id, out var entry);
        return Task.FromResult<BudgetEntry?>(entry);
    }

    public Task<IReadOnlyList<CostReportRow>> QueryAsync(BudgetReportQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<BudgetEntry> rows = _entries.Values
            .Where(e => e.StartedAtUtc >= query.SinceUtc);
        if (query.UntilUtc is { } until)
        {
            rows = rows.Where(e => e.StartedAtUtc <= until);
        }
        if (!string.IsNullOrWhiteSpace(query.ActorIdFilter))
        {
            rows = rows.Where(e => string.Equals(e.ActorId, query.ActorIdFilter, StringComparison.Ordinal));
        }
        if (query.CharacterIdFilter is { } cid) rows = rows.Where(e => e.CharacterId == cid);
        if (query.StoryboardIdFilter is { } sid) rows = rows.Where(e => e.StoryboardId == sid);
        if (!string.IsNullOrWhiteSpace(query.GatewayHostFilter))
        {
            rows = rows.Where(e => string.Equals(e.GatewayHost, query.GatewayHostFilter, StringComparison.Ordinal));
        }
        if (query.KindFilter is { } kind) rows = rows.Where(e => e.Kind == kind);

        var grouped = rows
            .GroupBy(e => GroupKey(e, query.GroupBy))
            .Select(g => new CostReportRow(
                GroupKey: g.Key,
                EntryCount: g.Count(),
                GpuSecondsTotal: g.Sum(e => e.GpuSeconds),
                SuccessfulEntryCount: g.Count(e => e.Status == LedgerEntryStatus.Succeeded),
                FailedEntryCount: g.Count(e => e.Status == LedgerEntryStatus.Failed)))
            .OrderBy(r => r.GroupKey, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult<IReadOnlyList<CostReportRow>>(grouped);
    }

    public Task<long> SumGpuSecondsForActorAsync(string actorId, DateTimeOffset sinceUtc, DateTimeOffset? untilUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return Task.FromResult(0L);
        }
        var rows = _entries.Values
            .Where(e => string.Equals(e.ActorId, actorId, StringComparison.Ordinal) && e.StartedAtUtc >= sinceUtc);
        if (untilUtc is { } until)
        {
            rows = rows.Where(e => e.StartedAtUtc <= until);
        }
        return Task.FromResult(rows.Sum(e => e.GpuSeconds));
    }

    private static string GroupKey(BudgetEntry entry, CostReportGroupBy groupBy) =>
        groupBy switch
        {
            CostReportGroupBy.Character => entry.CharacterId?.ToString() ?? "(none)",
            CostReportGroupBy.Storyboard => entry.StoryboardId?.ToString() ?? "(none)",
            CostReportGroupBy.GatewayHost => string.IsNullOrEmpty(entry.GatewayHost) ? "(none)" : entry.GatewayHost,
            CostReportGroupBy.Day => entry.StartedAtUtc.UtcDateTime.ToString("yyyy-MM-dd"),
            CostReportGroupBy.ActorId => string.IsNullOrEmpty(entry.ActorId) ? "(none)" : entry.ActorId,
            CostReportGroupBy.QualityTier => entry.QualityTier.ToString(),
            _ => "(unknown)",
        };
}
