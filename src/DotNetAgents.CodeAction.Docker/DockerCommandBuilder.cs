namespace DotNetAgents.CodeAction.Docker;

/// <summary>
/// Builds the argv array used to invoke docker for a single code-action execution. The
/// argv-only output is the unit-testable surface — the runtime composes this builder with the
/// process invocation, so security regressions can be caught with cheap deterministic tests.
/// </summary>
public static class DockerCommandBuilder
{
    /// <summary>
    /// Render the docker argv for the given request and options.
    /// </summary>
    /// <param name="request">The code-action request.</param>
    /// <param name="options">Sandbox options.</param>
    /// <param name="codeFilePathOnHost">Absolute path on the host to the temp file holding <c>request.Code</c>.</param>
    /// <param name="containerName">Stable container name used for diagnostics + cleanup.</param>
    public static IReadOnlyList<string> Build(
        CodeActionRequest request,
        DockerSandboxOptions options,
        string codeFilePathOnHost,
        string containerName)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(codeFilePathOnHost)) throw new ArgumentException("Code file path required", nameof(codeFilePathOnHost));
        if (string.IsNullOrWhiteSpace(containerName)) throw new ArgumentException("Container name required", nameof(containerName));

        var argv = new List<string>
        {
            "run",
            "--rm",
            "--name", containerName,
            "--read-only",
            "--tmpfs", "/tmp:rw,size=64m,mode=1777",
            "--user", options.ContainerUser,
            "--workdir", options.WorkingDirectory,
            "--cpus", options.CpuLimit,
            "--memory", options.MemoryLimit,
            "--memory-swap", options.MemoryLimit,
            "--pids-limit", options.PidsLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if (options.EnableHardenedSecurityOpts)
        {
            argv.Add("--cap-drop");
            argv.Add("ALL");
            argv.Add("--security-opt");
            argv.Add("no-new-privileges");
        }

        if (request.AllowNetwork)
        {
            // Operator-permitted egress: still forbid host network and explicit allow-list when supplied.
            argv.Add("--network");
            argv.Add("bridge");
            if (request.AllowedHosts is { Count: > 0 } hosts)
            {
                foreach (var host in hosts)
                {
                    argv.Add("--add-host");
                    argv.Add($"{host}:0.0.0.0");
                }
            }
        }
        else
        {
            argv.Add("--network");
            argv.Add("none");
        }

        // Code file mounted read-only at /sandbox/main.py.
        argv.Add("--mount");
        argv.Add($"type=bind,source={codeFilePathOnHost},target={options.WorkingDirectory}/main.py,readonly");

        // Operator-supplied environment variables.
        if (request.Environment is { Count: > 0 } env)
        {
            foreach (var (k, v) in env)
            {
                argv.Add("--env");
                argv.Add($"{k}={v}");
            }
        }

        argv.Add(options.Image);
        argv.Add("python");
        argv.Add($"{options.WorkingDirectory}/main.py");
        return argv;
    }
}
