namespace DotNetAgents.ComputerUse;

/// <summary>Result of a browser task (e.g. CompleteTaskAsync). FR-CU-003.</summary>
public sealed record WebTaskResult
{
    /// <summary>Whether the task completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Summary or output (e.g. "Submitted form", "Navigated to X").</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Final page URL if applicable.</summary>
    public string? FinalUrl { get; init; }

    /// <summary>Error message when Success is false.</summary>
    public string? Error { get; init; }
}
