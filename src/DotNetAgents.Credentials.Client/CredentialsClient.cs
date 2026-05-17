using System.Net.Http.Json;
using System.Text.Json;
using DotNetAgents.A2A;
using DotNetAgents.A2A.Client;
using DotNetAgents.Abstractions.AgentIdentity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetAgents.Credentials.Client;

/// <summary>
/// HTTP implementation of <see cref="ICredentialsClient"/>. Posts to the CredentialsAgent MCP
/// surface at <c>/mcp/tools/call</c> and maps the envelope into typed results + explicit exceptions.
/// </summary>
public sealed class CredentialsClient : ICredentialsClient
{
    /// <summary>Named HttpClient key used by <c>AddCredentialsClient</c>.</summary>
    public const string HttpClientName = "DotNetAgents.Credentials.Client";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly CredentialsClientOptions _options;
    private readonly ILogger<CredentialsClient> _log;
    private readonly IA2AClient? _a2aClient;

    public CredentialsClient(
        HttpClient http,
        IOptions<CredentialsClientOptions> options,
        ILogger<CredentialsClient> log,
        IA2AClient? a2aClient = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _a2aClient = a2aClient;

        if (string.IsNullOrWhiteSpace(_options.AgentId))
            throw new InvalidOperationException("CredentialsClientOptions.AgentId is required — pick the stable actor id this service presents to CredentialsAgent.");
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw new InvalidOperationException("CredentialsClientOptions.BaseUrl is required.");

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));

        var ingressKey = _options.McpIngressApiKey?.Trim();
        if (!string.IsNullOrEmpty(ingressKey))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", ingressKey);
    }

    public async Task<CredentialValue> GetCredentialAsync(string category, string name, CancellationToken ct = default)
    {
        var envelope = await CallAsync("credentials_get_credential", new Dictionary<string, object?>
        {
            ["category"] = category,
            ["name"] = name,
            ["agentId"] = _options.AgentId,
        }, ct).ConfigureAwait(false);

        var root = envelope.Result;
        if (root is null) throw Fail(envelope, "credentials_get_credential result was null");
        var cat = root.Value.TryGetProperty("category", out var c) ? c.GetString() ?? category : category;
        var nm = root.Value.TryGetProperty("name", out var n) ? n.GetString() ?? name : name;
        var value = root.Value.TryGetProperty("value", out var v) ? v.GetString() : null;
        if (value is null) throw Fail(envelope, "credentials_get_credential result had no 'value' field");
        return new CredentialValue(cat, nm, value);
    }

    public async Task<IReadOnlyList<CredentialListing>> ListAvailableAsync(string? categoryPrefix = null, CancellationToken ct = default)
    {
        var args = new Dictionary<string, object?> { ["agentId"] = _options.AgentId };
        if (!string.IsNullOrWhiteSpace(categoryPrefix)) args["category"] = categoryPrefix;

        var envelope = await CallAsync("credentials_list_available", args, ct).ConfigureAwait(false);
        var root = envelope.Result;
        if (root is null) return Array.Empty<CredentialListing>();
        if (!root.Value.TryGetProperty("credentials", out var items) || items.ValueKind != JsonValueKind.Array)
            return Array.Empty<CredentialListing>();

        var list = new List<CredentialListing>(items.GetArrayLength());
        foreach (var item in items.EnumerateArray())
        {
            var cat = item.TryGetProperty("category", out var c) ? c.GetString() : null;
            var nm = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (cat is not null && nm is not null) list.Add(new CredentialListing(cat, nm));
        }
        return list;
    }

    public async Task<CredentialHealth> CheckHealthAsync(string category, string name, CancellationToken ct = default)
    {
        var envelope = await CallAsync("credentials_check_health", new Dictionary<string, object?>
        {
            ["category"] = category,
            ["name"] = name,
            ["agentId"] = _options.AgentId,
        }, ct).ConfigureAwait(false);

        var root = envelope.Result;
        if (root is null) throw Fail(envelope, "credentials_check_health result was null");
        bool exists = root.Value.TryGetProperty("exists", out var e) && e.ValueKind == JsonValueKind.True;
        bool expired = root.Value.TryGetProperty("expired", out var ex) && ex.ValueKind == JsonValueKind.True;
        return new CredentialHealth(exists, expired);
    }

    public async Task RotateAsync(string category, string name, CancellationToken ct = default)
    {
        await CallAsync("credentials_rotate", new Dictionary<string, object?>
        {
            ["category"] = category,
            ["name"] = name,
            ["agentId"] = _options.AgentId,
        }, ct).ConfigureAwait(false);
    }

    public async Task<AgentIdentityVerification> VerifyAgentIdentityAsync(string agentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var verificationEnvelope = await CallAsync("verify_agent_identity", new Dictionary<string, object?>
        {
            ["agentId"] = agentId,
        }, ct).ConfigureAwait(false);
        var trustEnvelope = await CallAsync("get_agent_trust_score", new Dictionary<string, object?>
        {
            ["agentId"] = agentId,
        }, ct).ConfigureAwait(false);

        var verificationRoot = verificationEnvelope.Result;
        var trustRoot = trustEnvelope.Result;
        if (verificationRoot is null) throw Fail(verificationEnvelope, "verify_agent_identity result was null");
        if (trustRoot is null) throw Fail(trustEnvelope, "get_agent_trust_score result was null");

        var verifiedAtUtc = verificationRoot.Value.TryGetProperty("verifiedAtUtc", out var verifiedAtElement) &&
                            verifiedAtElement.ValueKind == JsonValueKind.String &&
                            DateTimeOffset.TryParse(verifiedAtElement.GetString(), out var verifiedAt)
            ? verifiedAt
            : DateTimeOffset.UtcNow;

        var reasons = new List<string>();
        if (verificationRoot.Value.TryGetProperty("reasons", out var verificationReasons) &&
            verificationReasons.ValueKind == JsonValueKind.Array)
        {
            reasons.AddRange(verificationReasons.EnumerateArray()
                .Select(static item => item.GetString())
                .Where(static item => !string.IsNullOrWhiteSpace(item))!
                .Cast<string>());
        }

        if (trustRoot.Value.TryGetProperty("reasons", out var trustReasons) &&
            trustReasons.ValueKind == JsonValueKind.Array)
        {
            foreach (var reason in trustReasons.EnumerateArray().Select(static item => item.GetString()))
            {
                if (!string.IsNullOrWhiteSpace(reason) && !reasons.Contains(reason, StringComparer.OrdinalIgnoreCase))
                    reasons.Add(reason);
            }
        }

        var resolvedAgentId = verificationRoot.Value.TryGetProperty("agentId", out var verifiedAgentId) &&
                              verifiedAgentId.ValueKind == JsonValueKind.String
            ? verifiedAgentId.GetString() ?? agentId
            : agentId;

        var signingMachineId = verificationRoot.Value.TryGetProperty("signingMachineId", out var signerElement) &&
                               signerElement.ValueKind == JsonValueKind.String
            ? signerElement.GetString() ?? string.Empty
            : string.Empty;

        var trustScore = trustRoot.Value.TryGetProperty("trustScore", out var trustScoreElement) &&
                         trustScoreElement.ValueKind == JsonValueKind.Number
            ? trustScoreElement.GetInt32()
            : 0;

        var trustLevel = trustRoot.Value.TryGetProperty("trustLevel", out var trustLevelElement) &&
                         trustLevelElement.ValueKind == JsonValueKind.String
            ? trustLevelElement.GetString() ?? string.Empty
            : string.Empty;

        return new AgentIdentityVerification(
            resolvedAgentId,
            verificationRoot.Value.TryGetProperty("verified", out var verifiedElement) && verifiedElement.ValueKind == JsonValueKind.True,
            verificationRoot.Value.TryGetProperty("signerKnown", out var signerKnownElement) && signerKnownElement.ValueKind == JsonValueKind.True,
            verificationRoot.Value.TryGetProperty("revoked", out var revokedElement) && revokedElement.ValueKind == JsonValueKind.True,
            verificationRoot.Value.TryGetProperty("expired", out var expiredElement) && expiredElement.ValueKind == JsonValueKind.True,
            trustScore,
            trustLevel,
            verifiedAtUtc,
            $"verification://credentials-agent/{Uri.EscapeDataString(resolvedAgentId)}/{verifiedAtUtc.ToUnixTimeSeconds()}",
            $"provenance://credentials-agent/{Uri.EscapeDataString(resolvedAgentId)}",
            signingMachineId,
            reasons);
    }

    public async Task<WorkerAuthLease> IssueWorkerAuthLeaseAsync(WorkerAuthLeaseIssueRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var envelope = await CallAsync("issue_worker_auth_lease", new Dictionary<string, object?>
        {
            ["agentId"] = request.AgentId,
            ["agentInstanceId"] = request.AgentInstanceId,
            ["agentPoolId"] = request.AgentPoolId,
            ["supervisorActorId"] = request.SupervisorActorId,
            ["laneId"] = request.LaneId,
            ["audiences"] = request.Audiences,
            ["storyId"] = request.StoryId,
            ["workRequestId"] = request.WorkRequestId,
            ["workloadClass"] = request.WorkloadClass,
            ["modelClass"] = request.ModelClass,
            ["runnerClass"] = request.RunnerClass,
            ["approvalScope"] = request.ApprovalScope,
            ["sandboxScope"] = request.SandboxScope,
            ["ttlMinutes"] = request.TtlMinutes
        }, ct).ConfigureAwait(false);

        return ParseWorkerAuthLease(envelope, "issue_worker_auth_lease");
    }

    public async Task<WorkerAuthLease> RenewWorkerAuthLeaseAsync(string authLeaseId, int? ttlMinutes = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authLeaseId);
        var envelope = await CallAsync("renew_worker_auth_lease", new Dictionary<string, object?>
        {
            ["authLeaseId"] = authLeaseId,
            ["ttlMinutes"] = ttlMinutes
        }, ct).ConfigureAwait(false);

        return ParseWorkerAuthLease(envelope, "renew_worker_auth_lease");
    }

    public async Task<WorkerAuthLease> VerifyWorkerAuthLeaseAsync(string authLeaseId, string? audience = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authLeaseId);
        var envelope = await CallAsync("verify_worker_auth_lease", new Dictionary<string, object?>
        {
            ["authLeaseId"] = authLeaseId,
            ["audience"] = audience
        }, ct).ConfigureAwait(false);

        return ParseWorkerAuthLease(envelope, "verify_worker_auth_lease");
    }

    public async Task<WorkerAuthLease> RevokeWorkerAuthLeaseAsync(string authLeaseId, string? reason = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authLeaseId);
        var envelope = await CallAsync("revoke_worker_auth_lease", new Dictionary<string, object?>
        {
            ["authLeaseId"] = authLeaseId,
            ["reason"] = reason
        }, ct).ConfigureAwait(false);

        return ParseWorkerAuthLease(envelope, "revoke_worker_auth_lease");
    }

    public async Task<RevocationEventBatch> SubscribeRevocationEventsAsync(string consumerId, string? scope = null, long afterSequence = 0, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);
        var envelope = await CallAsync("subscribe_revocation_events", new Dictionary<string, object?>
        {
            ["consumerId"] = consumerId,
            ["scope"] = scope,
            ["afterSequence"] = afterSequence
        }, ct).ConfigureAwait(false);

        if (envelope.Result is not JsonElement root)
        {
            throw Fail(envelope, "subscribe_revocation_events result was null");
        }

        return new RevocationEventBatch(
            ReadString(root, "consumerId"),
            ReadLong(root, "afterSequence"),
            ReadLong(root, "lastSequence"),
            ReadRevocationEvents(root));
    }

    internal sealed record McpEnvelope(bool Success, JsonElement? Result, string? Error, string? ErrorCode, string? CorrelationId);

    private async Task<McpEnvelope> CallAsync(string tool, Dictionary<string, object?> arguments, CancellationToken ct)
    {
        var a2aEnvelope = await TryCallA2AAsync(tool, arguments, ct).ConfigureAwait(false);
        if (a2aEnvelope is not null)
        {
            return a2aEnvelope;
        }

        var body = new { tool, arguments };
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("mcp/tools/call", body, JsonOpts, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new CredentialsClientException("TRANSPORT_FAILURE", $"CredentialsAgent unreachable: {ex.Message}", 0, null, ex);
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var parsed = await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOpts, ct).ConfigureAwait(false);

        bool success = parsed.TryGetProperty("success", out var sOk) && sOk.ValueKind == JsonValueKind.True;
        JsonElement? result = parsed.TryGetProperty("result", out var r) && r.ValueKind != JsonValueKind.Null ? r : (JsonElement?)null;
        string? error = parsed.TryGetProperty("error", out var er) && er.ValueKind == JsonValueKind.String ? er.GetString() : null;
        string? errorCode = parsed.TryGetProperty("errorCode", out var ec) && ec.ValueKind == JsonValueKind.String ? ec.GetString() : null;
        string? correlation = null;
        if (parsed.TryGetProperty("metadata", out var md) && md.ValueKind == JsonValueKind.Object &&
            md.TryGetProperty("correlationId", out var cid) && cid.ValueKind == JsonValueKind.String)
            correlation = cid.GetString();

        var envelope = new McpEnvelope(success, result, error, errorCode, correlation);

        if (!response.IsSuccessStatusCode)
        {
            throw new CredentialsClientException(
                errorCode ?? "HTTP_" + (int)response.StatusCode,
                error ?? $"CredentialsAgent HTTP {(int)response.StatusCode}",
                (int)response.StatusCode,
                correlation);
        }
        if (!success)
        {
            throw new CredentialsClientException(
                errorCode ?? "UNKNOWN",
                error ?? "CredentialsAgent returned success=false",
                (int)response.StatusCode,
                correlation);
        }

        _log.LogDebug("CredentialsClient tool={Tool} ok correlation={CorrelationId}", tool, correlation);
        return envelope;
    }

    private async Task<McpEnvelope?> TryCallA2AAsync(string tool, Dictionary<string, object?> arguments, CancellationToken ct)
    {
        if (!_options.PreferA2A || _a2aClient is null)
        {
            return null;
        }

        var baseUrl = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        try
        {
            var card = await _a2aClient.DiscoverAsync(baseUrl, ct).ConfigureAwait(false);
            if (!card.Skills.Any(skill => string.Equals(skill.Name, tool, StringComparison.Ordinal)))
            {
                return null;
            }

            var response = await _a2aClient.SendTaskAsync(
                baseUrl,
                new A2ATask
                {
                    Id = $"credentials-{Guid.NewGuid():N}",
                    Skill = tool,
                    Input = arguments,
                    Metadata = new A2AMetadata
                    {
                        CorrelationId = $"credentials-{Guid.NewGuid():N}",
                        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["x-dna-actor-id"] = _options.AgentId,
                            ["x-dna-actor-type"] = _options.A2AActorType
                        }
                    }
                },
                cancellationToken: ct).ConfigureAwait(false);

            if (!response.Success)
            {
                throw new CredentialsClientException("A2A_FAILURE", response.Error ?? $"A2A task '{tool}' failed.", 0, null);
            }

            if (response.Output is JsonElement output)
            {
                var parsed = ParseA2AEnvelope(output);
                if (parsed is not null)
                {
                    _log.LogDebug("CredentialsClient tool={Tool} ok via a2a", tool);
                    return parsed;
                }
            }

            throw new CredentialsClientException("A2A_SHAPE_MISMATCH", $"A2A output for '{tool}' could not be parsed.", 0, null);
        }
        catch (CredentialsClientException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "CredentialsClient A2A call failed for tool={Tool}; falling back to MCP.", tool);
            return null;
        }
    }

    private static McpEnvelope? ParseA2AEnvelope(JsonElement output)
    {
        if (output.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var success = output.TryGetProperty("success", out var successElement) && successElement.ValueKind == JsonValueKind.True;
        JsonElement? result = output.TryGetProperty("result", out var resultElement) && resultElement.ValueKind != JsonValueKind.Null
            ? resultElement
            : null;
        var error = output.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
            ? errorElement.GetString()
            : null;
        var errorCode = output.TryGetProperty("errorCode", out var errorCodeElement) && errorCodeElement.ValueKind == JsonValueKind.String
            ? errorCodeElement.GetString()
            : null;
        string? correlation = null;
        if (output.TryGetProperty("metadata", out var metadataElement) &&
            metadataElement.ValueKind == JsonValueKind.Object &&
            metadataElement.TryGetProperty("correlationId", out var correlationElement) &&
            correlationElement.ValueKind == JsonValueKind.String)
        {
            correlation = correlationElement.GetString();
        }

        return new McpEnvelope(success, result, error, errorCode, correlation);
    }

    private static CredentialsClientException Fail(McpEnvelope env, string message) =>
        new(env.ErrorCode ?? "SHAPE_MISMATCH", message, 200, env.CorrelationId);

    private static WorkerAuthLease ParseWorkerAuthLease(McpEnvelope env, string tool)
    {
        if (env.Result is not JsonElement root)
        {
            throw Fail(env, $"{tool} result was null");
        }

        return new WorkerAuthLease(
            ReadString(root, "authLeaseId"),
            ReadString(root, "agentId"),
            ReadString(root, "agentInstanceId"),
            ReadString(root, "agentPoolId"),
            ReadString(root, "supervisorActorId"),
            ReadString(root, "laneId"),
            ReadOptionalString(root, "storyId"),
            ReadOptionalString(root, "workRequestId"),
            ReadOptionalString(root, "workloadClass"),
            ReadOptionalString(root, "modelClass"),
            ReadOptionalString(root, "runnerClass"),
            ReadStringArray(root, "audiences"),
            ReadStringArray(root, "approvalScope"),
            ReadStringArray(root, "sandboxScope"),
            ReadDateTimeOffset(root, "issuedAtUtc"),
            ReadDateTimeOffset(root, "expiresAtUtc"),
            ReadOptionalDateTimeOffset(root, "renewedAtUtc"),
            ReadDateTimeOffset(root, "verifiedAtUtc"),
            ReadOptionalDateTimeOffset(root, "revokedAtUtc"),
            ReadOptionalString(root, "revocationReason"),
            ReadBoolean(root, "active"),
            ReadStringArray(root, "reasons"),
            ReadOptionalString(root, "leaseToken"));
    }

    private static string ReadString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string? ReadOptionalString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool ReadBoolean(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

    private static DateTimeOffset ReadDateTimeOffset(JsonElement root, string propertyName)
        => ReadOptionalDateTimeOffset(root, propertyName) ?? DateTimeOffset.UtcNow;

    private static DateTimeOffset? ReadOptionalDateTimeOffset(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var property) &&
           property.ValueKind == JsonValueKind.String &&
           DateTimeOffset.TryParse(property.GetString(), out var value)
            ? value
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property.EnumerateArray()
            .Select(static item => item.GetString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))!
            .Cast<string>()
            .ToArray();
    }

    private static long ReadLong(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt64(out var value) => value,
            JsonValueKind.String when long.TryParse(property.GetString(), out var value) => value,
            _ => 0
        };
    }

    private static IReadOnlyList<RevocationEvent> ReadRevocationEvents(JsonElement root)
    {
        if (!root.TryGetProperty("events", out var eventsElement) || eventsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RevocationEvent>();
        }

        var events = new List<RevocationEvent>(eventsElement.GetArrayLength());
        foreach (var item in eventsElement.EnumerateArray())
        {
            events.Add(new RevocationEvent(
                ReadLong(item, "sequence"),
                ReadString(item, "eventId"),
                ReadString(item, "eventType"),
                ReadString(item, "subjectId"),
                ReadOptionalString(item, "agentId"),
                ReadOptionalString(item, "agentInstanceId"),
                ReadOptionalString(item, "workOrderId"),
                ReadOptionalString(item, "authLeaseId"),
                ReadOptionalString(item, "reason"),
                ReadDateTimeOffset(item, "revokedAtUtc")));
        }

        return events;
    }
}
