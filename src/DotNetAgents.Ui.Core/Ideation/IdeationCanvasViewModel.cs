namespace DotNetAgents.Ui.Core.Ideation;

/// <summary>
/// Operator-facing state package for an ideation canvas page. The full graph is
/// paired with collaboration metadata so Blazor hosts can render the canvas
/// without inventing a separate view-model shape per service.
/// </summary>
public sealed record IdeationCanvasViewModel(
    IdeationArtifactGraph Graph,
    IReadOnlyList<IdeationParticipantViewModel> Participants,
    IReadOnlyList<IdeationCursorPresence> Cursors,
    string? ConflictMessage = null)
{
    public static IdeationCanvasViewModel FromGraph(IdeationArtifactGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new IdeationCanvasViewModel(graph, Array.Empty<IdeationParticipantViewModel>(), Array.Empty<IdeationCursorPresence>());
    }
}

public sealed record IdeationParticipantViewModel(
    string ActorId,
    string DisplayName,
    DateTimeOffset LastSeenUtc);

public sealed record IdeationCursorPresence(
    string ActorId,
    string DisplayName,
    double X,
    double Y,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Deterministic graph mutation helpers shared by the SignalR hub and tests.
/// </summary>
public static class IdeationGraphMutator
{
    public static IdeationArtifactGraph Empty(string graphId, string? displayName = null, DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        var timestamp = now ?? DateTimeOffset.UtcNow;
        return new IdeationArtifactGraph(
            GraphId: graphId,
            DisplayName: string.IsNullOrWhiteSpace(displayName) ? graphId : displayName,
            SchemaVersion: IdeationGraphSchemaVersion.Current,
            Nodes: Array.Empty<IdeationNode>(),
            Edges: Array.Empty<IdeationEdge>(),
            CreatedAtUtc: timestamp,
            UpdatedAtUtc: timestamp);
    }

    public static IdeationArtifactGraph UpsertNode(IdeationArtifactGraph graph, IdeationNode node, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        var nodes = graph.Nodes.Where(n => !StringComparer.Ordinal.Equals(n.NodeId, node.NodeId)).ToList();
        nodes.Add(node with { UpdatedAtUtc = now ?? DateTimeOffset.UtcNow });
        return Touch(graph with { Nodes = nodes }, now);
    }

    public static IdeationArtifactGraph DeleteNode(IdeationArtifactGraph graph, string nodeId, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        var nodes = graph.Nodes.Where(n => !StringComparer.Ordinal.Equals(n.NodeId, nodeId)).ToList();
        var edges = graph.Edges
            .Where(e => !StringComparer.Ordinal.Equals(e.SourceNodeId, nodeId) && !StringComparer.Ordinal.Equals(e.TargetNodeId, nodeId))
            .ToList();
        return Touch(graph with { Nodes = nodes, Edges = edges }, now);
    }

    public static IdeationArtifactGraph UpsertEdge(IdeationArtifactGraph graph, IdeationEdge edge, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(edge);
        var edges = graph.Edges.Where(e => !StringComparer.Ordinal.Equals(e.EdgeId, edge.EdgeId)).ToList();
        edges.Add(edge);
        return Touch(graph with { Edges = edges }, now);
    }

    public static IdeationArtifactGraph DeleteEdge(IdeationArtifactGraph graph, string edgeId, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(edgeId);
        return Touch(graph with { Edges = graph.Edges.Where(e => !StringComparer.Ordinal.Equals(e.EdgeId, edgeId)).ToList() }, now);
    }

    private static IdeationArtifactGraph Touch(IdeationArtifactGraph graph, DateTimeOffset? now)
    {
        return graph with { UpdatedAtUtc = now ?? DateTimeOffset.UtcNow };
    }
}
