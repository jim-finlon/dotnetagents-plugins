namespace DotNetAgents.Database.Orchestration;

/// <summary>
/// Represents progress of a database operation.
/// </summary>
public sealed class OperationProgress
{
    /// <summary>
    /// Gets the operation ID.
    /// </summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// Gets the current phase.
    /// </summary>
    public required string Phase { get; init; }

    /// <summary>
    /// Gets the progress percentage (0-100).
    /// </summary>
    public double ProgressPercent { get; init; }

    /// <summary>
    /// Gets the items processed.
    /// </summary>
    public long ItemsProcessed { get; init; }

    /// <summary>
    /// Gets the total items.
    /// </summary>
    public long TotalItems { get; init; }

    /// <summary>
    /// Gets the elapsed time.
    /// </summary>
    public TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// Gets the estimated time remaining.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; init; }
}
