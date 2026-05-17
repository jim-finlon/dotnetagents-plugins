using DotNetAgents.Workflow.Session;
using DotNetAgents.Workflow.Session.Storage;
using Npgsql;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DotNetAgents.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of <see cref="IMilestoneStore"/> for persistent milestone storage.
/// </summary>
public class PostgreSQLMilestoneStore : IMilestoneStore
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly ILogger<PostgreSQLMilestoneStore>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLMilestoneStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The table name for storing milestones. Default: "milestones".</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public PostgreSQLMilestoneStore(
        string connectionString,
        string tableName = "milestones",
        ILogger<PostgreSQLMilestoneStore>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _logger = logger;

        EnsureTableExistsAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task<Milestone> CreateAsync(
        Milestone milestone,
        CancellationToken cancellationToken = default)
    {
        if (milestone == null)
            throw new ArgumentNullException(nameof(milestone));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;
            var milestoneToCreate = milestone with
            {
                Id = milestone.Id == default ? Guid.NewGuid() : milestone.Id,
                CreatedAt = milestone.CreatedAt == default ? now : milestone.CreatedAt,
                UpdatedAt = now
            };

            var command = new NpgsqlCommand(
                $@"INSERT INTO {_tableName} (
                    id, session_id, workflow_run_id, name, description, status,
                    required_task_ids, criteria, display_order, tags, metadata,
                    created_at, updated_at, completed_at, due_date, snapshot_id)
                VALUES (
                    @id, @session_id, @workflow_run_id, @name, @description, @status,
                    @required_task_ids, @criteria, @display_order, @tags, @metadata,
                    @created_at, @updated_at, @completed_at, @due_date, @snapshot_id)",
                connection);

            AddMilestoneParameters(command, milestoneToCreate);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(
                "Milestone created. MilestoneId: {MilestoneId}, SessionId: {SessionId}, Name: {Name}",
                milestoneToCreate.Id, milestoneToCreate.SessionId, milestoneToCreate.Name);

            return milestoneToCreate;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to create milestone. SessionId: {SessionId}", milestone.SessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<Milestone?> GetByIdAsync(
        Guid milestoneId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT id, session_id, workflow_run_id, name, description, status,
                          required_task_ids, criteria, display_order, tags, metadata,
                          created_at, updated_at, completed_at, due_date, snapshot_id
                   FROM {_tableName}
                   WHERE id = @id",
                connection);

            command.Parameters.AddWithValue("@id", milestoneId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return ReadMilestone(reader);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get milestone. MilestoneId: {MilestoneId}", milestoneId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<Milestone> UpdateAsync(
        Milestone milestone,
        CancellationToken cancellationToken = default)
    {
        if (milestone == null)
            throw new ArgumentNullException(nameof(milestone));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var updatedMilestone = milestone with
            {
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var command = new NpgsqlCommand(
                $@"UPDATE {_tableName} SET
                    session_id = @session_id,
                    workflow_run_id = @workflow_run_id,
                    name = @name,
                    description = @description,
                    status = @status,
                    required_task_ids = @required_task_ids,
                    criteria = @criteria,
                    display_order = @display_order,
                    tags = @tags,
                    metadata = @metadata,
                    updated_at = @updated_at,
                    completed_at = @completed_at,
                    due_date = @due_date,
                    snapshot_id = @snapshot_id
                WHERE id = @id",
                connection);

            AddMilestoneParameters(command, updatedMilestone);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (rowsAffected == 0)
            {
                throw new InvalidOperationException($"Milestone {milestone.Id} not found.");
            }

            _logger?.LogInformation("Milestone updated. MilestoneId: {MilestoneId}", updatedMilestone.Id);

            return updatedMilestone;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to update milestone. MilestoneId: {MilestoneId}", milestone.Id);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Milestone>> GetBySessionIdAsync(
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
                $@"SELECT id, session_id, workflow_run_id, name, description, status,
                          required_task_ids, criteria, display_order, tags, metadata,
                          created_at, updated_at, completed_at, due_date, snapshot_id
                   FROM {_tableName}
                   WHERE session_id = @session_id
                   ORDER BY display_order ASC",
                connection);

            command.Parameters.AddWithValue("@session_id", sessionId);

            var milestones = new List<Milestone>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                milestones.Add(ReadMilestone(reader));
            }

            return milestones;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get milestones by session. SessionId: {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid milestoneId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $"DELETE FROM {_tableName} WHERE id = @id",
                connection);

            command.Parameters.AddWithValue("@id", milestoneId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Milestone deleted. MilestoneId: {MilestoneId}", milestoneId);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to delete milestone. MilestoneId: {MilestoneId}", milestoneId);
            throw;
        }
    }

    private static Milestone ReadMilestone(NpgsqlDataReader reader)
    {
        var id = reader.GetGuid(reader.GetOrdinal("id"));
        var sessionId = reader.GetString(reader.GetOrdinal("session_id"));

        var workflowRunIdOrdinal = reader.GetOrdinal("workflow_run_id");
        var workflowRunId = reader.IsDBNull(workflowRunIdOrdinal) ? null : reader.GetString(workflowRunIdOrdinal);

        var name = reader.GetString(reader.GetOrdinal("name"));
        var description = reader.GetString(reader.GetOrdinal("description"));

        var statusString = reader.GetString(reader.GetOrdinal("status"));
        var status = Enum.TryParse<MilestoneStatus>(statusString, out var parsedStatus)
            ? parsedStatus
            : MilestoneStatus.Pending;

        var requiredTaskIdsJson = reader.GetString(reader.GetOrdinal("required_task_ids"));
        var requiredTaskIds = JsonSerializer.Deserialize<List<Guid>>(requiredTaskIdsJson) ?? new List<Guid>();

        var criteriaJson = reader.GetString(reader.GetOrdinal("criteria"));
        var criteria = JsonSerializer.Deserialize<Dictionary<string, string>>(criteriaJson) ?? new Dictionary<string, string>();

        var displayOrder = reader.GetInt32(reader.GetOrdinal("display_order"));

        var tagsJson = reader.GetString(reader.GetOrdinal("tags"));
        var tags = JsonSerializer.Deserialize<List<string>>(tagsJson) ?? new List<string>();

        var metadataJson = reader.GetString(reader.GetOrdinal("metadata"));
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? new Dictionary<string, string>();

        var createdAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at"));
        var updatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at"));

        var completedAtOrdinal = reader.GetOrdinal("completed_at");
        var completedAt = reader.IsDBNull(completedAtOrdinal) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(completedAtOrdinal);

        var dueDateOrdinal = reader.GetOrdinal("due_date");
        var dueDate = reader.IsDBNull(dueDateOrdinal) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(dueDateOrdinal);

        var snapshotIdOrdinal = reader.GetOrdinal("snapshot_id");
        var snapshotId = reader.IsDBNull(snapshotIdOrdinal) ? (Guid?)null : reader.GetGuid(snapshotIdOrdinal);

        return new Milestone
        {
            Id = id,
            SessionId = sessionId,
            WorkflowRunId = workflowRunId,
            Name = name,
            Description = description,
            Status = status,
            RequiredTaskIds = requiredTaskIds,
            Criteria = criteria,
            Order = displayOrder,
            Tags = tags,
            Metadata = metadata,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            CompletedAt = completedAt,
            DueDate = dueDate,
            SnapshotId = snapshotId
        };
    }

    private static void AddMilestoneParameters(NpgsqlCommand command, Milestone milestone)
    {
        command.Parameters.AddWithValue("@id", milestone.Id);
        command.Parameters.AddWithValue("@session_id", milestone.SessionId);
        command.Parameters.AddWithValue("@workflow_run_id", milestone.WorkflowRunId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@name", milestone.Name);
        command.Parameters.AddWithValue("@description", milestone.Description);
        command.Parameters.AddWithValue("@status", milestone.Status.ToString());
        command.Parameters.AddWithValue("@required_task_ids", JsonSerializer.Serialize(milestone.RequiredTaskIds));
        command.Parameters.AddWithValue("@criteria", JsonSerializer.Serialize(milestone.Criteria));
        command.Parameters.AddWithValue("@display_order", milestone.Order);
        command.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(milestone.Tags));
        command.Parameters.AddWithValue("@metadata", JsonSerializer.Serialize(milestone.Metadata));
        command.Parameters.AddWithValue("@created_at", milestone.CreatedAt);
        command.Parameters.AddWithValue("@updated_at", milestone.UpdatedAt);
        command.Parameters.AddWithValue("@completed_at", milestone.CompletedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@due_date", milestone.DueDate ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@snapshot_id", milestone.SnapshotId ?? (object)DBNull.Value);
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
                    name VARCHAR(500) NOT NULL,
                    description TEXT NOT NULL DEFAULT '',
                    status VARCHAR(50) NOT NULL DEFAULT 'Pending',
                    required_task_ids JSONB NOT NULL DEFAULT '[]'::JSONB,
                    criteria JSONB NOT NULL DEFAULT '{{}}'::JSONB,
                    display_order INTEGER NOT NULL DEFAULT 0,
                    tags JSONB NOT NULL DEFAULT '[]'::JSONB,
                    metadata JSONB NOT NULL DEFAULT '{{}}'::JSONB,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    completed_at TIMESTAMPTZ NULL,
                    due_date TIMESTAMPTZ NULL,
                    snapshot_id UUID NULL
                )",
                connection);

            await createTableCommand.ExecuteNonQueryAsync().ConfigureAwait(false);

            var indexCommands = new[]
            {
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_session_id ON {_tableName} (session_id)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_status ON {_tableName} (status)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_session_order ON {_tableName} (session_id, display_order)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_created_at ON {_tableName} (created_at DESC)"
            };

            foreach (var indexSql in indexCommands)
            {
                var indexCommand = new NpgsqlCommand(indexSql, connection);
                await indexCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            _logger?.LogInformation("Ensured milestone table exists: {TableName}", _tableName);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to ensure milestone table exists");
            throw;
        }
    }
}
