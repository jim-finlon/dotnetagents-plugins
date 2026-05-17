namespace DotNetAgents.Ui.Core;

/// <summary>
/// Read-model contracts for agentic control-loop visibility (lifecycle, queues, evidence, attention).
/// Service hosts map domain state into these shapes; shared Blazor widgets in <c>DotNetAgents.Ui.Blazor</c> render them consistently across Prime Pulse, EcosystemAgent, SDLC consoles, and pilots.
/// </summary>
public enum ControlLoopLifecycleState
{
    Unknown = 0,
    Idle = 1,
    Starting = 2,
    Running = 3,
    Degraded = 4,
    Paused = 5,
    Blocked = 6,
    Draining = 7,
    Stopped = 8,
    Failed = 9
}

/// <summary>How urgently an operator should look at a loop.</summary>
public enum ControlLoopAttentionLevel
{
    None = 0,
    Info = 1,
    ActionRecommended = 2,
    OperatorRequired = 3
}

/// <summary>Queue-oriented signals for retrying / parallel work in a loop.</summary>
public sealed record ControlLoopQueueStats(
    int PendingDepth,
    int InFlight,
    int? MaxDepthBudget = null,
    DateTimeOffset? OldestPendingUtc = null,
    int CompletedCount = 0,
    int FailedCount = 0,
    int RetryingCount = 0,
    string? Backpressure = null);

public enum ControlLoopEvidenceKind
{
    Other = 0,
    Log = 1,
    Trace = 2,
    Metric = 3,
    Artifact = 4,
    Story = 5,
    Deployment = 6
}

/// <summary>Linkable evidence row (logs, traces, stories, deployment runs).</summary>
public sealed record ControlLoopEvidenceRef(
    string Id,
    ControlLoopEvidenceKind Kind,
    string Label,
    string? Href = null,
    DateTimeOffset? CapturedUtc = null,
    UiIntent Intent = UiIntent.Neutral);

/// <summary>Normalized view of one autonomous or supervised loop instance.</summary>
public sealed record ControlLoopSnapshotModel(
    string ServiceId,
    string DisplayName,
    ControlLoopLifecycleState Lifecycle,
    ControlLoopAttentionLevel Attention,
    string? StatusMessage = null,
    ControlLoopQueueStats? Queue = null,
    IReadOnlyList<ControlLoopEvidenceRef>? RecentEvidence = null,
    DateTimeOffset? LastTransitionUtc = null,
    string? CorrelationId = null,
    string? ActiveStoryId = null);

/// <summary>Multi-loop strip for scorecards and fleet views.</summary>
public sealed record ControlLoopScorecardModel(
    string Id,
    string Title,
    IReadOnlyList<ControlLoopSnapshotModel> Loops,
    DateTimeOffset? AsOfUtc = null);
