using DotNetAgents.Agents.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;
using TaskStatus = DotNetAgents.Agents.Tasks.TaskStatus;

namespace DotNetAgents.Storage.Agents.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of <see cref="ITaskStore"/>.
/// Suitable for distributed deployments requiring persistent task storage.
/// </summary>
public class PostgreSQLTaskStore : ITaskStore
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSQLTaskStore>? _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLTaskStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="logger">Optional logger instance.</param>
    public PostgreSQLTaskStore(
        string connectionString,
        ILogger<PostgreSQLTaskStore>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        WorkerTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureSchemaExistsAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
            INSERT INTO task_store (
                task_id, task_type, input_data, required_capability, preferred_agent_id,
                priority, timeout_ms, metadata, created_at, status
            )
            VALUES (
                @task_id, @task_type, @input_data, @required_capability, @preferred_agent_id,
                @priority, @timeout_ms, @metadata, @created_at, @status
            )
            ON CONFLICT (task_id) DO UPDATE SET
                task_type = EXCLUDED.task_type,
                input_data = EXCLUDED.input_data,
                required_capability = EXCLUDED.required_capability,
                preferred_agent_id = EXCLUDED.preferred_agent_id,
                priority = EXCLUDED.priority,
                timeout_ms = EXCLUDED.timeout_ms,
                metadata = EXCLUDED.metadata,
                status = EXCLUDED.status,
                updated_at = NOW()";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("task_id", task.TaskId);
        command.Parameters.AddWithValue("task_type", task.TaskType);
        command.Parameters.AddWithValue("input_data", JsonSerializer.Serialize(task.Input, _jsonOptions));
        command.Parameters.AddWithValue("required_capability", (object?)task.RequiredCapability ?? DBNull.Value);
        command.Parameters.AddWithValue("preferred_agent_id", (object?)task.PreferredAgentId ?? DBNull.Value);
        command.Parameters.AddWithValue("priority", task.Priority);
        command.Parameters.AddWithValue("timeout_ms", (object?)task.Timeout?.TotalMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(task.Metadata, _jsonOptions));
        command.Parameters.AddWithValue("created_at", task.CreatedAt);
        command.Parameters.AddWithValue("status", (int)TaskStatus.Pending);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("Saved task {TaskId} to PostgreSQL store", task.TaskId);
    }

    /// <inheritdoc />
    public async Task<WorkerTask?> GetAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(taskId);
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = @"
            SELECT task_id, task_type, input_data, required_capability, preferred_agent_id,
                   priority, timeout_ms, metadata, created_at
            FROM task_store
            WHERE task_id = @task_id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("task_id", taskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapToWorkerTask(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task SaveResultAsync(
        WorkerTaskResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureSchemaExistsAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
            INSERT INTO task_results (
                task_id, success, output_data, error_message, worker_agent_id,
                execution_time_ms, metadata, completed_at
            )
            VALUES (
                @task_id, @success, @output_data, @error_message, @worker_agent_id,
                @execution_time_ms, @metadata, @completed_at
            )
            ON CONFLICT (task_id) DO UPDATE SET
                success = EXCLUDED.success,
                output_data = EXCLUDED.output_data,
                error_message = EXCLUDED.error_message,
                worker_agent_id = EXCLUDED.worker_agent_id,
                execution_time_ms = EXCLUDED.execution_time_ms,
                metadata = EXCLUDED.metadata,
                completed_at = EXCLUDED.completed_at";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("task_id", result.TaskId);
        command.Parameters.AddWithValue("success", result.Success);
        command.Parameters.AddWithValue("output_data", result.Output != null
            ? JsonSerializer.Serialize(result.Output, _jsonOptions)
            : (object)DBNull.Value);
        command.Parameters.AddWithValue("error_message", (object?)result.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("worker_agent_id", result.WorkerAgentId);
        command.Parameters.AddWithValue("execution_time_ms", result.ExecutionTime.TotalMilliseconds);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(result.Metadata, _jsonOptions));
        command.Parameters.AddWithValue("completed_at", result.CompletedAt);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // Update task status
        await UpdateStatusAsync(result.TaskId, result.Success ? TaskStatus.Completed : TaskStatus.Failed, cancellationToken)
            .ConfigureAwait(false);

        _logger?.LogDebug(
            "Saved result for task {TaskId} (Success: {Success})",
            result.TaskId,
            result.Success);
    }

    /// <inheritdoc />
    public async Task<WorkerTaskResult?> GetResultAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(taskId);
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = @"
            SELECT task_id, success, output_data, error_message, worker_agent_id,
                   execution_time_ms, metadata, completed_at
            FROM task_results
            WHERE task_id = @task_id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("task_id", taskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapToWorkerTaskResult(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(
        string taskId,
        TaskStatus status,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(taskId);
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = "UPDATE task_store SET status = @status, updated_at = NOW() WHERE task_id = @task_id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("status", (int)status);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("Updated status for task {TaskId} to {Status}", taskId, status);
    }

    private WorkerTask MapToWorkerTask(NpgsqlDataReader reader)
    {
        var inputJson = reader.GetString(reader.GetOrdinal("input_data"));
        var input = JsonSerializer.Deserialize<object>(inputJson, _jsonOptions) ?? new object();

        var metadataJson = reader.GetString(reader.GetOrdinal("metadata"));
        var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson, _jsonOptions)
            ?? new Dictionary<string, object>();

        TimeSpan? timeoutMs = null;
        if (!reader.IsDBNull(reader.GetOrdinal("timeout_ms")))
        {
            timeoutMs = TimeSpan.FromMilliseconds(reader.GetInt64(reader.GetOrdinal("timeout_ms")));
        }

        return new WorkerTask
        {
            TaskId = reader.GetString(reader.GetOrdinal("task_id")),
            TaskType = reader.GetString(reader.GetOrdinal("task_type")),
            Input = input,
            RequiredCapability = reader.IsDBNull(reader.GetOrdinal("required_capability"))
                ? null
                : reader.GetString(reader.GetOrdinal("required_capability")),
            PreferredAgentId = reader.IsDBNull(reader.GetOrdinal("preferred_agent_id"))
                ? null
                : reader.GetString(reader.GetOrdinal("preferred_agent_id")),
            Priority = reader.GetInt32(reader.GetOrdinal("priority")),
            Timeout = timeoutMs,
            Metadata = metadata,
            CreatedAt = new DateTimeOffset(reader.GetDateTime(reader.GetOrdinal("created_at")))
        };
    }

    private WorkerTaskResult MapToWorkerTaskResult(NpgsqlDataReader reader)
    {
        object? output = null;
        if (!reader.IsDBNull(reader.GetOrdinal("output_data")))
        {
            var outputJson = reader.GetString(reader.GetOrdinal("output_data"));
            output = JsonSerializer.Deserialize<object>(outputJson, _jsonOptions);
        }

        var metadataJson = reader.GetString(reader.GetOrdinal("metadata"));
        var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson, _jsonOptions)
            ?? new Dictionary<string, object>();

        return new WorkerTaskResult
        {
            TaskId = reader.GetString(reader.GetOrdinal("task_id")),
            Success = reader.GetBoolean(reader.GetOrdinal("success")),
            Output = output,
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message"))
                ? null
                : reader.GetString(reader.GetOrdinal("error_message")),
            WorkerAgentId = reader.GetString(reader.GetOrdinal("worker_agent_id")),
            ExecutionTime = TimeSpan.FromMilliseconds(reader.GetInt64(reader.GetOrdinal("execution_time_ms"))),
            Metadata = metadata,
            CompletedAt = new DateTimeOffset(reader.GetDateTime(reader.GetOrdinal("completed_at")))
        };
    }

    private async Task EnsureSchemaExistsAsync(CancellationToken cancellationToken)
    {
        const string createTablesSql = @"
            CREATE TABLE IF NOT EXISTS task_store (
                task_id VARCHAR(255) PRIMARY KEY,
                task_type VARCHAR(255) NOT NULL,
                input_data JSONB NOT NULL,
                required_capability VARCHAR(255),
                preferred_agent_id VARCHAR(255),
                priority INTEGER NOT NULL DEFAULT 0,
                timeout_ms BIGINT,
                metadata JSONB NOT NULL DEFAULT '{}'::JSONB,
                status INTEGER NOT NULL DEFAULT 0,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS task_results (
                task_id VARCHAR(255) PRIMARY KEY,
                success BOOLEAN NOT NULL,
                output_data JSONB,
                error_message TEXT,
                worker_agent_id VARCHAR(255) NOT NULL,
                execution_time_ms BIGINT NOT NULL,
                metadata JSONB NOT NULL DEFAULT '{}'::JSONB,
                completed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS idx_task_store_status ON task_store(status);
            CREATE INDEX IF NOT EXISTS idx_task_store_type ON task_store(task_type);
            CREATE INDEX IF NOT EXISTS idx_task_results_worker ON task_results(worker_agent_id);
            CREATE INDEX IF NOT EXISTS idx_task_results_completed ON task_results(completed_at);";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(createTablesSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
