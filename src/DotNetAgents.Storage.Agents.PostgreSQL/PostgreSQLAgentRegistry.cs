using DotNetAgents.Agents.Registry;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;

namespace DotNetAgents.Storage.Agents.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of <see cref="IAgentRegistry"/>.
/// Suitable for distributed deployments requiring persistence.
/// </summary>
public class PostgreSQLAgentRegistry : IAgentRegistry
{
    private readonly string _connectionString;
    private readonly ILogger<PostgreSQLAgentRegistry>? _logger;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLAgentRegistry"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="logger">Optional logger instance.</param>
    public PostgreSQLAgentRegistry(
        string connectionString,
        ILogger<PostgreSQLAgentRegistry>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RegisterAsync(
        AgentCapabilities capabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureSchemaExistsAsync(cancellationToken).ConfigureAwait(false);

        const string sql = @"
            INSERT INTO agent_registry (
                agent_id, agent_type, status, supported_tools, supported_intents,
                max_concurrent_tasks, metadata, last_heartbeat, current_task_count
            )
            VALUES (
                @agent_id, @agent_type, @status, @supported_tools, @supported_intents,
                @max_concurrent_tasks, @metadata, @last_heartbeat, @current_task_count
            )
            ON CONFLICT (agent_id) DO UPDATE SET
                agent_type = EXCLUDED.agent_type,
                supported_tools = EXCLUDED.supported_tools,
                supported_intents = EXCLUDED.supported_intents,
                max_concurrent_tasks = EXCLUDED.max_concurrent_tasks,
                metadata = EXCLUDED.metadata,
                last_heartbeat = EXCLUDED.last_heartbeat";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("agent_id", capabilities.AgentId);
        command.Parameters.AddWithValue("agent_type", capabilities.AgentType);
        command.Parameters.AddWithValue("status", (int)AgentStatus.Available);
        command.Parameters.AddWithValue("supported_tools", capabilities.SupportedTools);
        command.Parameters.AddWithValue("supported_intents", capabilities.SupportedIntents);
        command.Parameters.AddWithValue("max_concurrent_tasks", capabilities.MaxConcurrentTasks);
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(capabilities.Metadata, _jsonOptions));
        command.Parameters.AddWithValue("last_heartbeat", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("current_task_count", 0);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("Registered agent {AgentId} in PostgreSQL", capabilities.AgentId);
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = "DELETE FROM agent_registry WHERE agent_id = @agent_id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("agent_id", agentId);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (rowsAffected > 0)
        {
            _logger?.LogInformation("Unregistered agent {AgentId} from PostgreSQL", agentId);
        }
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(
        string agentId,
        AgentStatus status,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = "UPDATE agent_registry SET status = @status WHERE agent_id = @agent_id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("agent_id", agentId);
        command.Parameters.AddWithValue("status", (int)status);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("Updated agent {AgentId} status to {Status}", agentId, status);
    }

    /// <inheritdoc />
    public async Task UpdateTaskCountAsync(
        string agentId,
        int taskCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);
        cancellationToken.ThrowIfCancellationRequested();

        if (taskCount < 0)
            throw new ArgumentException("Task count cannot be negative.", nameof(taskCount));

        const string sql = "UPDATE agent_registry SET current_task_count = @task_count WHERE agent_id = @agent_id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("agent_id", agentId);
        command.Parameters.AddWithValue("task_count", taskCount);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("Updated agent {AgentId} task count to {TaskCount}", agentId, taskCount);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentInfo>> FindByCapabilityAsync(
        string capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(capability);
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = @"
            SELECT agent_id, agent_type, status, supported_tools, supported_intents,
                   max_concurrent_tasks, metadata, last_heartbeat, current_task_count
            FROM agent_registry
            WHERE supported_tools @> ARRAY[@capability]::text[]
               OR supported_intents @> ARRAY[@capability]::text[]";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("capability", capability);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var agents = new List<AgentInfo>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            agents.Add(MapToAgentInfo(reader));
        }

        return agents;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentInfo>> FindByTypeAsync(
        string agentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentType);
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = @"
            SELECT agent_id, agent_type, status, supported_tools, supported_intents,
                   max_concurrent_tasks, metadata, last_heartbeat, current_task_count
            FROM agent_registry
            WHERE agent_type = @agent_type";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("agent_type", agentType);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var agents = new List<AgentInfo>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            agents.Add(MapToAgentInfo(reader));
        }

        return agents;
    }

    /// <inheritdoc />
    public async Task<AgentInfo?> GetByIdAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = @"
            SELECT agent_id, agent_type, status, supported_tools, supported_intents,
                   max_concurrent_tasks, metadata, last_heartbeat, current_task_count
            FROM agent_registry
            WHERE agent_id = @agent_id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("agent_id", agentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return MapToAgentInfo(reader);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentInfo>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = @"
            SELECT agent_id, agent_type, status, supported_tools, supported_intents,
                   max_concurrent_tasks, metadata, last_heartbeat, current_task_count
            FROM agent_registry";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var agents = new List<AgentInfo>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            agents.Add(MapToAgentInfo(reader));
        }

        return agents;
    }

    /// <inheritdoc />
    public async Task RecordHeartbeatAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);
        cancellationToken.ThrowIfCancellationRequested();

        const string sql = "UPDATE agent_registry SET last_heartbeat = @last_heartbeat WHERE agent_id = @agent_id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("agent_id", agentId);
        command.Parameters.AddWithValue("last_heartbeat", DateTimeOffset.UtcNow);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private AgentInfo MapToAgentInfo(NpgsqlDataReader reader)
    {
        var metadataJson = reader.GetString(reader.GetOrdinal("metadata"));
        var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson, _jsonOptions)
            ?? new Dictionary<string, object>();

        return new AgentInfo
        {
            AgentId = reader.GetString(reader.GetOrdinal("agent_id")),
            AgentType = reader.GetString(reader.GetOrdinal("agent_type")),
            Status = (AgentStatus)reader.GetInt32(reader.GetOrdinal("status")),
            Capabilities = new AgentCapabilities
            {
                AgentId = reader.GetString(reader.GetOrdinal("agent_id")),
                AgentType = reader.GetString(reader.GetOrdinal("agent_type")),
                SupportedTools = reader.GetFieldValue<string[]>(reader.GetOrdinal("supported_tools")),
                SupportedIntents = reader.GetFieldValue<string[]>(reader.GetOrdinal("supported_intents")),
                MaxConcurrentTasks = reader.GetInt32(reader.GetOrdinal("max_concurrent_tasks")),
                Metadata = metadata
            },
            LastHeartbeat = new DateTimeOffset(reader.GetDateTime(reader.GetOrdinal("last_heartbeat"))),
            CurrentTaskCount = reader.GetInt32(reader.GetOrdinal("current_task_count"))
        };
    }

    private async Task EnsureSchemaExistsAsync(CancellationToken cancellationToken)
    {
        const string createTableSql = @"
            CREATE TABLE IF NOT EXISTS agent_registry (
                agent_id VARCHAR(255) PRIMARY KEY,
                agent_type VARCHAR(255) NOT NULL,
                status INTEGER NOT NULL DEFAULT 1,
                supported_tools TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
                supported_intents TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[],
                max_concurrent_tasks INTEGER NOT NULL DEFAULT 1,
                metadata JSONB NOT NULL DEFAULT '{}'::JSONB,
                last_heartbeat TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                current_task_count INTEGER NOT NULL DEFAULT 0,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE INDEX IF NOT EXISTS idx_agent_registry_type ON agent_registry(agent_type);
            CREATE INDEX IF NOT EXISTS idx_agent_registry_status ON agent_registry(status);
            CREATE INDEX IF NOT EXISTS idx_agent_registry_tools ON agent_registry USING GIN(supported_tools);
            CREATE INDEX IF NOT EXISTS idx_agent_registry_intents ON agent_registry USING GIN(supported_intents);
            CREATE INDEX IF NOT EXISTS idx_agent_registry_heartbeat ON agent_registry(last_heartbeat);";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
