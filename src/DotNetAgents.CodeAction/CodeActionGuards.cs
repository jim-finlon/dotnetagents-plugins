using Microsoft.Extensions.Options;

namespace DotNetAgents.CodeAction;

/// <summary>
/// Pre-execution validation shared by every <see cref="ICodeActionRuntime"/>. Rejects oversize
/// payloads, unbounded network requests, and misuse of <see cref="CodeActionOptions.DenyNetworkRequests"/>
/// before the request reaches the sandbox.
/// </summary>
public static class CodeActionGuards
{
    /// <summary>
    /// Validate <paramref name="request"/> against <paramref name="options"/>. Returns
    /// <c>null</c> when the request is acceptable; returns a populated <see cref="CodeActionResult"/>
    /// describing the rejection otherwise so callers can emit it as the result.
    /// </summary>
    public static CodeActionResult? RejectIfInvalid(
        CodeActionRequest request,
        CodeActionOptions options,
        string sandboxId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return new CodeActionResult(-1, string.Empty, string.Empty, TimeSpan.Zero, false, sandboxId,
                "Empty or whitespace-only code is rejected before sandbox start.");
        }

        var codeBytes = System.Text.Encoding.UTF8.GetByteCount(request.Code);
        if (codeBytes > options.MaxCodeBytes)
        {
            return new CodeActionResult(-1, string.Empty, string.Empty, TimeSpan.Zero, false, sandboxId,
                $"Code payload {codeBytes} bytes exceeds limit {options.MaxCodeBytes} bytes.");
        }

        if (request.AllowNetwork && options.DenyNetworkRequests)
        {
            return new CodeActionResult(-1, string.Empty, string.Empty, TimeSpan.Zero, false, sandboxId,
                "Network access requested but DenyNetworkRequests is true (operator policy).");
        }

        return null;
    }

    /// <summary>
    /// Resolve the effective timeout for this execution: the request's <see cref="CodeActionRequest.Timeout"/>
    /// when set, otherwise <see cref="CodeActionOptions.DefaultTimeout"/>. Negative or zero
    /// timeouts collapse to the default.
    /// </summary>
    public static TimeSpan ResolveTimeout(CodeActionRequest request, CodeActionOptions options)
    {
        var requested = request.Timeout ?? options.DefaultTimeout;
        return requested <= TimeSpan.Zero ? options.DefaultTimeout : requested;
    }

    /// <summary>Convenience for runtimes constructed with <see cref="IOptions{TOptions}"/>.</summary>
    public static CodeActionOptions ResolveOptions(IOptions<CodeActionOptions>? options)
        => options?.Value ?? new CodeActionOptions();
}
