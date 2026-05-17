using DotNetAgents.Tasks.Models;
using DotNetAgents.Tasks.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;
using TaskStatus = DotNetAgents.Tasks.Models.TaskStatus;

namespace DotNetAgents.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="ITaskStore"/> for persistent task storage.
/// </summary>
public class SqlServerTaskStore : ITaskStore
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly ILogger<SqlServerTaskStore>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerTaskStore"/> class.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="tableName">The table name for storing tasks. Default: "WorkTasks".</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public SqlServerTaskStore(
        string connectionString,
        string tableName = "WorkTasks",
        ILogger<SqlServerTaskStore>? logger = null)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"SELECT Id, SessionId, WorkflowRunId, Content, Status, Priority, [Order],
                          DependsOn, BlockedBy, ParentTaskId, Notes, Tags, Metadata,
                          CreatedAt, UpdatedAt, StartedAt, CompletedAt, CancelledAt
                   FROM [{_tableName}]
                   WHERE Id = @Id",
                connection);

            command.Parameters.AddWithValue("@Id", taskId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return ReadTask(reader);
        }
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Auto-assign order if not set
            var order = task.Order;
            if (order == 0)
            {
                var maxOrderCommand = new SqlCommand(
                    $@"SELECT ISNULL(MAX([Order]), -1) FROM [{_tableName}] WHERE SessionId = @SessionId",
                    connection);
                maxOrderCommand.Parameters.AddWithValue("@SessionId", task.SessionId);
                var maxOrder = await maxOrderCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                order = Convert.ToInt32(maxOrder) + 1;
            }

            var command = new SqlCommand(
                $@"INSERT INTO [{_tableName}]
                   (Id, SessionId, WorkflowRunId, Content, Status, Priority, [Order],
                    DependsOn, BlockedBy, ParentTaskId, Notes, Tags, Metadata,
                    CreatedAt, UpdatedAt, StartedAt, CompletedAt, CancelledAt)
                   VALUES
                   (@Id, @SessionId, @WorkflowRunId, @Content, @Status, @Priority, @Order,
                    @DependsOn, @BlockedBy, @ParentTaskId, @Notes, @Tags, @Metadata,
                    @CreatedAt, @UpdatedAt, @StartedAt, @CompletedAt, @CancelledAt)",
                connection);

            AddTaskParameters(command, task with { Order = order });

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Task created. TaskId: {TaskId}, SessionId: {SessionId}", task.Id, task.SessionId);

            return task with { Order = order };
        }
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
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

            var command = new SqlCommand(
                $@"UPDATE [{_tableName}] SET
                   SessionId = @SessionId,
                   WorkflowRunId = @WorkflowRunId,
                   Content = @Content,
                   Status = @Status,
                   Priority = @Priority,
                   [Order] = @Order,
                   DependsOn = @DependsOn,
                   BlockedBy = @BlockedBy,
                   ParentTaskId = @ParentTaskId,
                   Notes = @Notes,
                   Tags = @Tags,
                   Metadata = @Metadata,
                   UpdatedAt = @UpdatedAt,
                   StartedAt = @StartedAt,
                   CompletedAt = @CompletedAt,
                   CancelledAt = @CancelledAt
                   WHERE Id = @Id",
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
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"DELETE FROM [{_tableName}] WHERE Id = @Id",
                connection);

            command.Parameters.AddWithValue("@Id", taskId);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Task deleted. TaskId: {TaskId}", taskId);
        }
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"SELECT Id, SessionId, WorkflowRunId, Content, Status, Priority, [Order],
                          DependsOn, BlockedBy, ParentTaskId, Notes, Tags, Metadata,
                          CreatedAt, UpdatedAt, StartedAt, CompletedAt, CancelledAt
                   FROM [{_tableName}]
                   WHERE SessionId = @SessionId
                   ORDER BY [Order]",
                connection);

            command.Parameters.AddWithValue("@SessionId", sessionId);

            var tasks = new List<WorkTask>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(ReadTask(reader));
            }

            return tasks;
        }
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"SELECT Id, SessionId, WorkflowRunId, Content, Status, Priority, [Order],
                          DependsOn, BlockedBy, ParentTaskId, Notes, Tags, Metadata,
                          CreatedAt, UpdatedAt, StartedAt, CompletedAt, CancelledAt
                   FROM [{_tableName}]
                   WHERE SessionId = @SessionId AND Status = @Status
                   ORDER BY [Order]",
                connection);

            command.Parameters.AddWithValue("@SessionId", sessionId);
            command.Parameters.AddWithValue("@Status", status.ToString());

            var tasks = new List<WorkTask>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(ReadTask(reader));
            }

            return tasks;
        }
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"SELECT Id, SessionId, WorkflowRunId, Content, Status, Priority, [Order],
                          DependsOn, BlockedBy, ParentTaskId, Notes, Tags, Metadata,
                          CreatedAt, UpdatedAt, StartedAt, CompletedAt, CancelledAt
                   FROM [{_tableName}]
                   WHERE WorkflowRunId = @WorkflowRunId
                   ORDER BY [Order]",
                connection);

            command.Parameters.AddWithValue("@WorkflowRunId", workflowRunId);

            var tasks = new List<WorkTask>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tasks.Add(ReadTask(reader));
            }

            return tasks;
        }
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"SELECT
                   COUNT(*) AS Total,
                   SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END) AS Pending,
                   SUM(CASE WHEN Status = 'InProgress' THEN 1 ELSE 0 END) AS InProgress,
                   SUM(CASE WHEN Status = 'Completed' THEN 1 ELSE 0 END) AS Completed,
                   SUM(CASE WHEN Status = 'Blocked' THEN 1 ELSE 0 END) AS Blocked,
                   SUM(CASE WHEN Status = 'Cancelled' THEN 1 ELSE 0 END) AS Cancelled,
                   SUM(CASE WHEN Status = 'Review' THEN 1 ELSE 0 END) AS Review
                   FROM [{_tableName}]
                   WHERE SessionId = @SessionId",
                connection);

            command.Parameters.AddWithValue("@SessionId", sessionId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new TaskStatistics();
            }

            var total = reader.GetInt32(reader.GetOrdinal("Total"));
            var pending = reader.GetInt32(reader.GetOrdinal("Pending"));
            var inProgress = reader.GetInt32(reader.GetOrdinal("InProgress"));
            var completed = reader.GetInt32(reader.GetOrdinal("Completed"));
            var blocked = reader.GetInt32(reader.GetOrdinal("Blocked"));
            var cancelled = reader.GetInt32(reader.GetOrdinal("Cancelled"));
            var review = reader.GetInt32(reader.GetOrdinal("Review"));

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
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var transaction = connection.BeginTransaction();
            try
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var (taskId, newOrder) in taskOrders)
                {
                    var command = new SqlCommand(
                        $@"UPDATE [{_tableName}]
                           SET [Order] = @Order, UpdatedAt = @UpdatedAt
                           WHERE Id = @Id AND SessionId = @SessionId",
                        connection,
                        transaction);

                    command.Parameters.AddWithValue("@Id", taskId);
                    command.Parameters.AddWithValue("@SessionId", sessionId);
                    command.Parameters.AddWithValue("@Order", newOrder);
                    command.Parameters.AddWithValue("@UpdatedAt", now);

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
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Get task dependencies
            var taskCommand = new SqlCommand(
                $@"SELECT DependsOn FROM [{_tableName}] WHERE Id = @Id",
                connection);
            taskCommand.Parameters.AddWithValue("@Id", taskId);

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
            var checkCommand = new SqlCommand(
                $@"SELECT COUNT(*) FROM [{_tableName}]
                   WHERE Id IN ({dependsOnJsonArray}) AND Status != 'Completed'",
                connection);

            var incompleteCount = Convert.ToInt32(await checkCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

            return incompleteCount == 0;
        }
        catch (SqlException ex)
        {
            _logger?.LogError(ex, "Failed to check dependencies. TaskId: {TaskId}", taskId);
            throw;
        }
    }

    private WorkTask ReadTask(SqlDataReader reader)
    {
        var id = reader.GetGuid(reader.GetOrdinal("Id"));
        var sessionId = reader.GetString(reader.GetOrdinal("SessionId"));
        var workflowRunIdOrdinal = reader.GetOrdinal("WorkflowRunId");
        var workflowRunId = reader.IsDBNull(workflowRunIdOrdinal) ? null : reader.GetString(workflowRunIdOrdinal);
        var content = reader.GetString(reader.GetOrdinal("Content"));
        var status = Enum.Parse<TaskStatus>(reader.GetString(reader.GetOrdinal("Status")));
        var priority = Enum.Parse<TaskPriority>(reader.GetString(reader.GetOrdinal("Priority")));
        var order = reader.GetInt32(reader.GetOrdinal("Order"));

        var dependsOnJson = reader.GetString(reader.GetOrdinal("DependsOn"));
        var dependsOn = string.IsNullOrWhiteSpace(dependsOnJson)
            ? (IReadOnlyList<Guid>)new List<Guid>()
            : (IReadOnlyList<Guid>)(JsonSerializer.Deserialize<List<Guid>>(dependsOnJson) ?? new List<Guid>());

        var blockedByJson = reader.GetString(reader.GetOrdinal("BlockedBy"));
        var blockedBy = string.IsNullOrWhiteSpace(blockedByJson)
            ? (IReadOnlyList<Guid>)new List<Guid>()
            : (IReadOnlyList<Guid>)(JsonSerializer.Deserialize<List<Guid>>(blockedByJson) ?? new List<Guid>());

        var parentTaskIdOrdinal = reader.GetOrdinal("ParentTaskId");
        var parentTaskId = reader.IsDBNull(parentTaskIdOrdinal) ? (Guid?)null : reader.GetGuid(parentTaskIdOrdinal);

        var notesOrdinal = reader.GetOrdinal("Notes");
        var notes = reader.IsDBNull(notesOrdinal) ? null : reader.GetString(notesOrdinal);

        var tagsJson = reader.GetString(reader.GetOrdinal("Tags"));
        var tags = string.IsNullOrWhiteSpace(tagsJson)
            ? (IReadOnlyList<string>)new List<string>()
            : (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(tagsJson) ?? new List<string>());

        var metadataJson = reader.GetString(reader.GetOrdinal("Metadata"));
        var metadata = string.IsNullOrWhiteSpace(metadataJson)
            ? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>()
            : (IReadOnlyDictionary<string, object>)(JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson) ?? new Dictionary<string, object>());

        var createdAt = reader.GetDateTimeOffset(reader.GetOrdinal("CreatedAt"));
        var updatedAt = reader.GetDateTimeOffset(reader.GetOrdinal("UpdatedAt"));

        var startedAtOrdinal = reader.GetOrdinal("StartedAt");
        var startedAt = reader.IsDBNull(startedAtOrdinal) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(startedAtOrdinal);

        var completedAtOrdinal = reader.GetOrdinal("CompletedAt");
        var completedAt = reader.IsDBNull(completedAtOrdinal) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(completedAtOrdinal);

        var cancelledAtOrdinal = reader.GetOrdinal("CancelledAt");
        var cancelledAt = reader.IsDBNull(cancelledAtOrdinal) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(cancelledAtOrdinal);

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

    private void AddTaskParameters(SqlCommand command, WorkTask task)
    {
        command.Parameters.AddWithValue("@Id", task.Id);
        command.Parameters.AddWithValue("@SessionId", task.SessionId);
        command.Parameters.AddWithValue("@WorkflowRunId", task.WorkflowRunId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Content", task.Content);
        command.Parameters.AddWithValue("@Status", task.Status.ToString());
        command.Parameters.AddWithValue("@Priority", task.Priority.ToString());
        command.Parameters.AddWithValue("@Order", task.Order);
        command.Parameters.AddWithValue("@DependsOn", JsonSerializer.Serialize(task.DependsOn));
        command.Parameters.AddWithValue("@BlockedBy", JsonSerializer.Serialize(task.BlockedBy));
        command.Parameters.AddWithValue("@ParentTaskId", task.ParentTaskId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Notes", task.Notes ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Tags", JsonSerializer.Serialize(task.Tags));
        command.Parameters.AddWithValue("@Metadata", JsonSerializer.Serialize(task.Metadata));
        command.Parameters.AddWithValue("@CreatedAt", task.CreatedAt);
        command.Parameters.AddWithValue("@UpdatedAt", task.UpdatedAt);
        command.Parameters.AddWithValue("@StartedAt", task.StartedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@CompletedAt", task.CompletedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@CancelledAt", task.CancelledAt ?? (object)DBNull.Value);
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = new SqlCommand(
                $@"IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[{_tableName}]') AND type in (N'U'))
                   BEGIN
                       CREATE TABLE [{_tableName}] (
                           Id UNIQUEIDENTIFIER PRIMARY KEY,
                           SessionId NVARCHAR(100) NOT NULL,
                           WorkflowRunId NVARCHAR(100) NULL,
                           Content NVARCHAR(MAX) NOT NULL,
                           Status NVARCHAR(20) NOT NULL,
                           Priority NVARCHAR(20) NOT NULL,
                           [Order] INT NOT NULL DEFAULT 0,
                           DependsOn NVARCHAR(MAX) NOT NULL DEFAULT '[]',
                           BlockedBy NVARCHAR(MAX) NOT NULL DEFAULT '[]',
                           ParentTaskId UNIQUEIDENTIFIER NULL,
                           Notes NVARCHAR(MAX) NULL,
                           Tags NVARCHAR(MAX) NOT NULL DEFAULT '[]',
                           Metadata NVARCHAR(MAX) NOT NULL DEFAULT '{{}}',
                           CreatedAt DATETIMEOFFSET NOT NULL,
                           UpdatedAt DATETIMEOFFSET NOT NULL,
                           StartedAt DATETIMEOFFSET NULL,
                           CompletedAt DATETIMEOFFSET NULL,
                           CancelledAt DATETIMEOFFSET NULL,
                           INDEX IX_SessionId (SessionId),
                           INDEX IX_WorkflowRunId (WorkflowRunId),
                           INDEX IX_Status (Status),
                           INDEX IX_CreatedAt (CreatedAt)
                       )
                   END",
                connection);

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);

            _logger?.LogInformation("Ensured task table exists: {TableName}", _tableName);
        }
        catch (SqlException ex)
        {
            _logger?.LogError(ex, "Failed to ensure task table exists");
            throw;
        }
    }
}
