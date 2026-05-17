namespace DotNetAgents.ComputerUse;

/// <summary>High-level task automation: NL task description, multi-step plan, error recovery. FR-CU-004, CU-3.6.</summary>
public interface IHighLevelTaskAutomation
{
    /// <summary>Executes a task from a natural language description using a plan and optional error recovery.</summary>
    /// <param name="taskDescription">Natural language description of the task.</param>
    /// <param name="options">Optional settings (max retries, timeout).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<HighLevelTaskResult> ExecuteTaskAsync(string taskDescription, HighLevelTaskOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>Options for high-level task execution. CU-3.6.</summary>
public sealed record HighLevelTaskOptions
{
    /// <summary>Max retries per step on failure. Default 2.</summary>
    public int MaxRetriesPerStep { get; init; } = 2;

    /// <summary>Per-step timeout. Default 30s.</summary>
    public TimeSpan? StepTimeout { get; init; }
}
