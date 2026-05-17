using DotNetAgents.A2A;
using DotNetAgents.A2A.Client;

namespace DotNetAgents.Interop.MicrosoftAgentFramework;

/// <summary>
/// Adapts a remote Microsoft Agent Framework agent (running an A2A server endpoint) into
/// a DNA <see cref="IA2AAgent"/>. This is the "DNA invokes MAF over A2A" path — the cleanest
/// interop because both sides speak A2A 1.0 natively.
/// </summary>
/// <remarks>
/// <para>
/// Use this adapter when MAF is hosting an A2A server (which MAF v1.0 supports natively per
/// the v1.0 announcement). The DNA host treats the MAF agent as just-another-A2A-endpoint
/// and gets full A2A semantics (Agent Card discovery, sync send, SSE streaming).
/// </para>
/// <para>
/// For the in-process MAF integration where DNA holds a direct C# reference to a MAF
/// <c>AIAgent</c> instance, use <see cref="IMafAgentInvoker"/> instead — that adapter calls
/// MAF directly without going through HTTP/A2A.
/// </para>
/// </remarks>
public sealed class MafA2AAgentAdapter : IA2AAgent
{
    private readonly IA2AClient _a2aClient;
    private readonly Uri _mafBaseUrl;
    private readonly A2AClientCallOptions? _callOptions;
    private readonly string _localAgentId;
    private readonly string _localDisplayName;

    public MafA2AAgentAdapter(
        IA2AClient a2aClient,
        Uri mafBaseUrl,
        string localAgentId,
        string localDisplayName,
        A2AClientCallOptions? callOptions = null)
    {
        _a2aClient = a2aClient ?? throw new ArgumentNullException(nameof(a2aClient));
        _mafBaseUrl = mafBaseUrl ?? throw new ArgumentNullException(nameof(mafBaseUrl));
        _localAgentId = localAgentId ?? throw new ArgumentNullException(nameof(localAgentId));
        _localDisplayName = localDisplayName ?? throw new ArgumentNullException(nameof(localDisplayName));
        _callOptions = callOptions;
    }

    /// <inheritdoc />
    public AgentCard GetAgentCard()
    {
        // The agent card is fetched lazily from the remote MAF endpoint when first requested.
        // For the synchronous IA2AAgent.GetAgentCard surface we return a stub card; consumers
        // who need the live MAF card should call DiscoverAsync on the IA2AClient directly.
        return new AgentCard
        {
            Name = _localDisplayName,
            Description = $"MAF agent at {_mafBaseUrl} (use IA2AClient.DiscoverAsync for live card)",
            Skills = Array.Empty<Skill>(),
            SupportedModes = Array.Empty<string>(),
            Version = "1.0",
        };
    }

    /// <inheritdoc />
    public Task<A2AResponse> HandleTaskAsync(A2ATask task, CancellationToken cancellationToken = default) =>
        _a2aClient.SendTaskAsync(_mafBaseUrl, task, _callOptions, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<A2AEvent> StreamTaskAsync(A2ATask task, CancellationToken cancellationToken = default) =>
        _a2aClient.StreamTaskAsync(_mafBaseUrl, task, _callOptions, cancellationToken);
}
