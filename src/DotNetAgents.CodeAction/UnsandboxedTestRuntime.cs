using Microsoft.Extensions.Options;

namespace DotNetAgents.CodeAction;

/// <summary>
/// A test-only runtime that returns canned <see cref="CodeActionResult"/>s based on a script
/// the test author registers. NEVER executes any code on the host. NEVER use in production —
/// it is intentionally not registered by the default DI helpers.
/// </summary>
/// <remarks>
/// Useful for orchestrator unit tests that want to verify the LLM → code-extract → execute
/// loop without standing up Docker. Tests register canned outputs by code body or by predicate
/// and the runtime returns them in order.
/// </remarks>
public sealed class UnsandboxedTestRuntime : ICodeActionRuntime, IExecutionSandboxProfileSource
{
    private readonly CodeActionOptions _options;
    private readonly Queue<Func<CodeActionRequest, CodeActionResult>> _scripted = new();

    public UnsandboxedTestRuntime(IOptions<CodeActionOptions>? options = null)
    {
        _options = CodeActionGuards.ResolveOptions(options);
    }

    public string SandboxId => "unsandboxed-test";

    public ExecutionSandboxRuntimeProfile GetRuntimeProfile(CodeActionRequest request)
    {
        return new ExecutionSandboxRuntimeProfile(
            SandboxId: SandboxId,
            Substrate: "test-double",
            FilesystemExposure: request.WorkingDirectoryFiles is { Count: > 0 }
                ? "test-only caller-supplied working files"
                : "no filesystem exposure",
            NetworkExposure: request.AllowNetwork ? "test-only simulated network request" : "network denied",
            SecretExposure: request.Environment is { Count: > 0 }
                ? "test-only simulated environment variables"
                : "no secret material exposed",
            EscalationGuidance: "Never use this runtime outside unit tests.",
            Cleanup: new ExecutionSandboxCleanupPolicy(
                Strategy: "no-op test cleanup",
                CleanupGuaranteed: true,
                CleanupNotRequired: true,
                CleanupAttempted: false,
                CleanupVerified: true,
                Notes: "UnsandboxedTestRuntime never creates external resources."),
            RetentionPolicy: "none");
    }

    /// <summary>Enqueue a canned response. Subsequent <c>ExecuteAsync</c> calls dequeue in FIFO order.</summary>
    public UnsandboxedTestRuntime EnqueueResponse(Func<CodeActionRequest, CodeActionResult> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        _scripted.Enqueue(respond);
        return this;
    }

    /// <summary>Convenience helper for tests that want a stdout-only success response.</summary>
    public UnsandboxedTestRuntime EnqueueStdOut(string stdout, int exitCode = 0)
    {
        return EnqueueResponse(_ => new CodeActionResult(exitCode, stdout, string.Empty, TimeSpan.FromMilliseconds(1), false, SandboxId));
    }

    public Task<CodeActionResult> ExecuteAsync(CodeActionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rejection = CodeActionGuards.RejectIfInvalid(request, _options, SandboxId);
        if (rejection is not null)
        {
            return Task.FromResult(rejection);
        }

        if (_scripted.Count == 0)
        {
            return Task.FromResult(new CodeActionResult(
                ExitCode: 0,
                StdOut: string.Empty,
                StdErr: string.Empty,
                Duration: TimeSpan.Zero,
                TimedOut: false,
                SandboxId: SandboxId,
                Error: "UnsandboxedTestRuntime: no canned response enqueued."));
        }

        var responder = _scripted.Dequeue();
        return Task.FromResult(responder(request));
    }
}
