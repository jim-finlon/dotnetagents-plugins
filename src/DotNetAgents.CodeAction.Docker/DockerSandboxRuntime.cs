using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotNetAgents.CodeAction.Docker;

/// <summary>
/// Default code-action runtime — shells out to <c>docker run</c> with the hardened argv built
/// by <see cref="DockerCommandBuilder"/>. The actual process invocation is testable via the
/// <see cref="IDockerProcessRunner"/> seam so tests do not require a Docker daemon.
/// </summary>
/// <remarks>
/// Security posture: read-only root, no network by default, drops all caps, enforces
/// <c>no-new-privileges</c>, runs as <c>nobody</c>, hard pids/cpu/memory limits. Code file is
/// bind-mounted read-only; nothing inside the container can persist to the host.
/// </remarks>
public sealed class DockerSandboxRuntime : ICodeActionRuntime, IExecutionSandboxProfileSource, IDisposable
{
    private readonly DockerSandboxOptions _dockerOptions;
    private readonly CodeActionOptions _codeActionOptions;
    private readonly IDockerProcessRunner _runner;
    private readonly ILogger<DockerSandboxRuntime> _logger;
    private readonly string _scratchRoot;

    public DockerSandboxRuntime(
        IOptions<DockerSandboxOptions> dockerOptions,
        IOptions<CodeActionOptions>? codeActionOptions = null,
        IDockerProcessRunner? runner = null,
        ILogger<DockerSandboxRuntime>? logger = null)
    {
        _dockerOptions = dockerOptions?.Value ?? throw new ArgumentNullException(nameof(dockerOptions));
        _codeActionOptions = CodeActionGuards.ResolveOptions(codeActionOptions);
        _runner = runner ?? new DefaultDockerProcessRunner();
        _logger = logger ?? NullLogger<DockerSandboxRuntime>.Instance;
        _scratchRoot = Path.Combine(Path.GetTempPath(), "dotnetagents-codeaction");
        Directory.CreateDirectory(_scratchRoot);
    }

    public string SandboxId => "docker";

    public ExecutionSandboxRuntimeProfile GetRuntimeProfile(CodeActionRequest request)
    {
        var networkExposure = request.AllowNetwork
            ? request.AllowedHosts is { Count: > 0 }
                ? $"bridge egress with operator allow-list ({string.Join(", ", request.AllowedHosts)})"
                : "bridge egress enabled by operator policy"
            : "docker network none";

        var filesystemExposure = request.WorkingDirectoryFiles is { Count: > 0 }
            ? "ephemeral read-only code mount plus caller-supplied working files"
            : "ephemeral read-only code mount only";

        return new ExecutionSandboxRuntimeProfile(
            SandboxId: SandboxId,
            Substrate: "container",
            FilesystemExposure: filesystemExposure,
            NetworkExposure: networkExposure,
            SecretExposure: request.Environment is { Count: > 0 }
                ? "caller-supplied environment variables inside an unprivileged container"
                : "no declared secret material",
            EscalationGuidance: "Escalate to an operator before enabling networked or long-running container execution.",
            Cleanup: new ExecutionSandboxCleanupPolicy(
                Strategy: "docker --rm plus per-run scratch file deletion",
                CleanupGuaranteed: true,
                CleanupNotRequired: false,
                CleanupAttempted: false,
                CleanupVerified: false,
                Notes: "Per-run code files are deleted in finally blocks and containers run with --rm."),
            RetentionPolicy: "receipt-only");
    }

    public async Task<CodeActionResult> ExecuteAsync(CodeActionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rejection = CodeActionGuards.RejectIfInvalid(request, _codeActionOptions, SandboxId);
        if (rejection is not null) return rejection;

        var timeout = CodeActionGuards.ResolveTimeout(request, _codeActionOptions);

        var executionId = Guid.NewGuid().ToString("N");
        var codePath = Path.Combine(_scratchRoot, $"{executionId}.py");
        var containerName = $"dna-codeaction-{executionId}";

        await File.WriteAllTextAsync(codePath, request.Code, cancellationToken).ConfigureAwait(false);

        try
        {
            var argv = DockerCommandBuilder.Build(request, _dockerOptions, codePath, containerName);

            if (_dockerOptions.DryRun)
            {
                var dryRunOutput = $"{_dockerOptions.DockerExecutable} {string.Join(' ', argv)}";
                return new CodeActionResult(0, dryRunOutput, string.Empty, TimeSpan.Zero, false, SandboxId);
            }

            var sw = Stopwatch.StartNew();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            DockerProcessResult procResult;
            try
            {
                procResult = await _runner.RunAsync(_dockerOptions.DockerExecutable, argv, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Code-action execution timed out after {Timeout}; killing container {Container}.", timeout, containerName);
                await _runner.KillContainerAsync(_dockerOptions.DockerExecutable, containerName, CancellationToken.None).ConfigureAwait(false);
                return new CodeActionResult(-1, string.Empty, string.Empty, sw.Elapsed, true, SandboxId, $"Timed out after {timeout}.");
            }

            sw.Stop();
            var stdOut = TruncateTo(procResult.StdOut, _codeActionOptions.MaxOutputBytes);
            var stdErr = TruncateTo(procResult.StdErr, _codeActionOptions.MaxOutputBytes);
            return new CodeActionResult(procResult.ExitCode, stdOut, stdErr, sw.Elapsed, false, SandboxId);
        }
        finally
        {
            try { File.Delete(codePath); }
            catch (IOException ex) { _logger.LogDebug(ex, "Code-action scratch file delete failed (non-fatal): {Path}", codePath); }
        }
    }

    private static string TruncateTo(string text, int maxBytes)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var bytes = Encoding.UTF8.GetByteCount(text);
        if (bytes <= maxBytes) return text;
        var truncated = Encoding.UTF8.GetBytes(text);
        Array.Resize(ref truncated, maxBytes);
        return Encoding.UTF8.GetString(truncated) + "\n[output truncated]";
    }

    public void Dispose()
    {
        // Scratch root is shared across executions; we leave it for the OS to GC.
    }
}
