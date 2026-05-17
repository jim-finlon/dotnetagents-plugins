using Microsoft.Extensions.Logging;

namespace DotNetAgents.Database.Orchestration;

/// <summary>
/// Interface for collecting database operation metrics.
/// </summary>
public interface IDatabaseMetricsCollector
{
    /// <summary>
    /// Records the start of a database operation.
    /// </summary>
    /// <param name="operationId">The operation ID.</param>
    /// <param name="operationType">The operation type.</param>
    void RecordOperationStart(string operationId, string operationType);

    /// <summary>
    /// Records the completion of a database operation.
    /// </summary>
    /// <param name="operationId">The operation ID.</param>
    /// <param name="duration">The operation duration.</param>
    /// <param name="success">Whether the operation succeeded.</param>
    void RecordOperationComplete(string operationId, TimeSpan duration, bool success);

    /// <summary>
    /// Records query execution metrics.
    /// </summary>
    /// <param name="query">The query executed.</param>
    /// <param name="duration">The execution duration.</param>
    /// <param name="rowsAffected">The number of rows affected.</param>
    void RecordQueryExecution(string query, TimeSpan duration, long rowsAffected);
}

/// <summary>
/// Implementation of database metrics collector.
/// </summary>
public sealed class DatabaseMetricsCollector : IDatabaseMetricsCollector
{
    private readonly ILogger<DatabaseMetricsCollector>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseMetricsCollector"/> class.
    /// </summary>
    /// <param name="logger">Optional logger instance.</param>
    public DatabaseMetricsCollector(ILogger<DatabaseMetricsCollector>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void RecordOperationStart(string operationId, string operationType)
    {
        _logger?.LogInformation("Database operation started: {OperationId}, Type: {OperationType}", operationId, operationType);
        // Would integrate with OpenTelemetry metrics here
    }

    /// <inheritdoc />
    public void RecordOperationComplete(string operationId, TimeSpan duration, bool success)
    {
        _logger?.LogInformation(
            "Database operation completed: {OperationId}, Duration: {Duration}ms, Success: {Success}",
            operationId,
            duration.TotalMilliseconds,
            success);
        // Would integrate with OpenTelemetry metrics here
    }

    /// <inheritdoc />
    public void RecordQueryExecution(string query, TimeSpan duration, long rowsAffected)
    {
        _logger?.LogDebug(
            "Query executed: Duration: {Duration}ms, Rows: {RowsAffected}",
            duration.TotalMilliseconds,
            rowsAffected);
        // Would integrate with OpenTelemetry metrics here
    }
}
