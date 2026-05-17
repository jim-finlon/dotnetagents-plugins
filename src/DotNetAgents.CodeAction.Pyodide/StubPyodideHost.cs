namespace DotNetAgents.CodeAction.Pyodide;

/// <summary>
/// Test-only <see cref="IPyodideHost"/>. Returns scripted responses without invoking Deno.
/// Production hosts wire <see cref="ProcessPyodideHost"/> (sketched in
/// <c>docs/runbooks/code-action-pyodide-install.md</c>) which shells out to Deno + the
/// bundled sidecar script.
/// </summary>
public sealed class StubPyodideHost : IPyodideHost
{
    private readonly Queue<Func<string, PyodideExecutionResult>> _scripted = new();

    public StubPyodideHost EnqueueResponse(Func<string, PyodideExecutionResult> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        _scripted.Enqueue(respond);
        return this;
    }

    public StubPyodideHost EnqueueStdOut(string stdout, int exitCode = 0)
    {
        return EnqueueResponse(_ => new PyodideExecutionResult(exitCode, stdout, string.Empty, false));
    }

    public StubPyodideHost EnqueueTimeout()
    {
        return EnqueueResponse(_ => new PyodideExecutionResult(-1, string.Empty, "[timed out]", true));
    }

    public Task<PyodideExecutionResult> ExecuteAsync(string pythonCode, TimeSpan timeout, bool allowNetwork, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_scripted.Count == 0)
        {
            return Task.FromResult(new PyodideExecutionResult(-1, string.Empty, "[no canned response]", false));
        }
        return Task.FromResult(_scripted.Dequeue()(pythonCode));
    }
}
