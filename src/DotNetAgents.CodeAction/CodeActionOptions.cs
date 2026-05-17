namespace DotNetAgents.CodeAction;

/// <summary>
/// Operator-tunable defaults shared by all code-action runtimes. Bind from configuration at
/// section <see cref="SectionName"/>.
/// </summary>
public sealed class CodeActionOptions
{
    public const string SectionName = "DotNetAgents:CodeAction";

    /// <summary>Default per-execution wall-clock timeout. Hard kill, not soft cancel.</summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum bytes of stdout/stderr captured before truncation. Defaults to 256 KiB.</summary>
    public int MaxOutputBytes { get; set; } = 256 * 1024;

    /// <summary>Maximum bytes of <see cref="CodeActionRequest.Code"/> accepted before the runtime rejects.</summary>
    public int MaxCodeBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// When <c>true</c> the runtime refuses requests with <see cref="CodeActionRequest.AllowNetwork"/>
    /// set to <c>true</c>. Operators flip this in lab environments where network egress is allowed
    /// per task; default-off keeps production deployments safe.
    /// </summary>
    public bool DenyNetworkRequests { get; set; } = true;

    /// <summary>Globally-allowed egress hosts when <see cref="DenyNetworkRequests"/> is false.</summary>
    public IList<string> GloballyAllowedHosts { get; set; } = new List<string>();
}
