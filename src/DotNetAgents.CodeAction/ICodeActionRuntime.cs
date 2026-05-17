namespace DotNetAgents.CodeAction;

/// <summary>
/// A sandboxed code-action runtime. Implementations are responsible for isolating untrusted code
/// from the host: read-only filesystem, dropped capabilities, no network unless explicitly
/// allowed, hard timeout, no privileged user. The runtime contract intentionally exposes only
/// <see cref="ExecuteAsync(CodeActionRequest, CancellationToken)"/> — there is no
/// "give me a shell" or "exec arbitrary host process" escape hatch.
/// </summary>
/// <remarks>
/// <para>
/// <b>Security contract.</b> A compliant <see cref="ICodeActionRuntime"/> MUST:
/// </para>
/// <list type="bullet">
///   <item><description>Reject empty, whitespace-only, or oversize <see cref="CodeActionRequest.Code"/> at the boundary, before any execution.</description></item>
///   <item><description>Enforce <see cref="CodeActionRequest.Timeout"/> (or <see cref="CodeActionOptions.DefaultTimeout"/>) as a hard kill, not a soft cancel.</description></item>
///   <item><description>Default <see cref="CodeActionRequest.AllowNetwork"/> to <c>false</c>; allow opt-in only when operator policy permits.</description></item>
///   <item><description>Truncate stdout/stderr to <see cref="CodeActionOptions.MaxOutputBytes"/> to prevent log bombs.</description></item>
///   <item><description>Treat the host filesystem as off-limits beyond a clearly bounded ephemeral working directory.</description></item>
/// </list>
/// </remarks>
public interface ICodeActionRuntime
{
    /// <summary>Stable id of this runtime (e.g. <c>docker</c>, <c>pyodide</c>, <c>unsandboxed-test</c>).</summary>
    string SandboxId { get; }

    /// <summary>Execute one code-action request inside the sandbox and return the structured result.</summary>
    Task<CodeActionResult> ExecuteAsync(
        CodeActionRequest request,
        CancellationToken cancellationToken = default);
}
