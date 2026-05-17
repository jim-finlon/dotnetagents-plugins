namespace DotNetAgents.Credentials.Client;

/// <summary>Result of a successful <see cref="ICredentialsClient.GetCredentialAsync"/> call.</summary>
public sealed record CredentialValue(string Category, string Name, string Value);

/// <summary>An entry in <see cref="ICredentialsClient.ListAvailableAsync"/>. The secret value is NOT included.</summary>
public sealed record CredentialListing(string Category, string Name);

/// <summary>Result of <see cref="ICredentialsClient.CheckHealthAsync"/>. <c>Exists</c> can be true with <c>Expired</c> also true.</summary>
public sealed record CredentialHealth(bool Exists, bool Expired);

/// <summary>
/// Normalized live verification bundle for a published DNA agent identity.
/// This is the canonical trust handoff shape for runtime surfaces.
/// </summary>
public sealed record AgentIdentityVerification(
    string AgentId,
    bool Verified,
    bool SignerKnown,
    bool Revoked,
    bool Expired,
    int TrustScore,
    string TrustLevel,
    DateTimeOffset VerifiedAtUtc,
    string VerificationRef,
    string ProvenanceBundleRef,
    string SigningMachineId,
    IReadOnlyList<string> Reasons);

/// <summary>Request payload for issuing a governed autonomous worker auth lease.</summary>
public sealed record WorkerAuthLeaseIssueRequest(
    string AgentId,
    string AgentInstanceId,
    string AgentPoolId,
    string SupervisorActorId,
    string LaneId,
    IReadOnlyList<string> Audiences,
    string? StoryId = null,
    string? WorkRequestId = null,
    string? WorkloadClass = null,
    string? ModelClass = null,
    string? RunnerClass = null,
    IReadOnlyList<string>? ApprovalScope = null,
    IReadOnlyList<string>? SandboxScope = null,
    int? TtlMinutes = null);

/// <summary>Normalized worker auth lease shape returned by CredentialsAgent.</summary>
public sealed record WorkerAuthLease(
    string AuthLeaseId,
    string AgentId,
    string AgentInstanceId,
    string AgentPoolId,
    string SupervisorActorId,
    string LaneId,
    string? StoryId,
    string? WorkRequestId,
    string? WorkloadClass,
    string? ModelClass,
    string? RunnerClass,
    IReadOnlyList<string> Audiences,
    IReadOnlyList<string> ApprovalScope,
    IReadOnlyList<string> SandboxScope,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RenewedAtUtc,
    DateTimeOffset VerifiedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? RevocationReason,
    bool Active,
    IReadOnlyList<string> Reasons,
    string? LeaseToken);

/// <summary>Replay batch returned by CredentialsAgent revocation subscription/resync.</summary>
public sealed record RevocationEventBatch(
    string ConsumerId,
    long AfterSequence,
    long LastSequence,
    IReadOnlyList<DotNetAgents.Abstractions.AgentIdentity.RevocationEvent> Events);

/// <summary>
/// Thrown when CredentialsAgent refuses a call (NOT_AUTHORIZED, NOT_FOUND, TOP_SECRET_BLOCKED, DECRYPT_FAILED).
/// Callers should handle this explicitly — do NOT fall back to env-var reads without logging the bug.
/// </summary>
public sealed class CredentialsClientException : Exception
{
    /// <summary>Machine-readable error code echoed from CredentialsAgent.</summary>
    public string ErrorCode { get; }

    /// <summary>Optional correlation id from the MCP envelope for log-line triage.</summary>
    public string? CorrelationId { get; }

    /// <summary>HTTP status code (0 for transport failures that surface no response).</summary>
    public int HttpStatus { get; }

    public CredentialsClientException(string errorCode, string message, int httpStatus = 0, string? correlationId = null, Exception? inner = null)
        : base(message, inner)
    {
        ErrorCode = errorCode;
        HttpStatus = httpStatus;
        CorrelationId = correlationId;
    }
}
