using System.Text.Json;

namespace DotNetAgents.CodeAction;

/// <summary>
/// Transport helpers for publishing sandbox receipts across process and service boundaries.
/// </summary>
public static class ExecutionSandboxReceiptTransport
{
    public const string EnvelopeKey = "executionSandbox";
    private static readonly ExecutionSandboxReceiptSummary EmptySummary = new(string.Empty, string.Empty, string.Empty, null);

    public static IReadOnlyDictionary<string, object?> ToEnvelope(ExecutionSandboxReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["runId"] = receipt.RunId,
            ["sandboxId"] = receipt.SandboxId,
            ["startedAtUtc"] = receipt.StartedAtUtc,
            ["completedAtUtc"] = receipt.CompletedAtUtc,
            ["finalState"] = receipt.FinalState,
            ["auditSummary"] = receipt.AuditSummary,
            ["runtimeProfile"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sandboxId"] = receipt.RuntimeProfile.SandboxId,
                ["substrate"] = receipt.RuntimeProfile.Substrate,
                ["filesystemExposure"] = receipt.RuntimeProfile.FilesystemExposure,
                ["networkExposure"] = receipt.RuntimeProfile.NetworkExposure,
                ["secretExposure"] = receipt.RuntimeProfile.SecretExposure,
                ["escalationGuidance"] = receipt.RuntimeProfile.EscalationGuidance,
                ["retentionPolicy"] = receipt.RuntimeProfile.RetentionPolicy,
                ["cleanup"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["strategy"] = receipt.RuntimeProfile.Cleanup.Strategy,
                    ["cleanupGuaranteed"] = receipt.RuntimeProfile.Cleanup.CleanupGuaranteed,
                    ["cleanupNotRequired"] = receipt.RuntimeProfile.Cleanup.CleanupNotRequired,
                    ["cleanupAttempted"] = receipt.RuntimeProfile.Cleanup.CleanupAttempted,
                    ["cleanupVerified"] = receipt.RuntimeProfile.Cleanup.CleanupVerified,
                    ["notes"] = receipt.RuntimeProfile.Cleanup.Notes
                }
            },
            ["quotaSnapshot"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["timeoutSeconds"] = receipt.QuotaSnapshot.Timeout.TotalSeconds,
                ["maxOutputBytes"] = receipt.QuotaSnapshot.MaxOutputBytes,
                ["maxCodeBytes"] = receipt.QuotaSnapshot.MaxCodeBytes,
                ["codeBytes"] = receipt.QuotaSnapshot.CodeBytes,
                ["networkRequested"] = receipt.QuotaSnapshot.NetworkRequested,
                ["allowedHosts"] = receipt.QuotaSnapshot.AllowedHosts.ToArray(),
                ["workingDirectoryFileCount"] = receipt.QuotaSnapshot.WorkingDirectoryFileCount,
                ["workingDirectoryBytes"] = receipt.QuotaSnapshot.WorkingDirectoryBytes,
                ["environmentVariableCount"] = receipt.QuotaSnapshot.EnvironmentVariableCount
            },
            ["retainedArtifacts"] = receipt.RetainedArtifacts.ToArray(),
            ["lifecycle"] = receipt.Lifecycle
                .Select(evt => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["state"] = evt.State,
                    ["timestampUtc"] = evt.TimestampUtc,
                    ["message"] = evt.Message
                })
                .ToArray()
        };
    }

    public static bool TryExtractSummary(object? payload, out ExecutionSandboxReceiptSummary summary)
    {
        if (TryReadEnvelope(payload, out var envelope))
        {
            summary = envelope;
            return true;
        }

        summary = EmptySummary;
        return false;
    }

    private static bool TryReadEnvelope(object? payload, out ExecutionSandboxReceiptSummary summary)
    {
        if (payload is null)
        {
            summary = EmptySummary;
            return false;
        }

        if (TryReadEnvelopeDictionary(AsDictionary(payload), out summary))
        {
            return true;
        }

        if (payload is JsonElement jsonElement)
        {
            return TryReadEnvelopeJson(jsonElement, out summary);
        }

        summary = EmptySummary;
        return false;
    }

    private static bool TryReadEnvelopeDictionary(
        IReadOnlyDictionary<string, object?>? payload,
        out ExecutionSandboxReceiptSummary summary)
    {
        if (payload is null)
        {
            summary = EmptySummary;
            return false;
        }

        if (payload.TryGetValue(EnvelopeKey, out var nested) && TryReadEnvelope(nested, out summary))
        {
            return true;
        }

        if (!TryReadString(payload, "runId", out var runId) ||
            !TryReadString(payload, "sandboxId", out var sandboxId) ||
            !TryReadString(payload, "finalState", out var finalState))
        {
            summary = EmptySummary;
            return false;
        }

        var substrate = TryReadNestedString(payload, "runtimeProfile", "substrate");
        summary = new ExecutionSandboxReceiptSummary(runId, sandboxId, finalState, substrate);
        return true;
    }

    private static bool TryReadEnvelopeJson(JsonElement payload, out ExecutionSandboxReceiptSummary summary)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty(EnvelopeKey, out var nested) &&
            TryReadEnvelopeJson(nested, out summary))
        {
            return true;
        }

        if (payload.ValueKind != JsonValueKind.Object ||
            !TryReadString(payload, "runId", out var runId) ||
            !TryReadString(payload, "sandboxId", out var sandboxId) ||
            !TryReadString(payload, "finalState", out var finalState))
        {
            summary = EmptySummary;
            return false;
        }

        string? substrate = null;
        if (payload.TryGetProperty("runtimeProfile", out var runtimeProfile) &&
            runtimeProfile.ValueKind == JsonValueKind.Object &&
            runtimeProfile.TryGetProperty("substrate", out var substrateElement) &&
            substrateElement.ValueKind == JsonValueKind.String)
        {
            substrate = substrateElement.GetString();
        }

        summary = new ExecutionSandboxReceiptSummary(runId, sandboxId, finalState, substrate);
        return true;
    }

    private static IReadOnlyDictionary<string, object?>? AsDictionary(object payload)
    {
        if (payload is IReadOnlyDictionary<string, object?> readOnly)
        {
            return readOnly;
        }

        if (payload is IDictionary<string, object?> mutable)
        {
            return new Dictionary<string, object?>(mutable, StringComparer.OrdinalIgnoreCase);
        }

        return null;
    }

    private static bool TryReadString(IReadOnlyDictionary<string, object?> payload, string key, out string value)
    {
        if (!payload.TryGetValue(key, out var raw) || raw is null)
        {
            value = string.Empty;
            return false;
        }

        switch (raw)
        {
            case string text when !string.IsNullOrWhiteSpace(text):
                value = text;
                return true;
            case JsonElement json when json.ValueKind == JsonValueKind.String:
                value = json.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            default:
                value = raw.ToString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
        }
    }

    private static bool TryReadString(JsonElement payload, string key, out string value)
    {
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty(key, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }

    private static string? TryReadNestedString(IReadOnlyDictionary<string, object?> payload, string parentKey, string childKey)
    {
        if (!payload.TryGetValue(parentKey, out var nested) || nested is null)
        {
            return null;
        }

        var dict = AsDictionary(nested);
        if (dict is not null && TryReadString(dict, childKey, out var value))
        {
            return value;
        }

        if (nested is JsonElement json &&
            json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty(childKey, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }
}

public sealed record ExecutionSandboxReceiptSummary(
    string RunId,
    string SandboxId,
    string FinalState,
    string? Substrate);
