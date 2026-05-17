namespace DotNetAgents.Database.Orchestration;

/// <summary>
/// Interface for orchestrating database operations with checkpointing and progress tracking.
/// </summary>
public interface IDatabaseOperationOrchestrator
{
    /// <summary>
    /// Executes a database operation with checkpointing support.
    /// </summary>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="operationId">Optional operation ID for checkpointing.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The operation result.</returns>
    Task<OperationResult> ExecuteAsync(
        DatabaseOperation operation,
        string? operationId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a database operation to execute.
/// </summary>
public sealed class DatabaseOperation
{
    /// <summary>
    /// Gets the operation type.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the source connection string.
    /// </summary>
    public string? SourceConnectionString { get; init; }

    /// <summary>
    /// Gets the target connection string.
    /// </summary>
    public string? TargetConnectionString { get; init; }

    /// <summary>
    /// Gets the operation parameters.
    /// </summary>
    public Dictionary<string, object> Parameters { get; init; } = new();
}

/// <summary>
/// Result of a database operation.
/// </summary>
public sealed class OperationResult
{
    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the operation ID.
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// Gets any errors encountered.
    /// </summary>
    public List<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets any warnings.
    /// </summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Gets operation statistics.
    /// </summary>
    public Dictionary<string, object> Statistics { get; init; } = new();
}
