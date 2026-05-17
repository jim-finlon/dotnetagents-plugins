namespace DotNetAgents.ComputerUse;

/// <summary>Result of high-level task automation (multi-step with error recovery). CU-3.6.</summary>
public sealed record HighLevelTaskResult
{
    /// <summary>Whether the task completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Summary of what was done.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Number of steps executed.</summary>
    public int StepsExecuted { get; init; }

    /// <summary>Number of error recoveries applied (retries).</summary>
    public int RecoveriesApplied { get; init; }

    /// <summary>Error message when Success is false.</summary>
    public string? Error { get; init; }
}
