using DotNetAgents.Tasks.Models;
using DotNetAgents.Tasks.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;
using TaskStatus = DotNetAgents.Tasks.Models.TaskStatus;

namespace DotNetAgents.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of <see cref="ITaskStore"/> for persistent task storage.
/// </summary>
public class PostgreSQLTaskStore : ITaskStore
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly ILogger<PostgreSQLTaskStore>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLTaskStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The table name for storing tasks. Default: "work_tasks".</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public PostgreSQLTaskStore(
        string connectionString,
        string tableName = "work_tasks",
        ILogger<PostgreSQLTaskStore>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _logger = logger;

        // Ensure table exists
        EnsureTableExistsAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task<WorkTask?> GetByIdAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT id, session_id, workflow_run_id, content, status, priority, ""order"",
                          depends_on, blocked_by, parent_task_id, notes, tags, metadata,
                          created_at, updated_at, started_at, completed_at, cancelled_at
                   FROM {_tableName}
                   WHERE id = @id",
                connection);

            command.Parameters.AddWithValue("@id", taskId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return ReadTask(reader);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get task. TaskId: {TaskId}", taskId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<WorkTask> CreateAsync(WorkTask task, CancellationToken cancellationToken = default)
    {
        if (task == null)
            throw new ArgumentNullException(nameof(task));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Auto-assign order if not set
            var order = task.Order;
            if (order == 0)
            {
                var maxOrderCommand = new NpgsqlCommand(
                    $@"SELECT COALESCE(MAX(""order""), -1) FROM {_tableName} WHERE session_id = @session_id",
                    connection);
                maxOrderCommand.Parameters.AddWithValue("@session_id", task.SessionId);
                var maxOrder = await maxOrderCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                order = Convert.ToInt32(maxOrder) + 1;
            }

            var command = new NpgsqlCommand(
                $@"INSERT INTO {_tableName}
                   (id, session_id, workflow_run_id, content, status, priority, ""order"",
                    depends_on, blocked_by, parent_task_id, notes, tags, metadata,
                    created_at, updated_at, started_at, completed_at, cancelled_at)
                   VALUES
                   (@id, @session_id, @workflow_run_id, @content, @status, @priority, @order,
                    @depends_on, @blocked_by, @parent_task_id, @notes, @tags, @metadata,
                    @created_at, @updated_at, @started_at, @completed_at, @cancelled_at)",
                connection);

            AddTaskParameters(command, task with { Order = order });

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Task created. TaskId: {TaskId}, SessionId: {SessionId}", task.Id, task.SessionId);

            return task with { Order = order };
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to create task. SessionId: {SessionId}", task.SessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<WorkTask> UpdateAsync(WorkTask task, CancellationToken cancellationToken = default)
    {
        if (task == null)
            throw new ArgumentNullException(nameof(task));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Get existing task to determine timestamp changes
            var existingTask = await GetByIdAsync(task.Id, cancellationToken).ConfigureAwait(false);
            if (existingTask == null)
            {
                throw new InvalidOperationException($"Task {task.Id} not found.");
            }

            var now = DateTimeOffset.UtcNow;
            var updatedTask = task with
            {
                UpdatedAt = now,
                StartedAt = GetStartedAt(existingTask, task, now),
                CompletedAt = GetCompletedAt(existingTask, task, now),
                CancelledAt = GetCancelledAt(existingTask, task, now)
            };

            var command = new NpgsqlCommand(
                $@"UPDATE {_tableName} SET
                   session_id = @session_id,
                   workflow_run_id = @workflow_run_id,
                   content = @content,
                   status = @status,
                   priority = @priority,
                   ""order"" = @order,
                   depends_on = @depends_on,
                   blocked_by = @blocked_by,
                   parent_task_id = @parent_task_id,
                   notes = @notes,
                   tags = @tags,
                   metadata = @metadata,
                   updated_at = @updated_at,
                   started_at = @started_at,
                   completed_at = @completed_at,
                   cancelled_at = @cancelled_at
                   WHERE id = @id",
                connection);

            AddTaskParameters(command, updatedTask);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (rowsAffected == 0)
            {
                throw new InvalidOperationException($"Task {task.Id} not found.");
            }

            _logger?.LogInformation("Task updated. TaskId: {TaskId}", updatedTask.Id);

            return updatedTask;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to update task. TaskId: {TaskId}", task.Id);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"DELETE FROM {_tableName} WHERE id = @id",
                connection);

            command.Parameters.AddWithValue("@id", taskId);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Task deleted. TaskId: {TaskId}", taskId);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to delete task. TaskId: {TaskId}", taskId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkTask>> GetBySessionIdAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT id, session_id, workflow_run_id, content, status, priority, ""order"",
                          depends_on, blocked_by, parent_task_id, notes, tags, metadata,
                          created_at, updated_at, started_at, completed_at, cancelled_at
                   FROM {_tableName}
                   WHERE session_id = @session_id
                   ORDER BY ""order""",
                connection);

            command.Parameters.AddWithValue("@session_id", sessionId);

            var tasks = new List<WorkTask>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(ReadTask(reader));
            }

            return tasks;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get tasks by session. SessionId: {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkTask>> GetByStatusAsync(
        string sessionId,
        TaskStatus status,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT id, session_id, workflow_run_id, content, status, priority, ""order"",
                          depends_on, blocked_by, parent_task_id, notes, tags, metadata,
                          created_at, updated_at, started_at, completed_at, cancelled_at
                   FROM {_tableName}
                   WHERE session_id = @session_id AND status = @status
                   ORDER BY ""order""",
                connection);

            command.Parameters.AddWithValue("@session_id", sessionId);
            command.Parameters.AddWithValue("@status", status.ToString());

            var tasks = new List<WorkTask>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(ReadTask(reader));
            }

            return tasks;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get tasks by status. SessionId: {SessionId}, Status: {Status}", sessionId, status);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkTask>> GetByWorkflowRunIdAsync(
        string workflowRunId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workflowRunId))
            throw new ArgumentException("Workflow run ID cannot be null or whitespace.", nameof(workflowRunId));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT id, session_id, workflow_run_id, content, status, priority, ""order"",
                          depends_on, blocked_by, parent_task_id, notes, tags, metadata,
                          created_at, updated_at, started_at, completed_at, cancelled_at
                   FROM {_tableName}
                   WHERE workflow_run_id = @workflow_run_id
                   ORDER BY ""order""",
                connection);

            command.Parameters.AddWithValue("@workflow_run_id", workflowRunId);

            var tasks = new List<WorkTask>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(ReadTask(reader));
            }

            return tasks;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get tasks by workflow run. WorkflowRunId: {WorkflowRunId}", workflowRunId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<TaskStatistics> GetStatisticsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT
                   COUNT(*) AS total,
                   SUM(CASE WHEN status = 'Pending' THEN 1 ELSE 0 END) AS pending,
                   SUM(CASE WHEN status = 'InProgress' THEN 1 ELSE 0 END) AS in_progress,
                   SUM(CASE WHEN status = 'Completed' THEN 1 ELSE 0 END) AS completed,
                   SUM(CASE WHEN status = 'Blocked' THEN 1 ELSE 0 END) AS blocked,
                   SUM(CASE WHEN status = 'Cancelled' THEN 1 ELSE 0 END) AS cancelled,
                   SUM(CASE WHEN status = 'Review' THEN 1 ELSE 0 END) AS review
                   FROM {_tableName}
                   WHERE session_id = @session_id",
                connection);

            command.Parameters.AddWithValue("@session_id", sessionId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new TaskStatistics();
            }

            var total = reader.GetInt32(reader.GetOrdinal("total"));
            var pending = reader.GetInt32(reader.GetOrdinal("pending"));
            var inProgress = reader.GetInt32(reader.GetOrdinal("in_progress"));
            var completed = reader.GetInt32(reader.GetOrdinal("completed"));
            var blocked = reader.GetInt32(reader.GetOrdinal("blocked"));
            var cancelled = reader.GetInt32(reader.GetOrdinal("cancelled"));
            var review = reader.GetInt32(reader.GetOrdinal("review"));

            return new TaskStatistics
            {
                Total = total,
                Pending = pending,
                InProgress = inProgress,
                Completed = completed,
                Blocked = blocked,
                Cancelled = cancelled,
                Review = review
            };
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get task statistics. SessionId: {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task ReorderAsync(
        string sessionId,
        Dictionary<Guid, int> taskOrders,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId));

        if (taskOrders == null)
            throw new ArgumentNullException(nameof(taskOrders));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var (taskId, newOrder) in taskOrders)
                {
                    var command = new NpgsqlCommand(
                        $@"UPDATE {_tableName}
                           SET ""order"" = @order, updated_at = @updated_at
                           WHERE id = @id AND session_id = @session_id",
                        connection,
                        transaction);

                    command.Parameters.AddWithValue("@id", taskId);
                    command.Parameters.AddWithValue("@session_id", sessionId);
                    command.Parameters.AddWithValue("@order", newOrder);
                    command.Parameters.AddWithValue("@updated_at", now);

                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to reorder tasks. SessionId: {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> AreDependenciesCompleteAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Get task dependencies
            var taskCommand = new NpgsqlCommand(
                $@"SELECT depends_on FROM {_tableName} WHERE id = @id",
                connection);
            taskCommand.Parameters.AddWithValue("@id", taskId);

            var dependsOnJson = await taskCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (string.IsNullOrWhiteSpace(dependsOnJson))
            {
                return true; // No dependencies
            }

            var dependsOn = JsonSerializer.Deserialize<List<Guid>>(dependsOnJson) ?? new List<Guid>();
            if (dependsOn.Count == 0)
            {
                return true;
            }

            // Check if all dependencies are completed
            var dependsOnJsonArray = string.Join(",", dependsOn.Select(id => $"'{id}'"));
            var checkCommand = new NpgsqlCommand(
                $@"SELECT COUNT(*) FROM {_tableName}
                   WHERE id = ANY(ARRAY[{dependsOnJsonArray}]::uuid[]) AND status != 'Completed'",
                connection);

            var incompleteCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

            return incompleteCount == 0;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to check dependencies. TaskId: {TaskId}", taskId);
            throw;
        }
    }

    private WorkTask ReadTask(NpgsqlDataReader reader)
    {
        var id = reader.GetGuid(reader.GetOrdinal("id"));
        var sessionId = reader.GetString(reader.GetOrdinal("session_id"));
        var workflowRunIdOrdinal = reader.GetOrdinal("workflow_run_id");
        var workflowRunId = reader.IsDBNull(workflowRunIdOrdinal) ? null : reader.GetString(workflowRunIdOrdinal);
        var content = reader.GetString(reader.GetOrdinal("content"));
        var status = Enum.Parse<TaskStatus>(reader.GetString(reader.GetOrdinal("status")));
        var priority = Enum.Parse<TaskPriority>(reader.GetString(reader.GetOrdinal("priority")));
        var order = reader.GetInt32(reader.GetOrdinal("order"));

        var dependsOnJson = reader.GetString(reader.GetOrdinal("depends_on"));
        var dependsOn = string.IsNullOrWhiteSpace(dependsOnJson)
            ? (IReadOnlyList<Guid>)new List<Guid>()
            : (IReadOnlyList<Guid>)(JsonSerializer.Deserialize<List<Guid>>(dependsOnJson) ?? new List<Guid>());

        var blockedByJson = reader.GetString(reader.GetOrdinal("blocked_by"));
        var blockedBy = string.IsNullOrWhiteSpace(blockedByJson)
            ? (IReadOnlyList<Guid>)new List<Guid>()
            : (IReadOnlyList<Guid>)(JsonSerializer.Deserialize<List<Guid>>(blockedByJson) ?? new List<Guid>());

        var parentTaskIdOrdinal = reader.GetOrdinal("parent_task_id");
        var parentTaskId = reader.IsDBNull(parentTaskIdOrdinal) ? (Guid?)null : reader.GetGuid(parentTaskIdOrdinal);

        var notesOrdinal = reader.GetOrdinal("notes");
        var notes = reader.IsDBNull(notesOrdinal) ? null : reader.GetString(notesOrdinal);

        var tagsJson = reader.GetString(reader.GetOrdinal("tags"));
        var tags = string.IsNullOrWhiteSpace(tagsJson)
            ? (IReadOnlyList<string>)new List<string>()
            : (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(tagsJson) ?? new List<string>());

        var metadataJson = reader.GetString(reader.GetOrdinal("metadata"));
        var metadata = string.IsNullOrWhiteSpace(metadataJson)
            ? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>()
            : (IReadOnlyDictionary<string, object>)(JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson) ?? new Dictionary<string, object>());

        var createdAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at"));
        var updatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at"));

        var startedAtOrdinal = reader.GetOrdinal("started_at");
        var startedAt = reader.IsDBNull(startedAtOrdinal) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(startedAtOrdinal);

        var completedAtOrdinal = reader.GetOrdinal("completed_at");
        var completedAt = reader.IsDBNull(completedAtOrdinal) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(completedAtOrdinal);

        var cancelledAtOrdinal = reader.GetOrdinal("cancelled_at");
        var cancelledAt = reader.IsDBNull(cancelledAtOrdinal) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(cancelledAtOrdinal);

        return new WorkTask
        {
            Id = id,
            SessionId = sessionId,
            WorkflowRunId = workflowRunId,
            Content = content,
            Status = status,
            Priority = priority,
            Order = order,
            DependsOn = dependsOn,
            BlockedBy = blockedBy,
            ParentTaskId = parentTaskId,
            Notes = notes,
            Tags = tags,
            Metadata = metadata,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            CancelledAt = cancelledAt
        };
    }

    private void AddTaskParameters(NpgsqlCommand command, WorkTask task)
    {
        command.Parameters.AddWithValue("@id", task.Id);
        command.Parameters.AddWithValue("@session_id", task.SessionId);
        command.Parameters.AddWithValue("@workflow_run_id", task.WorkflowRunId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@content", task.Content);
        command.Parameters.AddWithValue("@status", task.Status.ToString());
        command.Parameters.AddWithValue("@priority", task.Priority.ToString());
        command.Parameters.AddWithValue("@order", task.Order);
        command.Parameters.AddWithValue("@depends_on", JsonSerializer.Serialize(task.DependsOn));
        command.Parameters.AddWithValue("@blocked_by", JsonSerializer.Serialize(task.BlockedBy));
        command.Parameters.AddWithValue("@parent_task_id", task.ParentTaskId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@notes", task.Notes ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(task.Tags));
        command.Parameters.AddWithValue("@metadata", JsonSerializer.Serialize(task.Metadata));
        command.Parameters.AddWithValue("@created_at", task.CreatedAt);
        command.Parameters.AddWithValue("@updated_at", task.UpdatedAt);
        command.Parameters.AddWithValue("@started_at", task.StartedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@completed_at", task.CompletedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@cancelled_at", task.CancelledAt ?? (object)DBNull.Value);
    }

    private static DateTimeOffset? GetStartedAt(WorkTask existing, WorkTask updated, DateTimeOffset now)
    {
        if (updated.Status == TaskStatus.InProgress && existing.Status != TaskStatus.InProgress)
        {
            return updated.StartedAt ?? now;
        }
        return updated.StartedAt ?? existing.StartedAt;
    }

    private static DateTimeOffset? GetCompletedAt(WorkTask existing, WorkTask updated, DateTimeOffset now)
    {
        if (updated.Status == TaskStatus.Completed && existing.Status != TaskStatus.Completed)
        {
            return updated.CompletedAt ?? now;
        }
        return updated.CompletedAt ?? existing.CompletedAt;
    }

    private static DateTimeOffset? GetCancelledAt(WorkTask existing, WorkTask updated, DateTimeOffset now)
    {
        if (updated.Status == TaskStatus.Cancelled && existing.Status != TaskStatus.Cancelled)
        {
            return updated.CancelledAt ?? now;
        }
        return updated.CancelledAt ?? existing.CancelledAt;
    }

    private async Task EnsureTableExistsAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"CREATE TABLE IF NOT EXISTS {_tableName} (
                    id UUID PRIMARY KEY,
                    session_id VARCHAR(100) NOT NULL,
                    workflow_run_id VARCHAR(100),
                    content TEXT NOT NULL,
                    status VARCHAR(20) NOT NULL,
                    priority VARCHAR(20) NOT NULL,
                    ""order"" INTEGER NOT NULL DEFAULT 0,
                    depends_on TEXT NOT NULL DEFAULT '[]',
                    blocked_by TEXT NOT NULL DEFAULT '[]',
                    parent_task_id UUID,
                    notes TEXT,
                    tags TEXT NOT NULL DEFAULT '[]',
                    metadata TEXT NOT NULL DEFAULT '{{}}',
                    created_at TIMESTAMPTZ NOT NULL,
                    updated_at TIMESTAMPTZ NOT NULL,
                    started_at TIMESTAMPTZ,
                    completed_at TIMESTAMPTZ,
                    cancelled_at TIMESTAMPTZ
                );

                CREATE INDEX IF NOT EXISTS ix_{_tableName}_session_id ON {_tableName}(session_id);
                CREATE INDEX IF NOT EXISTS ix_{_tableName}_workflow_run_id ON {_tableName}(workflow_run_id);
                CREATE INDEX IF NOT EXISTS ix_{_tableName}_status ON {_tableName}(status);
                CREATE INDEX IF NOT EXISTS ix_{_tableName}_created_at ON {_tableName}(created_at);",
                connection);

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);

            _logger?.LogInformation("Ensured task table exists: {TableName}", _tableName);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to ensure task table exists");
            throw;
        }
    }
}
