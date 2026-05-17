using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotNetAgents.CodeAction.Pyodide;

/// <summary>
/// <see cref="ICodeActionRuntime"/> implementation backed by Pyodide running inside a Deno
/// sidecar. Honors the same security contract as the Docker runtime: empty-code rejected,
/// oversize-code rejected, network-when-policy-denies rejected, hard timeout, output truncation.
/// </summary>
/// <remarks>
/// Pyodide is a WebAssembly Python build, so the runtime cannot escape the WASM sandbox into
/// the host filesystem. Network access is gated by both DNA policy and Deno's permission model
/// (<c>--allow-net</c>). Memory + CPU bounds are inherited from Deno's V8 limits.
/// </remarks>
public sealed class PyodideSandboxRuntime : ICodeActionRuntime, IExecutionSandboxProfileSource
{
    private readonly PyodideSandboxOptions _pyodideOptions;
    private readonly CodeActionOptions _codeActionOptions;
    private readonly IPyodideHost _host;
    private readonly ILogger<PyodideSandboxRuntime> _logger;

    public PyodideSandboxRuntime(
        IOptions<PyodideSandboxOptions> pyodideOptions,
        IOptions<CodeActionOptions>? codeActionOptions,
        IPyodideHost host,
        ILogger<PyodideSandboxRuntime>? logger = null)
    {
        _pyodideOptions = pyodideOptions?.Value ?? throw new ArgumentNullException(nameof(pyodideOptions));
        _codeActionOptions = CodeActionGuards.ResolveOptions(codeActionOptions);
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _logger = logger ?? NullLogger<PyodideSandboxRuntime>.Instance;
    }

    public string SandboxId => "pyodide";

    public ExecutionSandboxRuntimeProfile GetRuntimeProfile(CodeActionRequest request)
    {
        return new ExecutionSandboxRuntimeProfile(
            SandboxId: SandboxId,
            Substrate: "wasm",
            FilesystemExposure: request.WorkingDirectoryFiles is { Count: > 0 }
                ? "ephemeral in-memory working files inside the Pyodide host"
                : "no persisted filesystem exposure",
            NetworkExposure: request.AllowNetwork && _pyodideOptions.AllowNetwork
                ? "deno-scoped allow-net egress when operator policy permits"
                : "network denied",
            SecretExposure: request.Environment is { Count: > 0 }
                ? "caller-supplied environment values passed through the host boundary"
                : "no declared secret material",
            EscalationGuidance: "Escalate before using Pyodide for workloads that need privileged host access or long-lived artifacts.",
            Cleanup: new ExecutionSandboxCleanupPolicy(
                Strategy: "ephemeral wasm host teardown",
                CleanupGuaranteed: true,
                CleanupNotRequired: true,
                CleanupAttempted: false,
                CleanupVerified: true,
                Notes: "Pyodide runs in an isolated WebAssembly host with no persistent per-run filesystem state."),
            RetentionPolicy: "receipt-only");
    }

    public async Task<CodeActionResult> ExecuteAsync(CodeActionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var rejection = CodeActionGuards.RejectIfInvalid(request, _codeActionOptions, SandboxId);
        if (rejection is not null) return rejection;

        if (!string.Equals(request.Language, "python", StringComparison.OrdinalIgnoreCase))
        {
            return new CodeActionResult(-1, string.Empty, string.Empty, TimeSpan.Zero, false, SandboxId,
                $"Pyodide runtime only supports Python; got '{request.Language}'.");
        }

        var timeout = CodeActionGuards.ResolveTimeout(request, _codeActionOptions);
        var allowNetwork = request.AllowNetwork && _pyodideOptions.AllowNetwork;

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _host.ExecuteAsync(request.Code, timeout, allowNetwork, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            return new CodeActionResult(
                ExitCode: result.ExitCode,
                StdOut: TruncateTo(result.StdOut, _codeActionOptions.MaxOutputBytes),
                StdErr: TruncateTo(result.StdErr, _codeActionOptions.MaxOutputBytes),
                Duration: sw.Elapsed,
                TimedOut: result.TimedOut,
                SandboxId: SandboxId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return new CodeActionResult(-1, string.Empty, string.Empty, sw.Elapsed, true, SandboxId,
                $"Pyodide execution timed out after {timeout}.");
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
}
