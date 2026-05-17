using System.Collections.Concurrent;

namespace DotNetAgents.Ui.Core.Ideation;

// Story 5c951c36 (foundation slice of 45b5a173 collaborative canvas).
// Persistence interface + in-memory implementation + read-model projector
// the Razor canvas page (sibling be855550), SignalR hub (sibling be855550),
// and SDLC handoff bridge (cross-epic 0ccc8658) all read through.
//
// In-memory first per the story's Constraints — durable EF/Postgres
// persistence is a separate follow-up. The contract is shaped so swapping
// in an EF-backed implementation later doesn't churn callers.

/// <summary>
/// Persistence contract for ideation canvases. Implementations may be
/// in-memory (the default <see cref="InMemoryIdeationGraphStore"/>) or backed
/// by EF/Postgres in a follow-up. All methods are async even when the
/// in-memory implementation is synchronous so callers don't have to change
/// shape when the store swaps.
/// </summary>
public interface IIdeationGraphStore
{
    /// <summary>Returns the canvas with the given id, or null when missing.</summary>
    ValueTask<IdeationArtifactGraph?> GetByIdAsync(string graphId, CancellationToken ct = default);

    /// <summary>Lists all canvas ids the store currently holds. Operator-shell uses this to render a canvas picker.</summary>
    ValueTask<IReadOnlyList<string>> ListGraphIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Insert-or-update a canvas. Validates structural invariants via
    /// <see cref="IdeationArtifactGraphValidator"/> before writing — invalid
    /// graphs throw <see cref="IdeationGraphPersistenceException"/> with
    /// the validation errors so callers can surface them on the canvas UI.
    /// </summary>
    ValueTask<IdeationArtifactGraph> SaveAsync(IdeationArtifactGraph graph, CancellationToken ct = default);

    /// <summary>Delete a canvas. Returns true when removed; false when the graph id wasn't found.</summary>
    ValueTask<bool> DeleteAsync(string graphId, CancellationToken ct = default);
}

/// <summary>
/// Thrown when a save would violate the structural invariants enforced by
/// <see cref="IdeationArtifactGraphValidator"/>. Carries the full error list
/// so callers can render every problem at once instead of fixing one at a
/// time.
/// </summary>
public sealed class IdeationGraphPersistenceException : Exception
{
    public IReadOnlyList<string> ValidationErrors { get; }

    public IdeationGraphPersistenceException(IReadOnlyList<string> validationErrors)
        : base($"Ideation graph save failed validation with {validationErrors.Count} error(s): {string.Join("; ", validationErrors)}")
    {
        ValidationErrors = validationErrors;
    }
}

/// <summary>
/// Default in-memory implementation. Thread-safe via
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>. Suitable for development,
/// tests, and the Razor + SignalR canvas slice (which doesn't need
/// durability for v1). Future EF-backed implementation swaps in via DI.
/// </summary>
public sealed class InMemoryIdeationGraphStore : IIdeationGraphStore
{
    private readonly ConcurrentDictionary<string, IdeationArtifactGraph> _graphs = new(StringComparer.Ordinal);

    public ValueTask<IdeationArtifactGraph?> GetByIdAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        _graphs.TryGetValue(graphId, out var graph);
        return ValueTask.FromResult(graph);
    }

    public ValueTask<IReadOnlyList<string>> ListGraphIdsAsync(CancellationToken ct = default)
    {
        return ValueTask.FromResult<IReadOnlyList<string>>(_graphs.Keys.ToList());
    }

    public ValueTask<IdeationArtifactGraph> SaveAsync(IdeationArtifactGraph graph, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var validation = IdeationArtifactGraphValidator.Validate(graph);
        if (!validation.Ok)
        {
            throw new IdeationGraphPersistenceException(validation.Errors);
        }
        _graphs[graph.GraphId] = graph;
        return ValueTask.FromResult(graph);
    }

    public ValueTask<bool> DeleteAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        return ValueTask.FromResult(_graphs.TryRemove(graphId, out _));
    }
}

/// <summary>
/// Operator-facing read-model summary of an ideation canvas. Distinct from
/// the full <see cref="IdeationArtifactGraph"/>: the canvas picker UI and
/// scorecard tiles want lightweight projection that doesn't pull every node
/// payload across the wire. The Razor cockpit canvas list and the
/// IdeationHandoffSpec preview drawer both read through this.
/// </summary>
/// <param name="GraphId">Stable canvas id.</param>
/// <param name="DisplayName">Operator-facing name.</param>
/// <param name="NodeCount">Total nodes on the canvas.</param>
/// <param name="EdgeCount">Total edges.</param>
/// <param name="NodeKindCounts">Per-<see cref="IdeationNodeKind"/> count for
/// the canvas-picker badges (e.g. "3 risks").</param>
/// <param name="UpdatedAtUtc">Wall-clock of the most-recent mutation.</param>
public sealed record IdeationCanvasReadModel(
    string GraphId,
    string DisplayName,
    int NodeCount,
    int EdgeCount,
    IReadOnlyDictionary<IdeationNodeKind, int> NodeKindCounts,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Pure-function projector — turns an <see cref="IdeationArtifactGraph"/>
/// into a lightweight read-model summary suitable for canvas pickers,
/// scorecard tiles, and the cockpit list view.
/// </summary>
public static class IdeationCanvasReadModelProjector
{
    public static IdeationCanvasReadModel Project(IdeationArtifactGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var counts = new Dictionary<IdeationNodeKind, int>();
        foreach (var node in graph.Nodes)
        {
            counts.TryGetValue(node.Kind, out var existing);
            counts[node.Kind] = existing + 1;
        }
        return new IdeationCanvasReadModel(
            GraphId: graph.GraphId,
            DisplayName: string.IsNullOrWhiteSpace(graph.DisplayName) ? graph.GraphId : graph.DisplayName,
            NodeCount: graph.Nodes.Count,
            EdgeCount: graph.Edges.Count,
            NodeKindCounts: counts,
            UpdatedAtUtc: graph.UpdatedAtUtc);
    }
}
