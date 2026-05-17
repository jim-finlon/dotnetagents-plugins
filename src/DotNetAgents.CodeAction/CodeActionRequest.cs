namespace DotNetAgents.CodeAction;

/// <summary>
/// Input for a single sandboxed code-action execution.
/// </summary>
/// <param name="Code">The code body to execute. Treated as untrusted input — sandbox MUST isolate.</param>
/// <param name="Language">Code language. Default <c>python</c>; sandbox implementations decide which they support.</param>
/// <param name="Timeout">Hard wall-clock limit for the execution. Sandbox enforces; orchestrator also enforces as defense in depth.</param>
/// <param name="AllowNetwork">When <c>true</c> the sandbox MAY allow network egress (still subject to operator policy). Default <c>false</c>.</param>
/// <param name="AllowedHosts">Optional egress allow-list (host names). Honored only when <see cref="AllowNetwork"/> is <c>true</c>.</param>
/// <param name="Environment">Environment variables to expose inside the sandbox. Caller is responsible for not leaking secrets.</param>
/// <param name="WorkingDirectoryFiles">Optional read-only files to drop into the sandbox at the documented path before execution. Useful for "process this CSV" patterns.</param>
public sealed record CodeActionRequest(
    string Code,
    string Language = "python",
    TimeSpan? Timeout = null,
    bool AllowNetwork = false,
    IReadOnlyList<string>? AllowedHosts = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    IReadOnlyDictionary<string, byte[]>? WorkingDirectoryFiles = null);
