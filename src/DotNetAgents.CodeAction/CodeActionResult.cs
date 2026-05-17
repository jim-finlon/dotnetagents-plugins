namespace DotNetAgents.CodeAction;

/// <summary>
/// Outcome of a sandboxed code-action execution.
/// </summary>
/// <param name="ExitCode">Process exit code as reported by the sandbox. <c>0</c> = success.</param>
/// <param name="StdOut">Captured stdout, truncated to <see cref="CodeActionOptions.MaxOutputBytes"/> bytes.</param>
/// <param name="StdErr">Captured stderr, truncated.</param>
/// <param name="Duration">Wall-clock duration of the execution including container start/stop.</param>
/// <param name="TimedOut">True when the execution was killed by the sandbox or orchestrator timeout.</param>
/// <param name="SandboxId">Stable id of the sandbox implementation that ran the request (e.g. <c>docker</c>, <c>pyodide</c>).</param>
/// <param name="Error">Operator-readable error message when the runtime failed to even start the sandbox.</param>
public sealed record CodeActionResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    TimeSpan Duration,
    bool TimedOut,
    string SandboxId,
    string? Error = null)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut && Error is null;
}
