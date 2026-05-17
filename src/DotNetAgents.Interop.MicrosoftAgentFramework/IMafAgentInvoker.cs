namespace DotNetAgents.Interop.MicrosoftAgentFramework;

/// <summary>
/// Abstraction over a Microsoft Agent Framework v1.0+ <c>AIAgent</c>'s invocation surface.
/// DotNetAgents never references <c>Microsoft.Agents.AI</c> directly — the consuming host
/// injects an implementation that wraps the real MAF agent. This keeps DotNetAgents' framework
/// surface independent of MAF point releases while letting hosts compose the two stacks.
/// </summary>
/// <remarks>
/// <para>
/// Story 68eb40ff (Phase 2B). The interop pattern is: a DNA host that wants to call a MAF
/// agent registers an <see cref="IMafAgentInvoker"/> implementation that internally holds a
/// reference to a Microsoft.Agents.AI.AIAgent and translates DNA's request shape into MAF's.
/// </para>
/// <para>
/// Reverse direction (MAF host calling DNA agents): MAF can call DNA agents directly over
/// A2A 1.0 since DNA shipped DotNetAgents.A2A.Server. No interop adapter required from the
/// DNA side — the MAF host uses any A2A 1.0 client to talk to DNA's `/.well-known/agent.json`
/// and `/a2a/v1/tasks/*` endpoints.
/// </para>
/// </remarks>
public interface IMafAgentInvoker
{
    /// <summary>Stable id of the underlying MAF agent (typically MAF's <c>AIAgent.Id</c>).</summary>
    string MafAgentId { get; }

    /// <summary>Operator-readable display name for the MAF agent.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Invoke the MAF agent with a DNA-shaped task. The implementation translates the input,
    /// calls the underlying <c>AIAgent.RunAsync</c> (or equivalent), and translates the
    /// output back into DNA's shape.
    /// </summary>
    Task<MafInvocationResult> InvokeAsync(MafInvocationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Input shape DNA passes through to a MAF agent invoker.</summary>
public sealed record MafInvocationRequest(
    string TaskInput,
    string? AgentId = null,
    string? TaskId = null,
    IReadOnlyDictionary<string, object>? Metadata = null);

/// <summary>Output shape DNA receives back from a MAF agent invoker.</summary>
public sealed record MafInvocationResult(
    bool IsSuccess,
    string? Output,
    string? ErrorMessage = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    IReadOnlyDictionary<string, object>? Metadata = null)
{
    public static MafInvocationResult Success(string output, int? inputTokens = null, int? outputTokens = null) =>
        new(IsSuccess: true, Output: output, ErrorMessage: null, InputTokens: inputTokens, OutputTokens: outputTokens);

    public static MafInvocationResult Failure(string errorMessage) =>
        new(IsSuccess: false, Output: null, ErrorMessage: errorMessage);
}
