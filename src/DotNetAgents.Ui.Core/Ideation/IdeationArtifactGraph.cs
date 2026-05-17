using System.Text.Json.Serialization;

namespace DotNetAgents.Ui.Core.Ideation;

// Story 6785da1a (foundation slice of 45b5a173 collaborative canvas). Shared
// artifact-graph contract every downstream slice consumes — persistence
// (sibling 5c951c36), Razor canvas + SignalR hub (sibling be855550),
// Playwright smoke (sibling da8048c6), and the SDLC handoff bridge (sibling
// 0ccc8658). Pure-additive — no UI or persistence code in this slice; just
// the shape every consumer compiles against.
//
// Lives under DotNetAgents.Ui.Core/Ideation so any operator-shell or
// AgentProjects consumer can depend on it without crossing project
// boundaries.

/// <summary>
/// Coarse classification of the kind of node living on an ideation canvas.
/// Per-kind typed payloads decode from <see cref="IdeationNode.PayloadJson"/>;
/// adding a new kind here means adding a payload contract record AND a
/// catalog entry so the validator + Razor renderer stay coherent.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IdeationNodeKind
{
    /// <summary>Free-text idea bubble — the simplest canvas primitive.</summary>
    Idea = 0,
    /// <summary>Structured requirement fragment — title + body + optional rubric anchors.</summary>
    Requirement = 1,
    /// <summary>Question or open prompt the team is working through.</summary>
    Question = 2,
    /// <summary>Decision point with optional accepted/rejected status.</summary>
    Decision = 3,
    /// <summary>Risk callout — what could go wrong if this idea ships.</summary>
    Risk = 4,
    /// <summary>Reference link to an external artifact (URL, doc, story id).</summary>
    Reference = 5,
    /// <summary>Sticky/annotation that scopes commentary to a region.</summary>
    Sticky = 6,
    /// <summary>Sketch or diagram region — payload carries the sketch encoding.</summary>
    Sketch = 7,
    /// <summary>Custom node kind a per-org canvas may declare — payload is opaque to the validator.</summary>
    Custom = 8,
}

/// <summary>
/// Coarse classification of how two nodes relate. Per-kind edges may carry
/// metadata in <see cref="IdeationEdge.MetadataJson"/> (e.g. relation
/// strength, who created the edge). Validator rejects edges whose source or
/// target NodeId doesn't appear in the graph's nodes.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IdeationEdgeKind
{
    /// <summary>Generic association — "these two are related".</summary>
    Relates = 0,
    /// <summary>Source node refines or specializes the target.</summary>
    Refines = 1,
    /// <summary>Source node depends on the target landing first.</summary>
    DependsOn = 2,
    /// <summary>Source node contradicts the target.</summary>
    Contradicts = 3,
    /// <summary>Source node groups the target into a cluster (parent → child).</summary>
    Groups = 4,
    /// <summary>Custom relation kind for per-org extensions.</summary>
    Custom = 5,
}

/// <summary>
/// Versioning marker on every <see cref="IdeationArtifactGraph"/>. SemVer
/// Major-only compatibility (mirrors GenomeSchemaVersion pattern) lets
/// consumers refuse a stale schema rather than silently misinterpreting
/// fields.
/// </summary>
public sealed record IdeationGraphSchemaVersion(int Major, int Minor, int Patch)
{
    public static readonly IdeationGraphSchemaVersion Current = new(1, 0, 0);

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public bool IsCompatibleWith(IdeationGraphSchemaVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Major == other.Major;
    }
}

/// <summary>
/// Spatial position of a node on the canvas. Pixel coordinates relative to
/// the canvas origin. Z-index gives the renderer stacking order; ties broken
/// by NodeId lexically so two clients independently render the same view.
/// </summary>
public sealed record IdeationCanvasPosition(double X, double Y, int ZIndex = 0);

/// <summary>
/// One node on an ideation canvas. Payload is JSON-encoded per-kind so the
/// graph contract stays type-agnostic; the canvas Razor page (sibling) and
/// SDLC handoff bridge (sibling 0ccc8658) decode payloads using the
/// per-kind contracts they own.
/// </summary>
/// <param name="NodeId">Stable id — survives moves, edits, and lineage
/// inheritance. Validator rejects duplicates within a graph.</param>
/// <param name="Kind">Coarse classification driving payload decoding.</param>
/// <param name="Title">Short label for the node — what shows on the canvas
/// without expanding.</param>
/// <param name="PayloadJson">JSON-encoded per-kind payload. Empty string is
/// allowed (e.g. blank Sticky); validator does NOT enforce schema here, the
/// per-kind decoder does.</param>
/// <param name="Position">Spatial location on the canvas.</param>
/// <param name="AuthorActorId">Stable actor id of who created the node;
/// surfaces on the canvas as the author chip.</param>
/// <param name="CreatedAtUtc">When the node was created.</param>
/// <param name="UpdatedAtUtc">When the node was last touched (any edit).</param>
/// <param name="Tags">Optional structured tags for cluster filtering and
/// scenario-builder linkage. Empty list is fine.</param>
public sealed record IdeationNode(
    string NodeId,
    IdeationNodeKind Kind,
    string Title,
    string PayloadJson,
    IdeationCanvasPosition Position,
    string AuthorActorId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<string> Tags);

/// <summary>
/// One edge connecting two nodes on the canvas. Source and target NodeId
/// must both appear in the graph's nodes; validator rejects orphan edges.
/// </summary>
public sealed record IdeationEdge(
    string EdgeId,
    string SourceNodeId,
    string TargetNodeId,
    IdeationEdgeKind Kind,
    string? MetadataJson = null);

/// <summary>
/// Versioned collection of nodes and edges that defines one ideation
/// space's collaborative state. Persistence (sibling 5c951c36) writes this
/// shape verbatim; the Razor canvas hydrates from it; the SDLC handoff
/// bridge (sibling 0ccc8658) snapshots it into structured intake.
/// </summary>
/// <param name="GraphId">Stable identifier — survives persistence
/// reload.</param>
/// <param name="DisplayName">Operator-facing name for the canvas.</param>
/// <param name="SchemaVersion">Marker for forward/backward compat on
/// catalog evolution.</param>
/// <param name="Nodes">Node list. Order is informational; the renderer keys
/// off NodeId + Position.ZIndex.</param>
/// <param name="Edges">Edge list. Validator enforces both ends exist.</param>
/// <param name="CreatedAtUtc">When the canvas was first materialized.</param>
/// <param name="UpdatedAtUtc">Wall-clock of the most recent mutation
/// (node/edge add/edit/remove).</param>
public sealed record IdeationArtifactGraph(
    string GraphId,
    string DisplayName,
    IdeationGraphSchemaVersion SchemaVersion,
    IReadOnlyList<IdeationNode> Nodes,
    IReadOnlyList<IdeationEdge> Edges,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Validation outcome — empty errors when the graph is internally coherent.
/// Mirrors the (Ok, Errors) pattern the ExperimentModeCatalog uses.
/// </summary>
public sealed record IdeationGraphValidation(bool Ok, IReadOnlyList<string> Errors);

/// <summary>
/// Pure-function validator. Enforces structural invariants the canvas
/// renderer + persistence + SDLC handoff bridge all rely on: unique
/// NodeIds, unique EdgeIds, edge endpoints exist, schema compat. Per-kind
/// payload schema is NOT enforced here — that's a sibling decoder's job.
/// </summary>
public static class IdeationArtifactGraphValidator
{
    public static IdeationGraphValidation Validate(IdeationArtifactGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(graph.GraphId))
            errors.Add("graph.GraphId is required.");
        if (!graph.SchemaVersion.IsCompatibleWith(IdeationGraphSchemaVersion.Current))
            errors.Add($"graph.SchemaVersion {graph.SchemaVersion} is incompatible with current {IdeationGraphSchemaVersion.Current}; refusing to load.");

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.NodeId))
            {
                errors.Add("node has empty NodeId.");
                continue;
            }
            if (!nodeIds.Add(node.NodeId))
                errors.Add($"node {node.NodeId}: duplicate NodeId in graph.");
        }

        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in graph.Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.EdgeId))
            {
                errors.Add("edge has empty EdgeId.");
                continue;
            }
            if (!edgeIds.Add(edge.EdgeId))
                errors.Add($"edge {edge.EdgeId}: duplicate EdgeId in graph.");
            if (!nodeIds.Contains(edge.SourceNodeId))
                errors.Add($"edge {edge.EdgeId}: SourceNodeId '{edge.SourceNodeId}' does not exist in the graph.");
            if (!nodeIds.Contains(edge.TargetNodeId))
                errors.Add($"edge {edge.EdgeId}: TargetNodeId '{edge.TargetNodeId}' does not exist in the graph.");
        }

        return new IdeationGraphValidation(errors.Count == 0, errors);
    }
}
