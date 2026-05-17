namespace DotNetAgents.CodeAction.Docker;

/// <summary>
/// Operator-tunable knobs for the Docker code-action sandbox. Bind from configuration at
/// section <see cref="SectionName"/>.
/// </summary>
public sealed class DockerSandboxOptions
{
    public const string SectionName = "DotNetAgents:CodeAction:Docker";

    /// <summary>Container image executed for every code-action request. Default is the DNA-curated minimal sandbox image.</summary>
    public string Image { get; set; } = "dotnetagents-sandbox:python-3.12-slim";

    /// <summary>Path to the docker CLI on the host. Override for environments where docker is not on PATH.</summary>
    public string DockerExecutable { get; set; } = "docker";

    /// <summary>CPU limit passed via <c>--cpus</c>. Defaults to <c>1.0</c>.</summary>
    public string CpuLimit { get; set; } = "1.0";

    /// <summary>Memory limit passed via <c>--memory</c>. Defaults to 256m.</summary>
    public string MemoryLimit { get; set; } = "256m";

    /// <summary>Process limit passed via <c>--pids-limit</c>. Defaults to 64.</summary>
    public int PidsLimit { get; set; } = 64;

    /// <summary>User to run inside the container. Defaults to <c>nobody</c>; never runs as root.</summary>
    public string ContainerUser { get; set; } = "nobody";

    /// <summary>Read-only working directory inside the container.</summary>
    public string WorkingDirectory { get; set; } = "/sandbox";

    /// <summary>
    /// When <c>true</c> (default) the runtime appends <c>--security-opt no-new-privileges</c>
    /// and drops all capabilities. Only an operator with a compelling reason should disable.
    /// </summary>
    public bool EnableHardenedSecurityOpts { get; set; } = true;

    /// <summary>
    /// When <c>true</c> the runtime renders the docker command and invokes it. When <c>false</c>
    /// the runtime returns the rendered argv as the result <c>StdOut</c> for inspection — used
    /// by tests and operator dry-runs.
    /// </summary>
    public bool DryRun { get; set; }
}
