namespace DotNetAgents.Credentials.Client;

/// <summary>
/// Configuration bound from the host app's IConfiguration (typically section <c>CredentialsClient</c>).
/// </summary>
public sealed class CredentialsClientOptions
{
    /// <summary>Configuration section name used by <c>AddCredentialsClient(IConfiguration)</c>.</summary>
    public const string SectionName = "CredentialsClient";

    /// <summary>Base URL of the CredentialsAgent HTTP MCP surface. Required.</summary>
    /// <example>http://credential-service:8080</example>
    public string BaseUrl { get; set; } = "http://credential-service:8080";

    /// <summary>
    /// Stable actor id this consumer presents to CredentialsAgent. Must match an authorized
    /// <c>AgentAuthorization</c> row (see CredentialsAgent.Infrastructure.Seed.WorkstationActorSeeder
    /// for workstation actors; identity cards for those actors are bootstrapped by
    /// CredentialsAgent.Api.Seed.WorkstationAgentIdentitySeeder when signing keys are configured).
    /// Per-service grants also use CredentialsAdminMiddleware-gated /api/agent-authorizations.
    /// Required.
    /// </summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>HTTP request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Prefer the governed A2A surface when the target advertises the required skill, falling back
    /// to the MCP tool surface during rollout or when A2A is unavailable.
    /// </summary>
    public bool PreferA2A { get; set; } = true;

    /// <summary>
    /// Actor type presented on A2A calls. This becomes <c>x-dna-actor-type</c> on outbound tasks.
    /// </summary>
    public string A2AActorType { get; set; } = "AgentInstance";

    /// <summary>
    /// Optional API key sent as <c>X-Api-Key</c> on every <c>POST …/mcp/tools/call</c> request.
    /// Tyr / edge gateways often require this for server-to-server calls while CredentialsAgent itself
    /// does not validate the header on the legacy REST MCP surface.
    /// </summary>
    public string? McpIngressApiKey { get; set; }
}
