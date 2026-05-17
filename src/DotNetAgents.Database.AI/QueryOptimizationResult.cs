namespace DotNetAgents.Database.AI;

/// <summary>
/// Result of a query optimization operation.
/// </summary>
public sealed record QueryOptimizationResult
{
    /// <summary>
    /// Gets the original query.
    /// </summary>
    public required string OriginalQuery { get; init; }

    /// <summary>
    /// Gets the optimized query.
    /// </summary>
    public string? OptimizedQuery { get; init; }

    /// <summary>
    /// Gets optimization suggestions.
    /// </summary>
    public List<OptimizationSuggestion> Suggestions { get; init; } = [];

    /// <summary>
    /// Gets estimated performance improvement percentage.
    /// </summary>
    public int? EstimatedImprovementPercent { get; init; }

    /// <summary>
    /// Gets confidence score (0-100).
    /// </summary>
    public int ConfidenceScore { get; init; }

    /// <summary>
    /// Gets any warnings about the optimization.
    /// </summary>
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Represents a single optimization suggestion.
/// </summary>
public sealed class OptimizationSuggestion
{
    /// <summary>
    /// Gets the type of optimization.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the description of the suggestion.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the impact level (Low, Medium, High).
    /// </summary>
    public string Impact { get; init; } = "Medium";
}
