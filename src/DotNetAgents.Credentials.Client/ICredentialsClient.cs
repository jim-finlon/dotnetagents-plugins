namespace DotNetAgents.Credentials.Client;

/// <summary>
/// Typed client for DNA CredentialsAgent. The canonical secret-fetch surface for every .NET service
/// in the platform — use this instead of env-var reads, config-file secrets, or hand-rolled curl.
/// If a credential you need is not authorized for the calling <c>AgentId</c>, file a HIGH-severity
/// bug in WorkflowService rather than working around it.
/// </summary>
public interface ICredentialsClient
{
    /// <summary>
    /// Retrieve the plaintext value of a credential. Caller must be authorized for the category
    /// (or have an explicit per-credential grant for Confidential tier).
    /// </summary>
    /// <exception cref="CredentialsClientException">
    /// Thrown on NOT_AUTHORIZED, NOT_FOUND, TOP_SECRET_BLOCKED, DECRYPT_FAILED, or transport failure.
    /// </exception>
    Task<CredentialValue> GetCredentialAsync(string category, string name, CancellationToken ct = default);

    /// <summary>
    /// List credentials the calling <c>AgentId</c> is authorized to read (values not included).
    /// Optionally scope to a category prefix (e.g. <c>platform/</c>).
    /// </summary>
    Task<IReadOnlyList<CredentialListing>> ListAvailableAsync(string? categoryPrefix = null, CancellationToken ct = default);

    /// <summary>Check whether a credential exists and whether it has passed its ExpiresAt.</summary>
    Task<CredentialHealth> CheckHealthAsync(string category, string name, CancellationToken ct = default);

    /// <summary>
    /// Request rotation. v1: this logs the request; human approval is still required to replace the value.
    /// </summary>
    Task RotateAsync(string category, string name, CancellationToken ct = default);

    /// <summary>
    /// Perform a live verification of a published DNA agent identity and return a normalized trust bundle
    /// suitable for runtime gates such as browser execution, sandbox provisioning, and async coding claims.
    /// </summary>
    Task<AgentIdentityVerification> VerifyAgentIdentityAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Issue a short-lived worker auth lease for an autonomous AgentInstance. The lease is bound to a lane,
    /// audience set, and supervisor identity, and should be renewed or revoked as the run changes state.
    /// </summary>
    async Task<WorkerAuthLease> IssueWorkerAuthLeaseAsync(WorkerAuthLeaseIssueRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("This ICredentialsClient implementation does not support worker auth leases.");

    /// <summary>
    /// Renew an existing worker auth lease while the autonomous run remains healthy.
    /// </summary>
    async Task<WorkerAuthLease> RenewWorkerAuthLeaseAsync(string authLeaseId, int? ttlMinutes = null, CancellationToken ct = default)
        => throw new NotSupportedException("This ICredentialsClient implementation does not support worker auth lease renewal.");

    /// <summary>
    /// Verify that a worker auth lease is still active and optionally valid for a specific audience.
    /// </summary>
    async Task<WorkerAuthLease> VerifyWorkerAuthLeaseAsync(string authLeaseId, string? audience = null, CancellationToken ct = default)
        => throw new NotSupportedException("This ICredentialsClient implementation does not support worker auth lease verification.");

    /// <summary>
    /// Revoke a worker auth lease so downstream autonomous execution fails closed.
    /// </summary>
    async Task<WorkerAuthLease> RevokeWorkerAuthLeaseAsync(string authLeaseId, string? reason = null, CancellationToken ct = default)
        => throw new NotSupportedException("This ICredentialsClient implementation does not support worker auth lease revocation.");

    /// <summary>
    /// Replay revocation events after a sequence cursor. Consumers persist the returned LastSequence.
    /// </summary>
    async Task<RevocationEventBatch> SubscribeRevocationEventsAsync(string consumerId, string? scope = null, long afterSequence = 0, CancellationToken ct = default)
        => throw new NotSupportedException("This ICredentialsClient implementation does not support revocation event subscriptions.");
}
