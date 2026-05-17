namespace DotNetAgents.Database.Orchestration;

/// <summary>
/// Interface for error recovery services.
/// </summary>
public interface IErrorRecoveryService
{
    /// <summary>
    /// Records a recovery checkpoint.
    /// </summary>
    /// <param name="operationId">The operation ID.</param>
    /// <param name="checkpoint">The checkpoint data.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    Task RecordCheckpointAsync(
        string operationId,
        OperationCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a recovery checkpoint.
    /// </summary>
    /// <param name="operationId">The operation ID.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The checkpoint if found; otherwise, null.</returns>
    Task<OperationCheckpoint?> LoadCheckpointAsync(
        string operationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to recover from an error.
    /// </summary>
    /// <param name="operationId">The operation ID.</param>
    /// <param name="error">The error that occurred.</param>
    /// <param name="cancellationToken">Cancellation token for the async operation.</param>
    /// <returns>The recovery result.</returns>
    Task<RecoveryResult> RecoverAsync(
        string operationId,
        Exception error,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a checkpoint for a database operation.
/// </summary>
public sealed class OperationCheckpoint
{
    /// <summary>
    /// Gets the operation ID.
    /// </summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// Gets the checkpoint timestamp.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Gets the operation state at checkpoint.
    /// </summary>
    public required Dictionary<string, object> State { get; init; } = new();

    /// <summary>
    /// Gets the progress percentage (0-100).
    /// </summary>
    public double ProgressPercent { get; init; }
}

/// <summary>
/// Result of an error recovery operation.
/// </summary>
public sealed class RecoveryResult
{
    /// <summary>
    /// Gets a value indicating whether recovery was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the recovery strategy used.
    /// </summary>
    public RecoveryStrategy Strategy { get; init; }

    /// <summary>
    /// Gets any recovery messages.
    /// </summary>
    public List<string> Messages { get; init; } = [];
}

/// <summary>
/// Recovery strategy types.
/// </summary>
public enum RecoveryStrategy
{
    /// <summary>
    /// Retry the operation.
    /// </summary>
    Retry,

    /// <summary>
    /// Rollback and restart.
    /// </summary>
    Rollback,

    /// <summary>
    /// Resume from checkpoint.
    /// </summary>
    Resume,

    /// <summary>
    /// Skip and continue.
    /// </summary>
    Skip,

    /// <summary>
    /// Fail the operation.
    /// </summary>
    Fail
}
