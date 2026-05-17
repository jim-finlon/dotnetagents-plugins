using DotNetAgents.Agents.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;
using TaskStatus = DotNetAgents.Agents.Tasks.TaskStatus;

namespace DotNetAgents.Storage.Agents.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of <see cref="ITaskQueue"/>.
/// Suitable for distributed deployments requiring persistent task queues.
/// </summary>
public class PostgreSQLTaskQueue : ITaskQueue
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSQLTaskQueue>? _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLTaskQueue"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="logger">Optional logger instance.</param>
    public PostgreSQLTaskQueue(
        string connectionString,
        ILogger<PostgreSQLTaskQueue>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(
        WorkerTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureSchemaExistsAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
            INSERT INTO task_queue (
                task_id, task_type, input_data, required_capability, preferred_agent_id,
                priority, timeout_ms, metadata, created_at, status
            )
            VALUES (
                @task_id, @task_type, @input_data, @required_capability, @preferred_agent_id,
                @priority, @timeout_ms, @metadata, @created_at, @status
            )";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        var metadata = BuildQueueMetadata(task);
        command.Parameters.AddWithValue("task_id", task.TaskId);
        command.Parameters.AddWithValue("task_type", task.TaskType);
        command.Parameters.AddWithValue("input_data", JsonSerializer.Serialize(task.Input, _jsonOptions));
        command.Parameters.AddWithValue("required_capability", (object?)task.RequiredCapability ?? DBNull.Value);
        command.Parameters.AddWithValue("preferred_agent_id", (object?)task.PreferredAgentId ?? DBNull.Value);
        command.Parameters.AddWithValue("priority", task.Priority);
        command.Parameters.AddWithValue("timeout_ms", (object?)task.Timeout?.TotalMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(metadata, _jsonOptions));
        command.Parameters.AddWithValue("created_at", task.CreatedAt);
        command.Parameters.AddWithValue("status", (int)TaskStatus.Pending);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("Enqueued task {TaskId} to PostgreSQL queue", task.TaskId);
    }

    /// <inheritdoc />
    public async Task<WorkerTask?> DequeueAsync(
        string? agentId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureSchemaExistsAsync(cancellationToken).ConfigureAwait(false);

        string sql;
        if (!string.IsNullOrEmpty(agentId))
        {
            sql = @"
                UPDATE task_queue
                SET status = @assigned_status, assigned_at = NOW()
                WHERE task_id = (
                    SELECT task_id
                    FROM task_queue
                    WHERE status = @pending_status
                      AND (preferred_agent_id IS NULL OR preferred_agent_id = @agent_id)
                    ORDER BY priority DESC, created_at ASC
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING task_id, task_type, input_data, required_capability, preferred_agent_id,
                          priority, timeout_ms, metadata, created_at";
        }
        else
        {
            sql = @"
                UPDATE task_queue
                SET status = @assigned_status, assigned_at = NOW()
                WHERE task_id = (
                    SELECT task_id
                    FROM task_queue
                    WHERE status = @pending_status
                    ORDER BY priority DESC, created_at ASC
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING task_id, task_type, input_data, required_capability, preferred_agent_id,
                          priority, timeout_ms, metadata, created_at";
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("assigned_status", (int)TaskStatus.Assigned);
        command.Parameters.AddWithValue("pending_status", (int)TaskStatus.Pending);
        if (!string.IsNullOrEmpty(agentId))
        {
            command.Parameters.AddWithValue("agent_id", agentId);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapToWorkerTask(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<int> GetPendingCountAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureSchemaExistsAsync(cancellationToken).ConfigureAwait(false);

        const string sql = "SELECT COUNT(*) FROM task_queue WHERE status = @pending_status";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("pending_status", (int)TaskStatus.Pending);

        var count = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(count);
    }

    /// <inheritdoc />
    public async Task<QueuePostureSnapshot> GetPostureAsync(
        string? queueKey = null,
        int? maxDepthBudget = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureSchemaExistsAsync(cancellationToken).ConfigureAwait(false);

        const string countsSql = @"
            SELECT
                COUNT(*) FILTER (WHERE status = @pending_status) AS pending_count,
                COUNT(*) FILTER (WHERE status IN (@assigned_status, @in_progress_status)) AS active_count,
                COUNT(*) FILTER (WHERE status = @completed_status) AS completed_count,
                COUNT(*) FILTER (WHERE status = @failed_status) AS failed_count,
                MIN(created_at) FILTER (WHERE status = @pending_status) AS oldest_pending_at
            FROM task_queue";

        const string itemsSql = @"
            SELECT task_id, task_type, input_data, required_capability, preferred_agent_id,
                   priority, timeout_ms, metadata, created_at, status, assigned_at
            FROM task_queue
            WHERE status IN (@pending_status, @assigned_status, @in_progress_status)
            ORDER BY priority DESC, created_at ASC
            LIMIT 50";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        int pending;
        int active;
        int completed;
        int failed;
        DateTimeOffset? oldestPending = null;
        await using (var countsCommand = new NpgsqlCommand(countsSql, connection))
        {
            AddStatusParameters(countsCommand);
            await using var reader = await countsCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            pending = Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("pending_count")));
            active = Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("active_count")));
            completed = Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("completed_count")));
            failed = Convert.ToInt32(reader.GetInt64(reader.GetOrdinal("failed_count")));
            if (!reader.IsDBNull(reader.GetOrdinal("oldest_pending_at")))
                oldestPending = new DateTimeOffset(reader.GetDateTime(reader.GetOrdinal("oldest_pending_at")));
        }

        var items = new List<QueueWorkItemSnapshot>();
        await using (var itemsCommand = new NpgsqlCommand(itemsSql, connection))
        {
            AddStatusParameters(itemsCommand);
            await using var reader = await itemsCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var task = MapToWorkerTask(reader);
                var status = (TaskStatus)reader.GetInt32(reader.GetOrdinal("status"));
                var assignedAt = reader.IsDBNull(reader.GetOrdinal("assigned_at"))
                    ? (DateTimeOffset?)null
                    : new DateTimeOffset(reader.GetDateTime(reader.GetOrdinal("assigned_at")));
                items.Add(ToSnapshot(task, status, assignedAt));
            }
        }

        return new QueuePostureSnapshot(
            queueKey ?? "postgresql",
            ClassifyBackpressure(pending, maxDepthBudget),
            pending,
            active,
            completed,
            failed,
            items.Count(static item => item.RetryBudget.Attempts > 0),
            maxDepthBudget,
            oldestPending,
            items);
    }

    /// <inheritdoc />
    public async Task<WorkerTask?> PeekAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureSchemaExistsAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
            SELECT task_id, task_type, input_data, required_capability, preferred_agent_id,
                   priority, timeout_ms, metadata, created_at
            FROM task_queue
            WHERE status = @pending_status
            ORDER BY priority DESC, created_at ASC
            LIMIT 1";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("pending_status", (int)TaskStatus.Pending);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapToWorkerTask(reader);
        }

        return null;
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
            QueueKey = TextMetadata(metadata, "queueKey"),
            CorrelationId = TextMetadata(metadata, "correlationId"),
            StoryId = TextMetadata(metadata, "storyId"),
            ParentTaskId = TextMetadata(metadata, "parentTaskId"),
            RetryCount = IntMetadata(metadata, "retryCount"),
            MaxRetryAttempts = IntMetadata(metadata, "maxRetryAttempts", 3),
            NextRetryAtUtc = DateMetadata(metadata, "nextRetryAtUtc"),
            Metadata = metadata,
            CreatedAt = new DateTimeOffset(reader.GetDateTime(reader.GetOrdinal("created_at")))
        };
    }

    private static Dictionary<string, object> BuildQueueMetadata(WorkerTask task)
    {
        var metadata = new Dictionary<string, object>(task.Metadata, StringComparer.OrdinalIgnoreCase)
        {
            ["retryCount"] = task.RetryCount,
            ["maxRetryAttempts"] = task.MaxRetryAttempts
        };

        AddIfPresent(metadata, "queueKey", task.QueueKey);
        AddIfPresent(metadata, "correlationId", task.CorrelationId);
        AddIfPresent(metadata, "storyId", task.StoryId);
        AddIfPresent(metadata, "parentTaskId", task.ParentTaskId);
        if (task.NextRetryAtUtc.HasValue)
            metadata["nextRetryAtUtc"] = task.NextRetryAtUtc.Value;
        return metadata;
    }

    private static QueueWorkItemSnapshot ToSnapshot(WorkerTask task, TaskStatus status, DateTimeOffset? assignedAt)
        => new(
            new QueueWorkItemIdentity(task.TaskId, task.TaskType, task.QueueKey, task.CorrelationId, task.StoryId, task.ParentTaskId),
            ToDisposition(status),
            new RetryBudget(task.RetryCount, task.MaxRetryAttempts, task.NextRetryAtUtc),
            task.Priority,
            task.CreatedAt,
            status == TaskStatus.Pending ? task.CreatedAt : null,
            assignedAt);

    private static QueueWorkDisposition ToDisposition(TaskStatus status)
        => status switch
        {
            TaskStatus.Pending => QueueWorkDisposition.Pending,
            TaskStatus.Assigned or TaskStatus.InProgress => QueueWorkDisposition.Active,
            TaskStatus.Completed => QueueWorkDisposition.Completed,
            TaskStatus.Failed => QueueWorkDisposition.Failed,
            TaskStatus.Cancelled => QueueWorkDisposition.Cancelled,
            _ => QueueWorkDisposition.Unknown
        };

    private static QueueBackpressureState ClassifyBackpressure(int pendingDepth, int? maxDepthBudget)
    {
        if (pendingDepth == 0)
            return QueueBackpressureState.Empty;
        if (maxDepthBudget is null or <= 0)
            return QueueBackpressureState.Normal;
        if (pendingDepth >= maxDepthBudget)
            return QueueBackpressureState.Saturated;
        if (pendingDepth >= Math.Ceiling(maxDepthBudget.Value * 0.8d))
            return QueueBackpressureState.Constrained;
        return QueueBackpressureState.Normal;
    }

    private static void AddStatusParameters(NpgsqlCommand command)
    {
        command.Parameters.AddWithValue("pending_status", (int)TaskStatus.Pending);
        command.Parameters.AddWithValue("assigned_status", (int)TaskStatus.Assigned);
        command.Parameters.AddWithValue("in_progress_status", (int)TaskStatus.InProgress);
        command.Parameters.AddWithValue("completed_status", (int)TaskStatus.Completed);
        command.Parameters.AddWithValue("failed_status", (int)TaskStatus.Failed);
    }

    private static void AddIfPresent(Dictionary<string, object> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            metadata[key] = value;
    }

    private static string? TextMetadata(IReadOnlyDictionary<string, object> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static int IntMetadata(IReadOnlyDictionary<string, object> metadata, string key, int fallback = 0)
        => metadata.TryGetValue(key, out var value) && int.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : fallback;

    private static DateTimeOffset? DateMetadata(IReadOnlyDictionary<string, object> metadata, string key)
        => metadata.TryGetValue(key, out var value) && DateTimeOffset.TryParse(value?.ToString(), out var parsed)
            ? parsed
            : null;

    private async Task EnsureSchemaExistsAsync(CancellationToken cancellationToken)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS task_queue (
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
                assigned_at TIMESTAMPTZ
            );

            CREATE INDEX IF NOT EXISTS idx_task_queue_status ON task_queue(status);
            CREATE INDEX IF NOT EXISTS idx_task_queue_priority ON task_queue(priority DESC, created_at ASC);
            CREATE INDEX IF NOT EXISTS idx_task_queue_type ON task_queue(task_type);
            CREATE INDEX IF NOT EXISTS idx_task_queue_preferred_agent ON task_queue(preferred_agent_id)
                WHERE preferred_agent_id IS NOT NULL;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
