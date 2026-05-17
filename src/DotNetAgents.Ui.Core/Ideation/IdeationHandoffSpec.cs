using System.Text.Json.Serialization;

namespace DotNetAgents.Ui.Core.Ideation;

// Story 978d883e (foundation slice of 0ccc8658 ideation -> SDLC bridge).
// Snapshots an IdeationArtifactGraph (story 6785da1a) into a structured DTO
// the WorkflowService submit endpoint (sibling 277aa3cf) ingests to spawn one
// epic + N stories. Pure-additive — no SDLC writes; downstream MCP/HTTP
// surface owns the persistence side.
//
// The projector here is a deterministic best-effort heuristic: every
// Requirement node becomes a story, every Idea/Question/Sketch becomes a
// candidate, and edges become traceability annotations on the resulting
// drafts. Operators tune the result before submitting; the cockpit UI
// (sibling 3731f1da) renders the drafts in a drawer for review.

/// <summary>
/// Coarse classification of a draft entry produced from an ideation node.
/// Distinct from <see cref="IdeationNodeKind"/>: the node kind is what the
/// operator drew on the canvas; this category is what the SDLC submit
/// endpoint will create.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IdeationDraftKind
{
    /// <summary>Becomes one SDLC story when submitted.</summary>
    Story = 0,
    /// <summary>Becomes one SDLC epic when submitted.</summary>
    Epic = 1,
    /// <summary>Background context attached to the umbrella epic; not its own story.</summary>
    Note = 2,
    /// <summary>Risk callout — surfaces on the umbrella epic's RiskNotes.</summary>
    Risk = 3,
}

/// <summary>
/// Single story-shaped draft produced from one or more ideation nodes.
/// Carries the source node ids so the submit endpoint can record provenance
/// on the resulting SDLC story (which originating nodes mapped here).
/// </summary>
/// <param name="DraftId">Stable identifier within the handoff spec; the
/// submit endpoint maps it to a real SDLC story id post-creation.</param>
/// <param name="Kind">What the submit endpoint creates from this draft.</param>
/// <param name="Title">Operator-facing title — mirrors what becomes the
/// SDLC story title.</param>
/// <param name="Description">Long-form description — mirrors what becomes
/// the SDLC story description.</param>
/// <param name="AcceptanceCriteria">Optional structured AC bullets the
/// projector synthesized; submit endpoint passes through verbatim.</param>
/// <param name="SourceNodeIds">Provenance — which IdeationNode(s) on the
/// canvas produced this draft. Submit endpoint records on the resulting
/// story for traceability.</param>
/// <param name="Tags">Tags inherited from the source nodes plus any
/// projector-applied taxonomy tags.</param>
public sealed record IdeationStoryDraft(
    string DraftId,
    IdeationDraftKind Kind,
    string Title,
    string Description,
    string? AcceptanceCriteria,
    IReadOnlyList<string> SourceNodeIds,
    IReadOnlyList<string> Tags);

/// <summary>
/// Umbrella epic draft produced from the canvas as a whole. Submit endpoint
/// creates this epic first, then attaches every story-kind draft to it.
/// </summary>
public sealed record IdeationEpicDraft(
    string Title,
    string Description,
    IReadOnlyList<string> SourceNodeIds);

/// <summary>
/// Snapshot of an ideation canvas in a shape the SDLC submit endpoint can
/// ingest deterministically. Not a persistence model — the canvas itself
/// stays in <see cref="IdeationArtifactGraph"/>; this record is a transient
/// projection produced at submit time.
/// </summary>
/// <param name="GraphId">Source canvas id — provenance link.</param>
/// <param name="Epic">Umbrella epic the submit endpoint creates.</param>
/// <param name="Drafts">Story / Note / Risk drafts to attach under the
/// umbrella epic. Order is preserved for predictable storyboarding.</param>
/// <param name="GeneratedAtUtc">When the projector ran. Lets the submit
/// endpoint refuse stale specs if the canvas has moved on since.</param>
public sealed record IdeationHandoffSpec(
    string GraphId,
    IdeationEpicDraft Epic,
    IReadOnlyList<IdeationStoryDraft> Drafts,
    DateTimeOffset GeneratedAtUtc);

/// <summary>
/// Pure-function projector — turns an <see cref="IdeationArtifactGraph"/>
/// into an <see cref="IdeationHandoffSpec"/>. Deterministic. No side
/// effects. Cockpit UI re-runs this on every canvas mutation to keep the
/// submit drawer's preview in sync.
/// </summary>
public static class IdeationHandoffProjector
{
    /// <summary>Tag the projector adds to drafts produced from <see cref="IdeationNodeKind.Risk"/> nodes; surfaces on cockpit + SDLC.</summary>
    public const string ProjectedRiskTag = "projected:risk";

    /// <summary>Tag the projector adds to drafts produced from <see cref="IdeationNodeKind.Sticky"/> or <see cref="IdeationNodeKind.Reference"/> nodes; surfaces on cockpit + SDLC.</summary>
    public const string ProjectedNoteTag = "projected:note";

    public static IdeationHandoffSpec Project(IdeationArtifactGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var drafts = new List<IdeationStoryDraft>();
        foreach (var node in graph.Nodes)
        {
            var draft = ProjectNode(node);
            if (draft is not null) drafts.Add(draft);
        }

        var epic = new IdeationEpicDraft(
            Title: string.IsNullOrWhiteSpace(graph.DisplayName) ? graph.GraphId : graph.DisplayName,
            Description: BuildEpicDescription(graph),
            SourceNodeIds: graph.Nodes.Select(n => n.NodeId).ToList());

        return new IdeationHandoffSpec(
            GraphId: graph.GraphId,
            Epic: epic,
            Drafts: drafts,
            GeneratedAtUtc: graph.UpdatedAtUtc);
    }

    private static IdeationStoryDraft? ProjectNode(IdeationNode node)
    {
        // Custom + Sketch are operator-only signals on the canvas — they don't
        // map to discrete SDLC items. Surface them on the umbrella epic
        // description instead via BuildEpicDescription.
        return node.Kind switch
        {
            IdeationNodeKind.Requirement or IdeationNodeKind.Decision => new IdeationStoryDraft(
                DraftId: node.NodeId,
                Kind: IdeationDraftKind.Story,
                Title: node.Title,
                Description: node.PayloadJson,
                AcceptanceCriteria: null,
                SourceNodeIds: new[] { node.NodeId },
                Tags: node.Tags),

            IdeationNodeKind.Idea => new IdeationStoryDraft(
                DraftId: node.NodeId,
                Kind: IdeationDraftKind.Story,
                Title: node.Title,
                Description: node.PayloadJson,
                AcceptanceCriteria: null,
                SourceNodeIds: new[] { node.NodeId },
                Tags: node.Tags),

            IdeationNodeKind.Question => new IdeationStoryDraft(
                DraftId: node.NodeId,
                Kind: IdeationDraftKind.Note,
                Title: node.Title,
                Description: node.PayloadJson,
                AcceptanceCriteria: null,
                SourceNodeIds: new[] { node.NodeId },
                Tags: AppendTag(node.Tags, ProjectedNoteTag)),

            IdeationNodeKind.Risk => new IdeationStoryDraft(
                DraftId: node.NodeId,
                Kind: IdeationDraftKind.Risk,
                Title: node.Title,
                Description: node.PayloadJson,
                AcceptanceCriteria: null,
                SourceNodeIds: new[] { node.NodeId },
                Tags: AppendTag(node.Tags, ProjectedRiskTag)),

            IdeationNodeKind.Reference or IdeationNodeKind.Sticky => new IdeationStoryDraft(
                DraftId: node.NodeId,
                Kind: IdeationDraftKind.Note,
                Title: node.Title,
                Description: node.PayloadJson,
                AcceptanceCriteria: null,
                SourceNodeIds: new[] { node.NodeId },
                Tags: AppendTag(node.Tags, ProjectedNoteTag)),

            // Sketch + Custom: skipped — they surface on the epic description.
            _ => null,
        };
    }

    private static string BuildEpicDescription(IdeationArtifactGraph graph)
    {
        var nodeCount = graph.Nodes.Count;
        var edgeCount = graph.Edges.Count;
        var skipped = graph.Nodes.Count(n =>
            n.Kind == IdeationNodeKind.Sketch || n.Kind == IdeationNodeKind.Custom);
        var skippedClause = skipped > 0
            ? $" {skipped} sketch/custom node(s) are summarized here but not promoted to discrete drafts."
            : string.Empty;
        return $"Ideation handoff from canvas '{graph.DisplayName}' ({graph.GraphId}). " +
               $"{nodeCount} node(s), {edgeCount} edge(s).{skippedClause}";
    }

    private static IReadOnlyList<string> AppendTag(IReadOnlyList<string> existing, string tag)
    {
        if (existing.Contains(tag, StringComparer.Ordinal)) return existing;
        var combined = new List<string>(existing.Count + 1);
        combined.AddRange(existing);
        combined.Add(tag);
        return combined;
    }
}
