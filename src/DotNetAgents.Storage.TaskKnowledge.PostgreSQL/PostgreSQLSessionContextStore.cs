using DotNetAgents.Workflow.Session;
using DotNetAgents.Workflow.Session.Storage;
using Npgsql;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DotNetAgents.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of <see cref="ISessionContextStore"/> for persistent session context storage.
/// </summary>
public class PostgreSQLSessionContextStore : ISessionContextStore
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly ILogger<PostgreSQLSessionContextStore>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLSessionContextStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The table name for storing session contexts. Default: "session_contexts".</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public PostgreSQLSessionContextStore(
        string connectionString,
        string tableName = "session_contexts",
        ILogger<PostgreSQLSessionContextStore>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _logger = logger;

        EnsureTableExistsAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task<SessionContext> CreateOrUpdateAsync(
        SessionContext context,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (string.IsNullOrWhiteSpace(context.SessionId))
            throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(context));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var now = DateTimeOffset.UtcNow;

            // Check if context already exists
            var existing = await GetBySessionIdInternalAsync(connection, context.SessionId, cancellationToken)
                .ConfigureAwait(false);

            var contextToStore = context with
            {
                Id = existing?.Id ?? (context.Id == default ? Guid.NewGuid() : context.Id),
                CreatedAt = existing?.CreatedAt ?? (context.CreatedAt == default ? now : context.CreatedAt),
                UpdatedAt = now
            };

            if (existing != null)
            {
                // Update
                var updateCommand = new NpgsqlCommand(
                    $@"UPDATE {_tableName} SET
                        recent_files = @recent_files,
                        last_modified_file = @last_modified_file,
                        last_commit_message = @last_commit_message,
                        last_commit_hash = @last_commit_hash,
                        key_decisions = @key_decisions,
                        open_questions = @open_questions,
                        assumptions = @assumptions,
                        recent_commands = @recent_commands,
                        recent_errors = @recent_errors,
                        working_directory = @working_directory,
                        active_branch = @active_branch,
                        metadata = @metadata,
                        updated_at = @updated_at
                    WHERE session_id = @session_id",
                    connection);

                AddContextParameters(updateCommand, contextToStore);
                await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                _logger?.LogInformation(
                    "Session context updated. SessionId: {SessionId}", contextToStore.SessionId);
            }
            else
            {
                // Insert
                var insertCommand = new NpgsqlCommand(
                    $@"INSERT INTO {_tableName} (
                        id, session_id, recent_files, last_modified_file, last_commit_message,
                        last_commit_hash, key_decisions, open_questions, assumptions,
                        recent_commands, recent_errors, working_directory, active_branch,
                        metadata, created_at, updated_at)
                    VALUES (
                        @id, @session_id, @recent_files, @last_modified_file, @last_commit_message,
                        @last_commit_hash, @key_decisions, @open_questions, @assumptions,
                        @recent_commands, @recent_errors, @working_directory, @active_branch,
                        @metadata, @created_at, @updated_at)",
                    connection);

                AddContextParameters(insertCommand, contextToStore);
                insertCommand.Parameters.AddWithValue("@id", contextToStore.Id);
                insertCommand.Parameters.AddWithValue("@created_at", contextToStore.CreatedAt);

                await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                _logger?.LogInformation(
                    "Session context created. SessionId: {SessionId}", contextToStore.SessionId);
            }

            return contextToStore;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to create or update session context. SessionId: {SessionId}",
                context.SessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<SessionContext?> GetBySessionIdAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            return await GetBySessionIdInternalAsync(connection, sessionId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get session context. SessionId: {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or whitespace.", nameof(sessionId));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $"DELETE FROM {_tableName} WHERE session_id = @session_id",
                connection);

            command.Parameters.AddWithValue("@session_id", sessionId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Session context deleted. SessionId: {SessionId}", sessionId);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to delete session context. SessionId: {SessionId}", sessionId);
            throw;
        }
    }

    private async Task<SessionContext?> GetBySessionIdInternalAsync(
        NpgsqlConnection connection,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var command = new NpgsqlCommand(
            $@"SELECT id, session_id, recent_files, last_modified_file, last_commit_message,
                      last_commit_hash, key_decisions, open_questions, assumptions,
                      recent_commands, recent_errors, working_directory, active_branch,
                      metadata, created_at, updated_at
               FROM {_tableName}
               WHERE session_id = @session_id",
            connection);

        command.Parameters.AddWithValue("@session_id", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return ReadSessionContext(reader);
    }

    private static SessionContext ReadSessionContext(NpgsqlDataReader reader)
    {
        var id = reader.GetGuid(reader.GetOrdinal("id"));
        var sessionId = reader.GetString(reader.GetOrdinal("session_id"));

        var recentFilesJson = reader.GetString(reader.GetOrdinal("recent_files"));
        var recentFiles = JsonSerializer.Deserialize<List<string>>(recentFilesJson) ?? new List<string>();

        var lastModifiedFileOrdinal = reader.GetOrdinal("last_modified_file");
        var lastModifiedFile = reader.IsDBNull(lastModifiedFileOrdinal) ? null : reader.GetString(lastModifiedFileOrdinal);

        var lastCommitMessageOrdinal = reader.GetOrdinal("last_commit_message");
        var lastCommitMessage = reader.IsDBNull(lastCommitMessageOrdinal) ? null : reader.GetString(lastCommitMessageOrdinal);

        var lastCommitHashOrdinal = reader.GetOrdinal("last_commit_hash");
        var lastCommitHash = reader.IsDBNull(lastCommitHashOrdinal) ? null : reader.GetString(lastCommitHashOrdinal);

        var keyDecisionsJson = reader.GetString(reader.GetOrdinal("key_decisions"));
        var keyDecisions = JsonSerializer.Deserialize<List<string>>(keyDecisionsJson) ?? new List<string>();

        var openQuestionsJson = reader.GetString(reader.GetOrdinal("open_questions"));
        var openQuestions = JsonSerializer.Deserialize<List<string>>(openQuestionsJson) ?? new List<string>();

        var assumptionsJson = reader.GetString(reader.GetOrdinal("assumptions"));
        var assumptions = JsonSerializer.Deserialize<Dictionary<string, string>>(assumptionsJson) ?? new Dictionary<string, string>();

        var recentCommandsJson = reader.GetString(reader.GetOrdinal("recent_commands"));
        var recentCommands = JsonSerializer.Deserialize<List<string>>(recentCommandsJson) ?? new List<string>();

        var recentErrorsJson = reader.GetString(reader.GetOrdinal("recent_errors"));
        var recentErrors = JsonSerializer.Deserialize<List<string>>(recentErrorsJson) ?? new List<string>();

        var workingDirectoryOrdinal = reader.GetOrdinal("working_directory");
        var workingDirectory = reader.IsDBNull(workingDirectoryOrdinal) ? null : reader.GetString(workingDirectoryOrdinal);

        var activeBranchOrdinal = reader.GetOrdinal("active_branch");
        var activeBranch = reader.IsDBNull(activeBranchOrdinal) ? null : reader.GetString(activeBranchOrdinal);

        var metadataJson = reader.GetString(reader.GetOrdinal("metadata"));
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? new Dictionary<string, string>();

        var createdAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at"));
        var updatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at"));

        return new SessionContext
        {
            Id = id,
            SessionId = sessionId,
            RecentFiles = recentFiles,
            LastModifiedFile = lastModifiedFile,
            LastCommitMessage = lastCommitMessage,
            LastCommitHash = lastCommitHash,
            KeyDecisions = keyDecisions,
            OpenQuestions = openQuestions,
            Assumptions = assumptions,
            RecentCommands = recentCommands,
            RecentErrors = recentErrors,
            WorkingDirectory = workingDirectory,
            ActiveBranch = activeBranch,
            Metadata = metadata,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    private static void AddContextParameters(NpgsqlCommand command, SessionContext context)
    {
        command.Parameters.AddWithValue("@session_id", context.SessionId);
        command.Parameters.AddWithValue("@recent_files", JsonSerializer.Serialize(context.RecentFiles));
        command.Parameters.AddWithValue("@last_modified_file", context.LastModifiedFile ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@last_commit_message", context.LastCommitMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@last_commit_hash", context.LastCommitHash ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@key_decisions", JsonSerializer.Serialize(context.KeyDecisions));
        command.Parameters.AddWithValue("@open_questions", JsonSerializer.Serialize(context.OpenQuestions));
        command.Parameters.AddWithValue("@assumptions", JsonSerializer.Serialize(context.Assumptions));
        command.Parameters.AddWithValue("@recent_commands", JsonSerializer.Serialize(context.RecentCommands));
        command.Parameters.AddWithValue("@recent_errors", JsonSerializer.Serialize(context.RecentErrors));
        command.Parameters.AddWithValue("@working_directory", context.WorkingDirectory ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@active_branch", context.ActiveBranch ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@metadata", JsonSerializer.Serialize(context.Metadata));
        command.Parameters.AddWithValue("@updated_at", context.UpdatedAt);
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
                    session_id VARCHAR(100) NOT NULL UNIQUE,
                    recent_files JSONB NOT NULL DEFAULT '[]'::JSONB,
                    last_modified_file TEXT NULL,
                    last_commit_message TEXT NULL,
                    last_commit_hash VARCHAR(100) NULL,
                    key_decisions JSONB NOT NULL DEFAULT '[]'::JSONB,
                    open_questions JSONB NOT NULL DEFAULT '[]'::JSONB,
                    assumptions JSONB NOT NULL DEFAULT '{{}}'::JSONB,
                    recent_commands JSONB NOT NULL DEFAULT '[]'::JSONB,
                    recent_errors JSONB NOT NULL DEFAULT '[]'::JSONB,
                    working_directory TEXT NULL,
                    active_branch VARCHAR(200) NULL,
                    metadata JSONB NOT NULL DEFAULT '{{}}'::JSONB,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                )",
                connection);

            await createTableCommand.ExecuteNonQueryAsync().ConfigureAwait(false);

            var indexCommands = new[]
            {
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_session_id ON {_tableName} (session_id)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_updated_at ON {_tableName} (updated_at DESC)"
            };

            foreach (var indexSql in indexCommands)
            {
                var indexCommand = new NpgsqlCommand(indexSql, connection);
                await indexCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            _logger?.LogInformation("Ensured session context table exists: {TableName}", _tableName);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to ensure session context table exists");
            throw;
        }
    }
}
