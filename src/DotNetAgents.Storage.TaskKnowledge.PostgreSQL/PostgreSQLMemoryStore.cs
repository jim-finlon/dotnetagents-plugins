using DotNetAgents.Abstractions.Memory;
using Npgsql;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DotNetAgents.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of <see cref="IMemoryStore"/> for persistent conversation memory.
/// Each instance is scoped to a specific session for message isolation.
/// </summary>
public class PostgreSQLMemoryStore : IMemoryStore
{
    private readonly string _connectionString;
    private readonly string _sessionId;
    private readonly string _tableName;
    private readonly ILogger<PostgreSQLMemoryStore>? _logger;
    private readonly List<MemoryMessage> _pendingMessages = new();
    private readonly object _lock = new();
    private long _nextSequenceNumber;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLMemoryStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="sessionId">The session identifier to scope this memory store to.</param>
    /// <param name="tableName">The table name for storing messages. Default: "conversation_messages".</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    /// <exception cref="ArgumentException">Thrown when session ID is null or whitespace.</exception>
    public PostgreSQLMemoryStore(
        string connectionString,
        string sessionId,
        string tableName = "conversation_messages",
        ILogger<PostgreSQLMemoryStore>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _sessionId = !string.IsNullOrWhiteSpace(sessionId) ? sessionId : throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId));
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _logger = logger;

        EnsureTableExistsAsync().GetAwaiter().GetResult();
        InitializeSequenceNumberAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task AddMessageAsync(
        MemoryMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            long sequenceNumber;
            lock (_lock)
            {
                sequenceNumber = ++_nextSequenceNumber;
            }

            var command = new NpgsqlCommand(
                $@"INSERT INTO {_tableName} (
                    id, session_id, content, role, timestamp, metadata, sequence_number)
                VALUES (
                    @id, @session_id, @content, @role, @timestamp, @metadata, @sequence_number)",
                connection);

            command.Parameters.AddWithValue("@id", Guid.NewGuid());
            command.Parameters.AddWithValue("@session_id", _sessionId);
            command.Parameters.AddWithValue("@content", message.Content);
            command.Parameters.AddWithValue("@role", message.Role);
            command.Parameters.AddWithValue("@timestamp", message.Timestamp);
            command.Parameters.AddWithValue("@metadata",
                message.Metadata != null ? JsonSerializer.Serialize(message.Metadata) : (object)DBNull.Value);
            command.Parameters.AddWithValue("@sequence_number", sequenceNumber);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug(
                "Message added. SessionId: {SessionId}, Role: {Role}, Sequence: {Sequence}",
                _sessionId, message.Role, sequenceNumber);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to add message. SessionId: {SessionId}", _sessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MemoryMessage>> GetMessagesAsync(
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        if (count < 0)
            throw new ArgumentException("Count must be non-negative.", nameof(count));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Get the most recent N messages, ordered oldest to newest
            var command = new NpgsqlCommand(
                $@"SELECT content, role, timestamp, metadata
                   FROM (
                       SELECT content, role, timestamp, metadata, sequence_number
                       FROM {_tableName}
                       WHERE session_id = @session_id
                       ORDER BY sequence_number DESC
                       LIMIT @count
                   ) sub
                   ORDER BY sequence_number ASC",
                connection);

            command.Parameters.AddWithValue("@session_id", _sessionId);
            command.Parameters.AddWithValue("@count", count);

            var messages = new List<MemoryMessage>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                messages.Add(ReadMessage(reader));
            }

            return messages;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get messages. SessionId: {SessionId}", _sessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $"DELETE FROM {_tableName} WHERE session_id = @session_id",
                connection);

            command.Parameters.AddWithValue("@session_id", _sessionId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                _nextSequenceNumber = 0;
                _pendingMessages.Clear();
            }

            _logger?.LogInformation("Memory cleared. SessionId: {SessionId}", _sessionId);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to clear memory. SessionId: {SessionId}", _sessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        // Messages are persisted immediately on AddMessageAsync, so Save is a no-op.
        // This is by design for durability -- every message is written to PostgreSQL on arrival.
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        // Re-initialize the sequence number from the database
        await InitializeSequenceNumberAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation(
            "Memory loaded from database. SessionId: {SessionId}, NextSequence: {NextSequence}",
            _sessionId, _nextSequenceNumber);
    }

    private static MemoryMessage ReadMessage(NpgsqlDataReader reader)
    {
        var content = reader.GetString(reader.GetOrdinal("content"));
        var role = reader.GetString(reader.GetOrdinal("role"));
        var timestamp = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("timestamp"));

        var metadataOrdinal = reader.GetOrdinal("metadata");
        IDictionary<string, object>? metadata = null;
        if (!reader.IsDBNull(metadataOrdinal))
        {
            var metadataJson = reader.GetString(metadataOrdinal);
            metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson);
        }

        return new MemoryMessage
        {
            Content = content,
            Role = role,
            Timestamp = timestamp,
            Metadata = metadata
        };
    }

    private async Task InitializeSequenceNumberAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT COALESCE(MAX(sequence_number), 0) FROM {_tableName} WHERE session_id = @session_id",
                connection);

            command.Parameters.AddWithValue("@session_id", _sessionId);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            lock (_lock)
            {
                _nextSequenceNumber = Convert.ToInt64(result);
            }
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to initialize sequence number. SessionId: {SessionId}", _sessionId);
            throw;
        }
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
                    content TEXT NOT NULL,
                    role VARCHAR(50) NOT NULL,
                    timestamp TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    metadata JSONB NULL,
                    sequence_number BIGINT NOT NULL DEFAULT 0
                )",
                connection);

            await createTableCommand.ExecuteNonQueryAsync().ConfigureAwait(false);

            var indexCommands = new[]
            {
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_session_id ON {_tableName} (session_id)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_session_sequence ON {_tableName} (session_id, sequence_number DESC)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_timestamp ON {_tableName} (timestamp DESC)"
            };

            foreach (var indexSql in indexCommands)
            {
                var indexCommand = new NpgsqlCommand(indexSql, connection);
                await indexCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            _logger?.LogInformation("Ensured conversation messages table exists: {TableName}", _tableName);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to ensure conversation messages table exists");
            throw;
        }
    }
}
