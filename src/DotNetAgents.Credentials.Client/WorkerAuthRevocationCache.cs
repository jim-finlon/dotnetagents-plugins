using DotNetAgents.Abstractions.AgentIdentity;

namespace DotNetAgents.Credentials.Client;

public interface IWorkerAuthRevocationCache
{
    long LastSequence { get; }
    void Apply(IEnumerable<RevocationEvent> events);
    Task RefreshAsync(ICredentialsClient credentialsClient, string consumerId, string? scope = null, CancellationToken ct = default);
    bool IsRevoked(WorkerAuthLease lease, DateTimeOffset? now = null);
    bool IsSubjectRevoked(string subjectId);
    bool IsAgentInstanceRevoked(string agentInstanceId);
    bool IsWorkOrderRevoked(string workOrderId);
}

public sealed class InMemoryWorkerAuthRevocationCache : IWorkerAuthRevocationCache
{
    private readonly object _gate = new();
    private readonly HashSet<string> _revokedLeaseIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _revokedSubjectIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _revokedAgentInstanceIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _revokedWorkOrderIds = new(StringComparer.OrdinalIgnoreCase);

    public long LastSequence { get; private set; }

    public void Apply(IEnumerable<RevocationEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        lock (_gate)
        {
            foreach (var evt in events.OrderBy(static item => item.Sequence))
            {
                LastSequence = Math.Max(LastSequence, evt.Sequence);
                if (string.Equals(evt.EventType, RevocationEventTypes.LeaseRevocation, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(evt.AuthLeaseId))
                {
                    _revokedLeaseIds.Add(evt.AuthLeaseId);
                }

                if (string.Equals(evt.EventType, RevocationEventTypes.InstanceRevocation, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(evt.SubjectId))
                {
                    _revokedSubjectIds.Add(evt.SubjectId);
                }

                if (string.Equals(evt.EventType, RevocationEventTypes.InstanceRevocation, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(evt.AgentInstanceId))
                {
                    _revokedAgentInstanceIds.Add(evt.AgentInstanceId);
                }

                if (string.Equals(evt.EventType, RevocationEventTypes.WorkOrderRevocation, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(evt.WorkOrderId))
                {
                    _revokedWorkOrderIds.Add(evt.WorkOrderId);
                }
            }
        }
    }

    public async Task RefreshAsync(ICredentialsClient credentialsClient, string consumerId, string? scope = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(credentialsClient);
        var batch = await credentialsClient.SubscribeRevocationEventsAsync(consumerId, scope, LastSequence, ct).ConfigureAwait(false);
        Apply(batch.Events);
    }

    public bool IsRevoked(WorkerAuthLease lease, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var currentTime = now ?? DateTimeOffset.UtcNow;
        if (!lease.Active || lease.ExpiresAtUtc <= currentTime)
        {
            return true;
        }

        lock (_gate)
        {
            return _revokedLeaseIds.Contains(lease.AuthLeaseId) ||
                   _revokedSubjectIds.Contains(lease.AgentId) ||
                   _revokedAgentInstanceIds.Contains(lease.AgentInstanceId) ||
                   (!string.IsNullOrWhiteSpace(lease.WorkRequestId) && _revokedWorkOrderIds.Contains(lease.WorkRequestId));
        }
    }

    public bool IsSubjectRevoked(string subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return false;
        }

        lock (_gate)
        {
            return _revokedSubjectIds.Contains(subjectId);
        }
    }

    public bool IsAgentInstanceRevoked(string agentInstanceId)
    {
        if (string.IsNullOrWhiteSpace(agentInstanceId))
        {
            return false;
        }

        lock (_gate)
        {
            return _revokedAgentInstanceIds.Contains(agentInstanceId);
        }
    }

    public bool IsWorkOrderRevoked(string workOrderId)
    {
        if (string.IsNullOrWhiteSpace(workOrderId))
        {
            return false;
        }

        lock (_gate)
        {
            return _revokedWorkOrderIds.Contains(workOrderId);
        }
    }
}
