using DotNetAgents.Workflow.Checkpoints;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;

namespace DotNetAgents.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="ICheckpointStore{TState}"/> for persistent checkpoint storage.
/// </summary>
/// <typeparam name="TState">The type of the workflow state.</typeparam>
public class SqlServerCheckpointStore<TState> : ICheckpointStore<TState> where TState : class
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly IStateSerializer<TState> _serializer;
    private readonly ILogger<SqlServerCheckpointStore<TState>>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerCheckpointStore{TState}"/> class.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="tableName">The table name for storing checkpoints. Default: "WorkflowCheckpoints".</param>
    /// <param name="serializer">The state serializer to use.</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public SqlServerCheckpointStore(
        string connectionString,
        string tableName = "WorkflowCheckpoints",
        IStateSerializer<TState>? serializer = null,
        ILogger<SqlServerCheckpointStore<TState>>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _serializer = serializer ?? new JsonStateSerializer<TState>();
        _logger = logger;

        // Ensure table exists
        EnsureTableExistsAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task<string> SaveAsync(
        Checkpoint<TState> checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpoint.RunId);

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"INSERT INTO [{_tableName}] (CheckpointId, RunId, State, NodeName, CreatedAt, StateVersion, ExpiresAt)
                   VALUES (@CheckpointId, @RunId, @State, @NodeName, @CreatedAt, @StateVersion, @ExpiresAt)",
                connection);

            command.Parameters.AddWithValue("@CheckpointId", checkpoint.Id);
            command.Parameters.AddWithValue("@RunId", checkpoint.RunId);
            command.Parameters.AddWithValue("@State", checkpoint.SerializedState);
            command.Parameters.AddWithValue("@NodeName", checkpoint.NodeName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@CreatedAt", checkpoint.CreatedAt);
            command.Parameters.AddWithValue("@StateVersion", checkpoint.StateVersion);

            // Calculate expiration (30 days from creation if not specified)
            var expiresAt = checkpoint.CreatedAt.AddDays(30);
            command.Parameters.AddWithValue("@ExpiresAt", expiresAt);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(
                "Checkpoint saved. RunId: {RunId}, CheckpointId: {CheckpointId}",
                checkpoint.RunId,
                checkpoint.Id);

            return checkpoint.Id;
        }
        catch (SqlException ex)
        {
            _logger?.LogError(ex, "Failed to save checkpoint. RunId: {RunId}", checkpoint.RunId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<Checkpoint<TState>?> GetAsync(
        string checkpointId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"SELECT CheckpointId, RunId, State, NodeName, CreatedAt, StateVersion
                   FROM [{_tableName}]
                   WHERE CheckpointId = @CheckpointId AND (ExpiresAt IS NULL OR ExpiresAt > @Now)",
                connection);

            command.Parameters.AddWithValue("@CheckpointId", checkpointId);
            command.Parameters.AddWithValue("@Now", DateTime.UtcNow);

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var checkpointIdValue = reader.GetString(reader.GetOrdinal("CheckpointId"));
            var runId = reader.GetString(reader.GetOrdinal("RunId"));
            var serializedState = reader.GetString(reader.GetOrdinal("State"));
            var nodeNameOrdinal = reader.GetOrdinal("NodeName");
            var nodeName = reader.IsDBNull(nodeNameOrdinal) ? string.Empty : reader.GetString(nodeNameOrdinal);
            var createdAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"));
            var stateVersion = reader.GetInt32(reader.GetOrdinal("StateVersion"));

            return new Checkpoint<TState>
            {
                Id = checkpointIdValue,
                RunId = runId,
                NodeName = nodeName,
                SerializedState = serializedState,
                CreatedAt = createdAt,
                StateVersion = stateVersion
            };
        }
        catch (SqlException ex)
        {
            _logger?.LogError(ex, "Failed to get checkpoint. CheckpointId: {CheckpointId}", checkpointId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<Checkpoint<TState>?> GetLatestAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"SELECT TOP 1 CheckpointId, RunId, State, NodeName, CreatedAt, StateVersion
                   FROM [{_tableName}]
                   WHERE RunId = @RunId AND (ExpiresAt IS NULL OR ExpiresAt > @Now)
                   ORDER BY CreatedAt DESC",
                connection);

            command.Parameters.AddWithValue("@RunId", runId);
            command.Parameters.AddWithValue("@Now", DateTime.UtcNow);

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var checkpointIdValue = reader.GetString(reader.GetOrdinal("CheckpointId"));
            var serializedState = reader.GetString(reader.GetOrdinal("State"));
            var nodeNameOrdinal = reader.GetOrdinal("NodeName");
            var nodeName = reader.IsDBNull(nodeNameOrdinal) ? string.Empty : reader.GetString(nodeNameOrdinal);
            var createdAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"));
            var stateVersion = reader.GetInt32(reader.GetOrdinal("StateVersion"));

            return new Checkpoint<TState>
            {
                Id = checkpointIdValue,
                RunId = runId,
                NodeName = nodeName,
                SerializedState = serializedState,
                CreatedAt = createdAt,
                StateVersion = stateVersion
            };
        }
        catch (SqlException ex)
        {
            _logger?.LogError(ex, "Failed to get latest checkpoint. RunId: {RunId}", runId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Checkpoint<TState>>> ListAsync(
        string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"SELECT CheckpointId, RunId, State, NodeName, CreatedAt, StateVersion
                   FROM [{_tableName}]
                   WHERE RunId = @RunId AND (ExpiresAt IS NULL OR ExpiresAt > @Now)
                   ORDER BY CreatedAt ASC",
                connection);

            command.Parameters.AddWithValue("@RunId", runId);
            command.Parameters.AddWithValue("@Now", DateTime.UtcNow);

            var checkpoints = new List<Checkpoint<TState>>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var checkpointIdValue = reader.GetString(reader.GetOrdinal("CheckpointId"));
                var serializedState = reader.GetString(reader.GetOrdinal("State"));
                var nodeNameOrdinal = reader.GetOrdinal("NodeName");
                var nodeName = reader.IsDBNull(nodeNameOrdinal) ? string.Empty : reader.GetString(nodeNameOrdinal);
                var createdAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt"));
                var stateVersion = reader.GetInt32(reader.GetOrdinal("StateVersion"));

                checkpoints.Add(new Checkpoint<TState>
                {
                    Id = checkpointIdValue,
                    RunId = runId,
                    NodeName = nodeName,
                    SerializedState = serializedState,
                    CreatedAt = createdAt,
                    StateVersion = stateVersion
                });
            }

            return checkpoints;
        }
        catch (SqlException ex)
        {
            _logger?.LogError(ex, "Failed to list checkpoints. RunId: {RunId}", runId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(
        string checkpointId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"DELETE FROM [{_tableName}] WHERE CheckpointId = @CheckpointId",
                connection);

            command.Parameters.AddWithValue("@CheckpointId", checkpointId);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Checkpoint deleted. CheckpointId: {CheckpointId}", checkpointId);
        }
        catch (SqlException ex)
        {
            _logger?.LogError(ex, "Failed to delete checkpoint. CheckpointId: {CheckpointId}", checkpointId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<int> DeleteOlderThanAsync(
        DateTime olderThan,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"DELETE FROM [{_tableName}] WHERE CreatedAt < @OlderThan",
                connection);

            command.Parameters.AddWithValue("@OlderThan", olderThan);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Deleted {Count} checkpoints older than {OlderThan}", rowsAffected, olderThan);

            return rowsAffected;
        }
        catch (SqlException ex)
        {
            _logger?.LogError(ex, "Failed to delete old checkpoints");
            throw;
        }
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
                           CheckpointId NVARCHAR(50) PRIMARY KEY,
                           RunId NVARCHAR(100) NOT NULL,
                           State NVARCHAR(MAX) NOT NULL,
                           NodeName NVARCHAR(200) NULL,
                           CreatedAt DATETIME2 NOT NULL,
                           StateVersion INT NOT NULL DEFAULT 1,
                           ExpiresAt DATETIME2 NULL,
                           INDEX IX_RunId (RunId),
                           INDEX IX_ExpiresAt (ExpiresAt),
                           INDEX IX_CreatedAt (CreatedAt)
                       )
                   END",
                connection);

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);

            _logger?.LogInformation("Ensured checkpoint table exists: {TableName}", _tableName);
        }
        catch (SqlException ex)
        {
            _logger?.LogError(ex, "Failed to ensure checkpoint table exists");
            throw;
        }
    }
}
