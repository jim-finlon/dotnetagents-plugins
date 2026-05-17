namespace DotNetAgents.CodeAction.Pyodide;

/// <summary>
/// Indirection over the Deno-hosted Pyodide sidecar that actually runs the Python code.
/// Default implementation shells out to <c>deno run --allow-read=...</c> with the bundled
/// sidecar script; tests inject a <see cref="StubPyodideHost"/> so unit tests don't need
/// Deno installed.
/// </summary>
public interface IPyodideHost
{
    /// <summary>
    /// Execute Python code in the Pyodide sidecar and return the structured result.
    /// </summary>
    Task<PyodideExecutionResult> ExecuteAsync(
        string pythonCode,
        TimeSpan timeout,
        bool allowNetwork,
        CancellationToken cancellationToken);
}

/// <param name="ExitCode">0 on success.</param>
/// <param name="StdOut">Captured stdout.</param>
/// <param name="StdErr">Captured stderr (Pyodide tracebacks land here).</param>
/// <param name="TimedOut">True when the host killed the sidecar for exceeding timeout.</param>
public sealed record PyodideExecutionResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);
