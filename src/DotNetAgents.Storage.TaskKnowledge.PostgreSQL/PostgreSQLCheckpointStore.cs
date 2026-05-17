using DotNetAgents.Workflow.Checkpoints;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DotNetAgents.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of <see cref="ICheckpointStore{TState}"/> for persistent checkpoint storage.
/// </summary>
/// <typeparam name="TState">The type of the workflow state.</typeparam>
public class PostgreSQLCheckpointStore<TState> : ICheckpointStore<TState> where TState : class
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly IStateSerializer<TState> _serializer;
    private readonly ILogger<PostgreSQLCheckpointStore<TState>>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLCheckpointStore{TState}"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The table name for storing checkpoints. Default: "workflow_checkpoints".</param>
    /// <param name="serializer">The state serializer to use.</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public PostgreSQLCheckpointStore(
        string connectionString,
        string tableName = "workflow_checkpoints",
        IStateSerializer<TState>? serializer = null,
        ILogger<PostgreSQLCheckpointStore<TState>>? logger = null)
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
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"INSERT INTO {_tableName} (checkpoint_id, run_id, state, node_name, created_at, state_version, expires_at)
                   VALUES (@checkpoint_id, @run_id, @state, @node_name, @created_at, @state_version, @expires_at)
                   ON CONFLICT (checkpoint_id) DO UPDATE SET
                       state = EXCLUDED.state,
                       node_name = EXCLUDED.node_name,
                       created_at = EXCLUDED.created_at,
                       state_version = EXCLUDED.state_version,
                       expires_at = EXCLUDED.expires_at",
                connection);

            command.Parameters.AddWithValue("@checkpoint_id", checkpoint.Id);
            command.Parameters.AddWithValue("@run_id", checkpoint.RunId);
            command.Parameters.AddWithValue("@state", checkpoint.SerializedState);
            command.Parameters.AddWithValue("@node_name", checkpoint.NodeName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@created_at", checkpoint.CreatedAt);
            command.Parameters.AddWithValue("@state_version", checkpoint.StateVersion);

            // Calculate expiration (30 days from creation if not specified)
            var expiresAt = checkpoint.CreatedAt.AddDays(30);
            command.Parameters.AddWithValue("@expires_at", expiresAt);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(
                "Checkpoint saved. RunId: {RunId}, CheckpointId: {CheckpointId}",
                checkpoint.RunId,
                checkpoint.Id);

            return checkpoint.Id;
        }
        catch (PostgresException ex)
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
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT checkpoint_id, run_id, state, node_name, created_at, state_version
                   FROM {_tableName}
                   WHERE checkpoint_id = @checkpoint_id AND (expires_at IS NULL OR expires_at > @now)",
                connection);

            command.Parameters.AddWithValue("@checkpoint_id", checkpointId);
            command.Parameters.AddWithValue("@now", DateTime.UtcNow);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var checkpointIdValue = reader.GetString(reader.GetOrdinal("checkpoint_id"));
            var runId = reader.GetString(reader.GetOrdinal("run_id"));
            var serializedState = reader.GetString(reader.GetOrdinal("state"));
            var nodeNameOrdinal = reader.GetOrdinal("node_name");
            var nodeName = reader.IsDBNull(nodeNameOrdinal) ? string.Empty : reader.GetString(nodeNameOrdinal);
            var createdAt = reader.GetDateTime(reader.GetOrdinal("created_at"));
            var stateVersion = reader.GetInt32(reader.GetOrdinal("state_version"));

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
        catch (PostgresException ex)
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
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT checkpoint_id, run_id, state, node_name, created_at, state_version
                   FROM {_tableName}
                   WHERE run_id = @run_id AND (expires_at IS NULL OR expires_at > @now)
                   ORDER BY created_at DESC
                   LIMIT 1",
                connection);

            command.Parameters.AddWithValue("@run_id", runId);
            command.Parameters.AddWithValue("@now", DateTime.UtcNow);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var checkpointIdValue = reader.GetString(reader.GetOrdinal("checkpoint_id"));
            var serializedState = reader.GetString(reader.GetOrdinal("state"));
            var nodeNameOrdinal = reader.GetOrdinal("node_name");
            var nodeName = reader.IsDBNull(nodeNameOrdinal) ? string.Empty : reader.GetString(nodeNameOrdinal);
            var createdAt = reader.GetDateTime(reader.GetOrdinal("created_at"));
            var stateVersion = reader.GetInt32(reader.GetOrdinal("state_version"));

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
        catch (PostgresException ex)
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
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT checkpoint_id, run_id, state, node_name, created_at, state_version
                   FROM {_tableName}
                   WHERE run_id = @run_id AND (expires_at IS NULL OR expires_at > @now)
                   ORDER BY created_at ASC",
                connection);

            command.Parameters.AddWithValue("@run_id", runId);
            command.Parameters.AddWithValue("@now", DateTime.UtcNow);

            var checkpoints = new List<Checkpoint<TState>>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var checkpointIdValue = reader.GetString(reader.GetOrdinal("checkpoint_id"));
                var serializedState = reader.GetString(reader.GetOrdinal("state"));
                var nodeNameOrdinal = reader.GetOrdinal("node_name");
                var nodeName = reader.IsDBNull(nodeNameOrdinal) ? string.Empty : reader.GetString(nodeNameOrdinal);
                var createdAt = reader.GetDateTime(reader.GetOrdinal("created_at"));
                var stateVersion = reader.GetInt32(reader.GetOrdinal("state_version"));

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
        catch (PostgresException ex)
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
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"DELETE FROM {_tableName} WHERE checkpoint_id = @checkpoint_id",
                connection);

            command.Parameters.AddWithValue("@checkpoint_id", checkpointId);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Checkpoint deleted. CheckpointId: {CheckpointId}", checkpointId);
        }
        catch (PostgresException ex)
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
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"DELETE FROM {_tableName} WHERE created_at < @older_than",
                connection);

            command.Parameters.AddWithValue("@older_than", olderThan);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Deleted {Count} checkpoints older than {OlderThan}", rowsAffected, olderThan);

            return rowsAffected;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to delete old checkpoints");
            throw;
        }
    }

    private async Task EnsureTableExistsAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"CREATE TABLE IF NOT EXISTS {_tableName} (
                    checkpoint_id VARCHAR(50) PRIMARY KEY,
                    run_id VARCHAR(100) NOT NULL,
                    state TEXT NOT NULL,
                    node_name VARCHAR(200),
                    created_at TIMESTAMP NOT NULL,
                    state_version INTEGER NOT NULL DEFAULT 1,
                    expires_at TIMESTAMP,
                    CONSTRAINT fk_checkpoint_expires CHECK (expires_at IS NULL OR expires_at > created_at)
                );

                CREATE INDEX IF NOT EXISTS ix_{_tableName}_run_id ON {_tableName}(run_id);
                CREATE INDEX IF NOT EXISTS ix_{_tableName}_expires_at ON {_tableName}(expires_at);
                CREATE INDEX IF NOT EXISTS ix_{_tableName}_created_at ON {_tableName}(created_at);",
                connection);

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);

            _logger?.LogInformation("Ensured checkpoint table exists: {TableName}", _tableName);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to ensure checkpoint table exists");
            throw;
        }
    }
}
