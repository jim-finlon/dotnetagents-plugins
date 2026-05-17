using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using DotNetAgents.Skills;

namespace DotNetAgents.Credentials.Client;

/// <summary>
/// Default <see cref="IRemoteAgentCardSigner"/> implementation that posts to a CredentialsAgent
/// MCP surface (<c>POST /mcp/tools/call</c>) using a pre-configured <see cref="HttpClient"/>.
/// The caller is expected to set <c>HttpClient.BaseAddress</c> and any required
/// <c>X-API-Key</c> header on the supplied HttpClient — typically via IHttpClientFactory.
/// </summary>
/// <remarks>
/// <para>This wrapper does not contain any auth wiring of its own; it lives in
/// <c>DotNetAgents.Skills</c> to avoid pulling <c>DotNetAgents.Credentials.Client</c> into the
/// Skills dependency graph. A richer implementation that reuses <c>CredentialsClient</c> directly
/// can land alongside the Ed25519 follow-up.</para>
/// </remarks>
public sealed class HttpRemoteAgentCardSigner : IRemoteAgentCardSigner
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;

    public HttpRemoteAgentCardSigner(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <inheritdoc />
    public async Task<RemoteSignature> SignAsync(string keyRef, ReadOnlyMemory<byte> canonicalPayload, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyRef);
        var payloadBase64 = Convert.ToBase64String(canonicalPayload.Span);
        var envelope = await PostToolCallAsync("sign_agent_card", new Dictionary<string, object?>
        {
            ["agentId"] = keyRef,
            ["payloadBase64"] = payloadBase64,
        }, ct).ConfigureAwait(false);
        if (envelope is null) throw new InvalidOperationException("sign_agent_card returned no envelope");
        var alg = ReadString(envelope.Value, "alg") ?? throw new InvalidOperationException("sign_agent_card response missing 'alg'");
        var signature = ReadString(envelope.Value, "signature")
            ?? ReadString(envelope.Value, "signatureBase64")
            ?? throw new InvalidOperationException("sign_agent_card response missing 'signature'");
        return new RemoteSignature(alg, signature);
    }

    /// <inheritdoc />
    public async Task<RemotePublicKey?> ExportPublicKeyAsync(string keyRef, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyRef);
        var envelope = await PostToolCallAsync("export_provenance_bundle", new Dictionary<string, object?>
        {
            ["agentId"] = keyRef,
        }, ct).ConfigureAwait(false);
        if (envelope is null) return null;
        var card = envelope.Value.TryGetProperty("agentCard", out var cardEl) ? (JsonElement?)cardEl : envelope;
        if (card is null) return null;
        var alg = ReadString(card.Value, "alg") ?? ReadString(card.Value, "signatureAlg");
        var publicKey = ReadString(card.Value, "publicKeyBase64")
            ?? ReadString(card.Value, "publicKey")
            ?? ReadString(card.Value, "spkiBase64");
        if (alg is null || publicKey is null) return null;
        return new RemotePublicKey(alg, publicKey);
    }

    private async Task<JsonElement?> PostToolCallAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = tool,
            ["arguments"] = arguments,
        };
        using var response = await _http.PostAsJsonAsync("mcp/tools/call", body, JsonOpts, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;
        // Standard MCP envelope: { success, result, error?, correlationId? }
        if (root.TryGetProperty("result", out var resultEl) && resultEl.ValueKind != JsonValueKind.Null)
        {
            return resultEl.Clone();
        }
        // Some legacy endpoints return the result fields at the root.
        return root.ValueKind == JsonValueKind.Object ? root.Clone() : null;
    }

    private static string? ReadString(JsonElement el, string name)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}
