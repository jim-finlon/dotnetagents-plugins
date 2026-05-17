namespace DotNetAgents.CodeAction.Pyodide;

/// <summary>
/// Operator knobs for the Pyodide-based <see cref="ICodeActionRuntime"/>. Bind from
/// configuration at section <see cref="SectionName"/>.
/// </summary>
public sealed class PyodideSandboxOptions
{
    public const string SectionName = "DotNetAgents:CodeAction:Pyodide";

    /// <summary>Path to the Deno executable. Default <c>deno</c> (assumes on PATH).</summary>
    public string DenoExecutable { get; set; } = "deno";

    /// <summary>Path to the bundled Pyodide sidecar JS entry. Operators ship this with the host.</summary>
    public string SidecarScript { get; set; } = "./pyodide-sidecar/index.js";

    /// <summary>Hard wall-clock timeout per execution. Default 30s.</summary>
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to allow network access in the sidecar. Pyodide's network surface is limited
    /// (fetch only) and gated through Deno permissions; default <c>false</c>.
    /// </summary>
    public bool AllowNetwork { get; set; }
}
