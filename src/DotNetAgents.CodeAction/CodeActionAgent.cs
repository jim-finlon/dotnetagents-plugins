using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotNetAgents.CodeAction;

/// <summary>
/// Minimal LLM ↔ sandbox orchestration loop. Given an LLM completion delegate and a runtime,
/// asks the LLM for code, extracts blocks, executes the first non-empty block, and returns the
/// final <see cref="CodeActionResult"/> together with the LLM transcript so callers can decide
/// whether to iterate.
/// </summary>
/// <remarks>
/// <para>
/// The agent does not own the LLM client — it accepts a <see cref="LlmCompletion"/> delegate so
/// the same code can be driven by Local-LLM gateway calls, mocked test responders, or any
/// other completion source. This keeps the package free of vendor SDK dependencies.
/// </para>
/// <para>
/// The default system prompt instructs the model to emit Python in <c>&lt;code&gt;...&lt;/code&gt;</c>
/// blocks. Callers can override via <see cref="CodeActionAgentOptions.SystemPrompt"/>.
/// </para>
/// </remarks>
public sealed class CodeActionAgent
{
    private readonly ICodeActionRuntime _runtime;
    private readonly IExecutionSandboxManager _sandboxManager;
    private readonly ILogger<CodeActionAgent> _logger;
    private readonly CodeActionAgentOptions _options;

    public CodeActionAgent(
        ICodeActionRuntime runtime,
        IExecutionSandboxManager? sandboxManager = null,
        CodeActionAgentOptions? options = null,
        ILogger<CodeActionAgent>? logger = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _sandboxManager = sandboxManager ?? new DefaultExecutionSandboxManager();
        _options = options ?? new CodeActionAgentOptions();
        _logger = logger ?? NullLogger<CodeActionAgent>.Instance;
    }

    /// <summary>
    /// Run one code-action turn: prompt → LLM completion → extract first code block → sandbox
    /// execute → return.
    /// </summary>
    /// <param name="userTask">Operator-readable description of the task.</param>
    /// <param name="completion">LLM completion delegate the agent calls with the system prompt + user task.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CodeActionTurnResult> RunOnceAsync(
        string userTask,
        LlmCompletion completion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userTask);
        ArgumentNullException.ThrowIfNull(completion);

        var systemPrompt = _options.SystemPrompt;
        var llmResponse = await completion(systemPrompt, userTask, cancellationToken).ConfigureAwait(false);

        var blocks = CodeBlockExtractor.Extract(llmResponse);
        if (blocks.Count == 0)
        {
            _logger.LogInformation("CodeActionAgent: LLM response had no code blocks; returning text-only turn.");
            return new CodeActionTurnResult(
                LlmResponse: llmResponse ?? string.Empty,
                Code: null,
                ExecutionResult: null);
        }

        if (blocks.Count > 1 && !_options.AllowMultipleCodeBlocks)
        {
            _logger.LogWarning("CodeActionAgent: LLM emitted {Count} code blocks but AllowMultipleCodeBlocks=false; using the first.", blocks.Count);
        }

        var code = blocks[0];
        var request = new CodeActionRequest(
            Code: code,
            Language: _options.Language,
            Timeout: _options.ExecutionTimeout,
            AllowNetwork: _options.AllowNetwork,
            AllowedHosts: _options.AllowedHosts);

        var execution = await _sandboxManager.ExecuteAsync(_runtime, request, cancellationToken).ConfigureAwait(false);
        return new CodeActionTurnResult(
            LlmResponse: llmResponse ?? string.Empty,
            Code: code,
            ExecutionResult: execution.Result,
            SandboxReceipt: execution.Receipt);
    }
}

/// <summary>Delegate type the agent uses to call any LLM provider.</summary>
public delegate Task<string> LlmCompletion(string systemPrompt, string userPrompt, CancellationToken cancellationToken);

/// <summary>Operator-tunable knobs on the orchestrator loop.</summary>
public sealed class CodeActionAgentOptions
{
    /// <summary>System prompt prepended to every LLM call. Default instructs the model to use <c>&lt;code&gt;</c> blocks.</summary>
    public string SystemPrompt { get; set; } =
        "You are a code-action agent. When the user asks you to perform a task, respond with a single Python code block wrapped in <code>...</code> tags. Do not narrate. The code will be executed in a sandboxed environment.";

    /// <summary>Per-execution wall-clock timeout passed to the sandbox. Defaults to 30 seconds.</summary>
    public TimeSpan? ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether the sandbox is allowed network egress for this turn.</summary>
    public bool AllowNetwork { get; set; }

    /// <summary>Egress allow-list when <see cref="AllowNetwork"/> is true.</summary>
    public IReadOnlyList<string>? AllowedHosts { get; set; }

    /// <summary>Code language; defaults to <c>python</c>.</summary>
    public string Language { get; set; } = "python";

    /// <summary>When false (default) the agent uses only the first code block found.</summary>
    public bool AllowMultipleCodeBlocks { get; set; }
}

/// <summary>Result of one code-action turn, exposing both LLM transcript and sandbox outcome.</summary>
/// <param name="LlmResponse">The raw LLM response, including text outside any code block.</param>
/// <param name="Code">The first code block executed, or <c>null</c> when the model emitted no block.</param>
/// <param name="ExecutionResult">Sandbox result, or <c>null</c> when no code was executed.</param>
/// <param name="SandboxReceipt">Operator-facing lifecycle receipt for executed code, or <c>null</c> when no code was executed.</param>
public sealed record CodeActionTurnResult(
    string LlmResponse,
    string? Code,
    CodeActionResult? ExecutionResult,
    ExecutionSandboxReceipt? SandboxReceipt = null)
{
    public bool ExecutedCode => ExecutionResult is not null;
}
