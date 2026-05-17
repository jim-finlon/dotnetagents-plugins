using DotNetAgents.Workflow.Session;
using DotNetAgents.Workflow.Session.Storage;
using Npgsql;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DotNetAgents.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of <see cref="ISnapshotStore"/> for persistent workflow snapshot storage.
/// </summary>
public class PostgreSQLSnapshotStore : ISnapshotStore
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly ILogger<PostgreSQLSnapshotStore>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLSnapshotStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The table name for storing snapshots. Default: "workflow_snapshots".</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public PostgreSQLSnapshotStore(
        string connectionString,
        string tableName = "workflow_snapshots",
        ILogger<PostgreSQLSnapshotStore>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _logger = logger;

        EnsureTableExistsAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task<WorkflowSnapshot> CreateAsync(
        WorkflowSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var snapshotToCreate = snapshot with
            {
                Id = snapshot.Id == default ? Guid.NewGuid() : snapshot.Id,
                CreatedAt = snapshot.CreatedAt == default ? DateTimeOffset.UtcNow : snapshot.CreatedAt
            };

            var command = new NpgsqlCommand(
                $@"INSERT INTO {_tableName} (
                    id, session_id, workflow_run_id, snapshot_number, serialized_state,
                    resume_point, task_summary, tasks, knowledge_count, trigger,
                    trigger_details, size_in_bytes, metadata, created_at)
                VALUES (
                    @id, @session_id, @workflow_run_id, @snapshot_number, @serialized_state,
                    @resume_point, @task_summary, @tasks, @knowledge_count, @trigger,
                    @trigger_details, @size_in_bytes, @metadata, @created_at)",
                connection);

            AddSnapshotParameters(command, snapshotToCreate);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(
                "Snapshot created. SnapshotId: {SnapshotId}, SessionId: {SessionId}, Number: {Number}",
                snapshotToCreate.Id, snapshotToCreate.SessionId, snapshotToCreate.SnapshotNumber);

            return snapshotToCreate;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to create snapshot. SessionId: {SessionId}", snapshot.SessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<WorkflowSnapshot?> GetByIdAsync(
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT id, session_id, workflow_run_id, snapshot_number, serialized_state,
                          resume_point, task_summary, tasks, knowledge_count, trigger,
                          trigger_details, size_in_bytes, metadata, created_at
                   FROM {_tableName}
                   WHERE id = @id",
                connection);

            command.Parameters.AddWithValue("@id", snapshotId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return ReadSnapshot(reader);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get snapshot. SnapshotId: {SnapshotId}", snapshotId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<WorkflowSnapshot>> GetBySessionIdAsync(
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
                $@"SELECT id, session_id, workflow_run_id, snapshot_number, serialized_state,
                          resume_point, task_summary, tasks, knowledge_count, trigger,
                          trigger_details, size_in_bytes, metadata, created_at
                   FROM {_tableName}
                   WHERE session_id = @session_id
                   ORDER BY snapshot_number ASC",
                connection);

            command.Parameters.AddWithValue("@session_id", sessionId);

            var snapshots = new List<WorkflowSnapshot>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                snapshots.Add(ReadSnapshot(reader));
            }

            return snapshots;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get snapshots by session. SessionId: {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<WorkflowSnapshot?> GetLatestAsync(
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
                $@"SELECT id, session_id, workflow_run_id, snapshot_number, serialized_state,
                          resume_point, task_summary, tasks, knowledge_count, trigger,
                          trigger_details, size_in_bytes, metadata, created_at
                   FROM {_tableName}
                   WHERE session_id = @session_id
                   ORDER BY snapshot_number DESC
                   LIMIT 1",
                connection);

            command.Parameters.AddWithValue("@session_id", sessionId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return ReadSnapshot(reader);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get latest snapshot. SessionId: {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $"DELETE FROM {_tableName} WHERE id = @id",
                connection);

            command.Parameters.AddWithValue("@id", snapshotId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Snapshot deleted. SnapshotId: {SnapshotId}", snapshotId);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to delete snapshot. SnapshotId: {SnapshotId}", snapshotId);
            throw;
        }
    }

    private static WorkflowSnapshot ReadSnapshot(NpgsqlDataReader reader)
    {
        var id = reader.GetGuid(reader.GetOrdinal("id"));
        var sessionId = reader.GetString(reader.GetOrdinal("session_id"));

        var workflowRunIdOrdinal = reader.GetOrdinal("workflow_run_id");
        var workflowRunId = reader.IsDBNull(workflowRunIdOrdinal) ? null : reader.GetString(workflowRunIdOrdinal);

        var snapshotNumber = reader.GetInt32(reader.GetOrdinal("snapshot_number"));
        var serializedState = reader.GetString(reader.GetOrdinal("serialized_state"));
        var resumePoint = reader.GetString(reader.GetOrdinal("resume_point"));

        var taskSummaryJson = reader.GetString(reader.GetOrdinal("task_summary"));
        var taskSummary = JsonSerializer.Deserialize<SnapshotTaskSummary>(taskSummaryJson) ?? new SnapshotTaskSummary();

        var tasksJson = reader.GetString(reader.GetOrdinal("tasks"));
        var tasks = JsonSerializer.Deserialize<List<TaskSnapshot>>(tasksJson) ?? new List<TaskSnapshot>();

        var knowledgeCount = reader.GetInt32(reader.GetOrdinal("knowledge_count"));

        var triggerString = reader.GetString(reader.GetOrdinal("trigger"));
        var trigger = Enum.TryParse<SnapshotTrigger>(triggerString, out var parsedTrigger)
            ? parsedTrigger
            : SnapshotTrigger.Manual;

        var triggerDetailsOrdinal = reader.GetOrdinal("trigger_details");
        var triggerDetails = reader.IsDBNull(triggerDetailsOrdinal) ? null : reader.GetString(triggerDetailsOrdinal);

        var sizeInBytes = reader.GetInt64(reader.GetOrdinal("size_in_bytes"));

        var metadataJson = reader.GetString(reader.GetOrdinal("metadata"));
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? new Dictionary<string, string>();

        var createdAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at"));

        return new WorkflowSnapshot
        {
            Id = id,
            SessionId = sessionId,
            WorkflowRunId = workflowRunId,
            SnapshotNumber = snapshotNumber,
            SerializedState = serializedState,
            ResumePoint = resumePoint,
            TaskSummary = taskSummary,
            Tasks = tasks,
            KnowledgeCount = knowledgeCount,
            Trigger = trigger,
            TriggerDetails = triggerDetails,
            SizeInBytes = sizeInBytes,
            Metadata = metadata,
            CreatedAt = createdAt
        };
    }

    private static void AddSnapshotParameters(NpgsqlCommand command, WorkflowSnapshot snapshot)
    {
        command.Parameters.AddWithValue("@id", snapshot.Id);
        command.Parameters.AddWithValue("@session_id", snapshot.SessionId);
        command.Parameters.AddWithValue("@workflow_run_id", snapshot.WorkflowRunId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@snapshot_number", snapshot.SnapshotNumber);
        command.Parameters.AddWithValue("@serialized_state", snapshot.SerializedState);
        command.Parameters.AddWithValue("@resume_point", snapshot.ResumePoint);
        command.Parameters.AddWithValue("@task_summary", JsonSerializer.Serialize(snapshot.TaskSummary));
        command.Parameters.AddWithValue("@tasks", JsonSerializer.Serialize(snapshot.Tasks));
        command.Parameters.AddWithValue("@knowledge_count", snapshot.KnowledgeCount);
        command.Parameters.AddWithValue("@trigger", snapshot.Trigger.ToString());
        command.Parameters.AddWithValue("@trigger_details", snapshot.TriggerDetails ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@size_in_bytes", snapshot.SizeInBytes);
        command.Parameters.AddWithValue("@metadata", JsonSerializer.Serialize(snapshot.Metadata));
        command.Parameters.AddWithValue("@created_at", snapshot.CreatedAt);
    }

    private async Task EnsureTableExistsAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var createTableCommand = new NpgsqlCommand(
                $@"CREATE TABLE IF NOT EXISTS {_tableName} (
                    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    session_id VARCHAR(100) NOT NULL,
                    workflow_run_id VARCHAR(100) NULL,
                    snapshot_number INTEGER NOT NULL,
                    serialized_state TEXT NOT NULL DEFAULT '',
                    resume_point TEXT NOT NULL DEFAULT '',
                    task_summary JSONB NOT NULL DEFAULT '{{}}'::JSONB,
                    tasks JSONB NOT NULL DEFAULT '[]'::JSONB,
                    knowledge_count INTEGER NOT NULL DEFAULT 0,
                    trigger VARCHAR(50) NOT NULL DEFAULT 'Manual',
                    trigger_details TEXT NULL,
                    size_in_bytes BIGINT NOT NULL DEFAULT 0,
                    metadata JSONB NOT NULL DEFAULT '{{}}'::JSONB,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                )",
                connection);

            await createTableCommand.ExecuteNonQueryAsync().ConfigureAwait(false);

            var indexCommands = new[]
            {
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_session_id ON {_tableName} (session_id)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_session_number ON {_tableName} (session_id, snapshot_number DESC)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_created_at ON {_tableName} (created_at DESC)"
            };

            foreach (var indexSql in indexCommands)
            {
                var indexCommand = new NpgsqlCommand(indexSql, connection);
                await indexCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            _logger?.LogInformation("Ensured snapshot table exists: {TableName}", _tableName);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to ensure snapshot table exists");
            throw;
        }
    }
}
