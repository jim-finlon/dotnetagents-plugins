using System.Diagnostics;

namespace DotNetAgents.CodeAction.Docker;

/// <summary>
/// Indirection over <see cref="System.Diagnostics.Process"/> so the Docker runtime can be unit-tested
/// without a real Docker daemon. Default implementation shells out; tests use a fake.
/// </summary>
public interface IDockerProcessRunner
{
    Task<DockerProcessResult> RunAsync(string executable, IReadOnlyList<string> argv, CancellationToken cancellationToken);
    Task KillContainerAsync(string executable, string containerName, CancellationToken cancellationToken);
}

/// <param name="ExitCode">Process exit code.</param>
/// <param name="StdOut">Captured stdout.</param>
/// <param name="StdErr">Captured stderr.</param>
public sealed record DockerProcessResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Default <see cref="IDockerProcessRunner"/> that uses <see cref="System.Diagnostics.Process"/>.</summary>
public sealed class DefaultDockerProcessRunner : IDockerProcessRunner
{
    public async Task<DockerProcessResult> RunAsync(string executable, IReadOnlyList<string> argv, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in argv)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* best-effort kill */ }
            throw;
        }

        return new DockerProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public async Task KillContainerAsync(string executable, string containerName, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("kill");
        psi.ArgumentList.Add(containerName);
        using var process = new Process { StartInfo = psi };
        process.Start();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    }
}
