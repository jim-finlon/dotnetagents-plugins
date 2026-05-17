namespace DotNetAgents.CodeAction;

/// <summary>
/// Shared execution sandbox manager used by higher-level runtimes such as JARVIS to run
/// sandboxed code or high-impact tool actions through one auditable path.
/// </summary>
public interface IExecutionSandboxManager
{
    Task<ExecutionSandboxRun> ExecuteAsync(
        ICodeActionRuntime runtime,
        CodeActionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Stable execution contract that wraps sandbox execution with lifecycle receipts, quota snapshots,
/// cleanup notes, and operator-facing runtime metadata.
/// </summary>
public sealed class DefaultExecutionSandboxManager : IExecutionSandboxManager
{
    private readonly CodeActionOptions _options;

    public DefaultExecutionSandboxManager(Microsoft.Extensions.Options.IOptions<CodeActionOptions>? options = null)
    {
        _options = CodeActionGuards.ResolveOptions(options);
    }

    public async Task<ExecutionSandboxRun> ExecuteAsync(
        ICodeActionRuntime runtime,
        CodeActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        var runId = Guid.NewGuid().ToString("N");
        var startedAtUtc = DateTimeOffset.UtcNow;
        var runtimeProfile = ResolveRuntimeProfile(runtime, request);
        var quotaSnapshot = BuildQuotaSnapshot(request);
        var lifecycle = new List<ExecutionSandboxLifecycleEvent>
        {
            new("requested", startedAtUtc, "Execution requested by orchestrator."),
            new("running", DateTimeOffset.UtcNow, $"Sandbox runtime '{runtime.SandboxId}' started execution."),
        };

        CodeActionResult result;
        try
        {
            result = await runtime.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var cancelledAtUtc = DateTimeOffset.UtcNow;
            lifecycle.Add(new("cancelled", cancelledAtUtc, "Execution cancelled before the sandbox completed."));
            throw;
        }

        var completedAtUtc = DateTimeOffset.UtcNow;
        var finalState = ResolveFinalState(result);
        lifecycle.Add(new(finalState, completedAtUtc, BuildFinalStateMessage(result)));

        var cleanup = runtimeProfile.Cleanup switch
        {
            { CleanupNotRequired: true } noCleanupRequired => noCleanupRequired with
            {
                CleanupAttempted = false,
                CleanupVerified = true,
            },
            _ => runtimeProfile.Cleanup with
            {
                CleanupAttempted = true,
                CleanupVerified = runtimeProfile.Cleanup.CleanupGuaranteed,
            }
        };

        var receipt = new ExecutionSandboxReceipt(
            RunId: runId,
            SandboxId: runtime.SandboxId,
            RuntimeProfile: runtimeProfile with { Cleanup = cleanup },
            QuotaSnapshot: quotaSnapshot,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            FinalState: finalState,
            Lifecycle: lifecycle,
            RetainedArtifacts: BuildRetainedArtifacts(runtimeProfile, request),
            AuditSummary: BuildAuditSummary(runtime, request, result));

        return new ExecutionSandboxRun(result, receipt);
    }

    private ExecutionSandboxQuotaSnapshot BuildQuotaSnapshot(CodeActionRequest request)
    {
        var timeout = CodeActionGuards.ResolveTimeout(request, _options);
        var codeBytes = System.Text.Encoding.UTF8.GetByteCount(request.Code);
        var workingDirectoryFileCount = request.WorkingDirectoryFiles?.Count ?? 0;
        var workingDirectoryBytes = request.WorkingDirectoryFiles?.Values.Sum(static bytes => (long)bytes.Length) ?? 0L;
        var environmentVariableCount = request.Environment?.Count ?? 0;

        return new ExecutionSandboxQuotaSnapshot(
            Timeout: timeout,
            MaxOutputBytes: _options.MaxOutputBytes,
            MaxCodeBytes: _options.MaxCodeBytes,
            CodeBytes: codeBytes,
            NetworkRequested: request.AllowNetwork,
            AllowedHosts: request.AllowedHosts?.ToArray() ?? Array.Empty<string>(),
            WorkingDirectoryFileCount: workingDirectoryFileCount,
            WorkingDirectoryBytes: workingDirectoryBytes,
            EnvironmentVariableCount: environmentVariableCount);
    }

    private static ExecutionSandboxRuntimeProfile ResolveRuntimeProfile(ICodeActionRuntime runtime, CodeActionRequest request)
    {
        if (runtime is IExecutionSandboxProfileSource profiledRuntime)
        {
            return profiledRuntime.GetRuntimeProfile(request);
        }

        return new ExecutionSandboxRuntimeProfile(
            SandboxId: runtime.SandboxId,
            Substrate: "custom",
            FilesystemExposure: request.WorkingDirectoryFiles is { Count: > 0 }
                ? "caller-supplied read-only working files"
                : "no declared writable filesystem exposure",
            NetworkExposure: request.AllowNetwork ? "request-scoped operator-approved egress" : "network denied",
            SecretExposure: request.Environment is { Count: > 0 }
                ? "caller-supplied environment variables"
                : "no declared secret material",
            EscalationGuidance: "Review the runtime implementation and operator policy before enabling high-impact execution.",
            Cleanup: new ExecutionSandboxCleanupPolicy(
                Strategy: "runtime-managed cleanup",
                CleanupGuaranteed: false,
                CleanupNotRequired: false,
                CleanupAttempted: false,
                CleanupVerified: false,
                Notes: "Custom runtime did not publish a stronger cleanup contract."),
            RetentionPolicy: "operator-defined");
    }

    private static string ResolveFinalState(CodeActionResult result)
    {
        if (result.TimedOut)
        {
            return "timed_out";
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return "runtime_error";
        }

        return result.ExitCode == 0 ? "completed" : "failed";
    }

    private static string BuildFinalStateMessage(CodeActionResult result)
    {
        if (result.TimedOut)
        {
            return "Sandbox execution hit the enforced timeout and was terminated.";
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            return $"Sandbox runtime reported an error: {result.Error}";
        }

        return result.ExitCode == 0
            ? "Sandbox execution completed successfully."
            : $"Sandbox execution exited with code {result.ExitCode}.";
    }

    private static IReadOnlyList<string> BuildRetainedArtifacts(ExecutionSandboxRuntimeProfile runtimeProfile, CodeActionRequest request)
    {
        var artifacts = new List<string>
        {
            $"retention:{runtimeProfile.RetentionPolicy}",
        };

        if (request.WorkingDirectoryFiles is { Count: > 0 })
        {
            artifacts.Add($"working-files:{request.WorkingDirectoryFiles.Count}");
        }

        if (request.Environment is { Count: > 0 })
        {
            artifacts.Add($"environment-variables:{request.Environment.Count}");
        }

        return artifacts;
    }

    private static string BuildAuditSummary(ICodeActionRuntime runtime, CodeActionRequest request, CodeActionResult result)
    {
        var networkText = request.AllowNetwork ? "network-enabled" : "network-denied";
        var hostText = request.AllowedHosts is { Count: > 0 }
            ? $" allow-list=[{string.Join(", ", request.AllowedHosts)}]"
            : string.Empty;
        var errorText = string.IsNullOrWhiteSpace(result.Error) ? string.Empty : $" error='{result.Error}'";
        return $"sandbox={runtime.SandboxId}; language={request.Language}; {networkText}{hostText}; exitCode={result.ExitCode}; timedOut={result.TimedOut}; durationMs={Math.Round(result.Duration.TotalMilliseconds)}{errorText}";
    }
}

public interface IExecutionSandboxProfileSource
{
    ExecutionSandboxRuntimeProfile GetRuntimeProfile(CodeActionRequest request);
}

public sealed record ExecutionSandboxRun(CodeActionResult Result, ExecutionSandboxReceipt Receipt);

public sealed record ExecutionSandboxReceipt(
    string RunId,
    string SandboxId,
    ExecutionSandboxRuntimeProfile RuntimeProfile,
    ExecutionSandboxQuotaSnapshot QuotaSnapshot,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string FinalState,
    IReadOnlyList<ExecutionSandboxLifecycleEvent> Lifecycle,
    IReadOnlyList<string> RetainedArtifacts,
    string AuditSummary);

public sealed record ExecutionSandboxRuntimeProfile(
    string SandboxId,
    string Substrate,
    string FilesystemExposure,
    string NetworkExposure,
    string SecretExposure,
    string EscalationGuidance,
    ExecutionSandboxCleanupPolicy Cleanup,
    string RetentionPolicy);

public sealed record ExecutionSandboxCleanupPolicy(
    string Strategy,
    bool CleanupGuaranteed,
    bool CleanupNotRequired,
    bool CleanupAttempted,
    bool CleanupVerified,
    string Notes);

public sealed record ExecutionSandboxQuotaSnapshot(
    TimeSpan Timeout,
    int MaxOutputBytes,
    int MaxCodeBytes,
    int CodeBytes,
    bool NetworkRequested,
    IReadOnlyList<string> AllowedHosts,
    int WorkingDirectoryFileCount,
    long WorkingDirectoryBytes,
    int EnvironmentVariableCount);

public sealed record ExecutionSandboxLifecycleEvent(
    string State,
    DateTimeOffset TimestampUtc,
    string Message);
