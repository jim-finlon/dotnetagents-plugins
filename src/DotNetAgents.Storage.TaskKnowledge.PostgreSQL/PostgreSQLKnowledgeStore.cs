using DotNetAgents.Knowledge.Models;
using DotNetAgents.Knowledge.Storage;
using Npgsql;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Knowledgecategory = DotNetAgents.Knowledge.Models.KnowledgeCategory;
using Knowledgeseverity = DotNetAgents.Knowledge.Models.KnowledgeSeverity;

namespace DotNetAgents.Storage.PostgreSQL;

/// <summary>
/// PostgreSQL implementation of <see cref="IKnowledgeStore"/> for persistent knowledge storage.
/// </summary>
public class PostgreSQLKnowledgeStore : IKnowledgeStore
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly ILogger<PostgreSQLKnowledgeStore>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLKnowledgeStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The table name for storing knowledge items. Default: "knowledge_items".</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public PostgreSQLKnowledgeStore(
        string connectionString,
        string tableName = "knowledge_items",
        ILogger<PostgreSQLKnowledgeStore>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _logger = logger;

        // Ensure table exists
        EnsureTableExistsAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task<KnowledgeItem?> GetByIdAsync(Guid knowledgeId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT id, session_id, workflow_run_id, task_id, title, description, context, solution, category, severity, tags, tech_stack, source_session, imported_at, import_source, error_message, stack_trace, tool_name, tool_parameters, reference_count, metadata, created_at, updated_at, last_referenced_at, content_hash
                   FROM {_tableName}
                   WHERE id = @id",
                connection);

            command.Parameters.AddWithValue("@id", knowledgeId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return ReadKnowledgeItem(reader);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get knowledge item. KnowledgeId: {KnowledgeId}", knowledgeId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<KnowledgeItem> CreateAsync(KnowledgeItem knowledge, CancellationToken cancellationToken = default)
    {
        if (knowledge == null)
            throw new ArgumentNullException(nameof(knowledge));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Calculate content hash if not provided
            var contentHash = knowledge.ContentHash;
            if (string.IsNullOrWhiteSpace(contentHash))
            {
                contentHash = DotNetAgents.Knowledge.Helpers.ContentHashHelper.CalculateContentHash(knowledge.Title, knowledge.Description);
            }

            var knowledgeToCreate = knowledge with
            {
                Id = knowledge.Id == default ? Guid.NewGuid() : knowledge.Id,
                CreatedAt = knowledge.CreatedAt == default ? DateTimeOffset.UtcNow : knowledge.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow,
                ContentHash = contentHash
            };

            var command = new NpgsqlCommand(
                $@"INSERT INTO {_tableName} (id, session_id, workflow_run_id, task_id, title, description, context, solution,
                    category, severity, tags, tech_stack, source_session, imported_at, import_source,
                    error_message, stack_trace, tool_name, tool_parameters, reference_count, metadata,
                    created_at, updated_at, last_referenced_at, content_hash)
                   VALUES
                   (@id, @session_id, @workflow_run_id, @task_id, @title, @description, @context, @solution,
                    @category, @severity, @tags, @tech_stack, @source_session, @imported_at, @import_source,
                    @error_message, @stack_trace, @tool_name, @tool_parameters, @reference_count, @metadata,
                    @created_at, @updated_at, @last_referenced_at, @content_hash)",
                connection);

            AddKnowledgeParameters(command, knowledgeToCreate);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(
                "Knowledge item created. KnowledgeId: {KnowledgeId}, SessionId: {SessionId}",
                knowledgeToCreate.Id,
                knowledgeToCreate.SessionId);

            return knowledgeToCreate;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to create knowledge item. SessionId: {SessionId}", knowledge.SessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<KnowledgeItem> UpdateAsync(KnowledgeItem knowledge, CancellationToken cancellationToken = default)
    {
        if (knowledge == null)
            throw new ArgumentNullException(nameof(knowledge));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var updatedKnowledge = knowledge with
            {
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var command = new NpgsqlCommand(
                $@"UPDATE {_tableName} SET session_id = @session_id,
                   workflow_run_id = @workflow_run_id,
                   task_id = @task_id,
                   title = @title,
                   description = @description,
                   context = @context,
                   solution = @solution,
                   category = @category,
                   severity = @severity,
                   tags = @tags,
                   tech_stack = @tech_stack,
                   source_session = @source_session,
                   imported_at = @imported_at,
                   import_source = @import_source,
                   error_message = @error_message,
                   stack_trace = @stack_trace,
                   tool_name = @tool_name,
                   tool_parameters = @tool_parameters,
                   reference_count = @reference_count,
                   metadata = @metadata,
                   updated_at = @updated_at,
                   last_referenced_at = @last_referenced_at
                   WHERE id = @id",
                connection);

            AddKnowledgeParameters(command, updatedKnowledge);

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (rowsAffected == 0)
            {
                throw new InvalidOperationException($"Knowledge item {knowledge.Id} not found.");
            }

            _logger?.LogInformation("Knowledge item updated. KnowledgeId: {KnowledgeId}", updatedKnowledge.Id);

            return updatedKnowledge;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to update knowledge item. KnowledgeId: {KnowledgeId}", knowledge.Id);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(Guid knowledgeId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"DELETE FROM {_tableName} WHERE id = @id",
                connection);

            command.Parameters.AddWithValue("@id", knowledgeId);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Knowledge item deleted. KnowledgeId: {KnowledgeId}", knowledgeId);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to delete knowledge item. KnowledgeId: {KnowledgeId}", knowledgeId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<KnowledgeItem>> GetBySessionIdAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            NpgsqlCommand command;
            if (sessionId == null)
            {
                command = new NpgsqlCommand(
                    $@"SELECT id, session_id, workflow_run_id, task_id, title, description, context, solution, category, severity, tags, tech_stack, source_session, imported_at, import_source, error_message, stack_trace, tool_name, tool_parameters, reference_count, metadata, created_at, updated_at, last_referenced_at, content_hash
                       FROM {_tableName}
                       WHERE session_id IS NULL
                       ORDER BY created_at DESC",
                    connection);
            }
            else
            {
                command = new NpgsqlCommand(
                    $@"SELECT id, session_id, workflow_run_id, task_id, title, description, context, solution, category, severity, tags, tech_stack, source_session, imported_at, import_source, error_message, stack_trace, tool_name, tool_parameters, reference_count, metadata, created_at, updated_at, last_referenced_at, content_hash
                       FROM {_tableName}
                       WHERE session_id = @session_id
                       ORDER BY created_at DESC",
                    connection);
                command.Parameters.AddWithValue("@session_id", sessionId);
            }

            var items = new List<KnowledgeItem>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(ReadKnowledgeItem(reader));
            }

            return items;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get knowledge items by session. SessionId: {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<PagedResult<KnowledgeItem>> QueryAsync(
        KnowledgeQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query == null)
            throw new ArgumentNullException(nameof(query));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Build WHERE clause
            var whereConditions = new List<string>();
            var parameters = new List<NpgsqlParameter>();

            if (!string.IsNullOrWhiteSpace(query.SessionId))
            {
                if (query.IncludeGlobal)
                {
                    whereConditions.Add("(session_id = @session_id OR session_id IS NULL)");
                }
                else
                {
                    whereConditions.Add("session_id = @session_id");
                }
                parameters.Add(new NpgsqlParameter("@session_id", query.SessionId));
            }
            else if (!query.IncludeGlobal)
            {
                whereConditions.Add("session_id IS NOT NULL");
            }

            if (query.Category.HasValue)
            {
                whereConditions.Add("category = @category");
                parameters.Add(new NpgsqlParameter("@category", query.Category.Value.ToString()));
            }

            if (query.Severity.HasValue)
            {
                whereConditions.Add("severity = @severity");
                parameters.Add(new NpgsqlParameter("@severity", query.Severity.Value.ToString()));
            }

            if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                whereConditions.Add("(title LIKE @search_text OR description LIKE @search_text OR context LIKE @search_text OR solution LIKE @search_text)");
                parameters.Add(new NpgsqlParameter("@search_text", $"%{query.SearchText}%"));
            }

            var whereClause = whereConditions.Count > 0 ? "WHERE " + string.Join(" AND ", whereConditions) : "";

            // Get total count
            var countCommand = new NpgsqlCommand(
                $@"SELECT COUNT(*) FROM {_tableName} {whereClause}",
                connection);
            countCommand.Parameters.AddRange(parameters.ToArray());
            var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

            // Build ORDER BY
            var orderBy = query.SortDescending ? "DESC" : "ASC";
            var sortBy = query.SortBy switch
            {
                "CreatedAt" => "created_at",
                "ReferenceCount" => "reference_count",
                "UpdatedAt" => "updated_at",
                _ => "created_at"
            };

            // Get paginated results
            var skip = (query.Page - 1) * query.PageSize;
            var selectCommand = new NpgsqlCommand(
                $@"SELECT id, session_id, workflow_run_id, task_id, title, description, context, solution, category, severity, tags, tech_stack, source_session, imported_at, import_source, error_message, stack_trace, tool_name, tool_parameters, reference_count, metadata, created_at, updated_at, last_referenced_at, content_hash
                   FROM {_tableName}
                   {whereClause}
                   ORDER BY {sortBy} {orderBy}
                   OFFSET {skip} LIMIT {query.PageSize}",
                connection);
            selectCommand.Parameters.AddRange(parameters.ToArray());

            var items = new List<KnowledgeItem>();

            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(ReadKnowledgeItem(reader));
            }

            // Filter by tags if specified (done in memory due to JSON column)
            if (query.Tags != null && query.Tags.Count > 0)
            {
                items = items.Where(k => k.Tags.Any(t => query.Tags!.Contains(t))).ToList();
            }

            return new PagedResult<KnowledgeItem>
            {
                Items = items,
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize
            };
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to query knowledge items");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<KnowledgeItem>> SearchAsync(
        string searchText,
        string? sessionId = null,
        bool includeGlobal = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            throw new ArgumentException("Search text cannot be null or whitespace.", nameof(searchText));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var whereConditions = new List<string> { "(title LIKE @search_text OR description LIKE @search_text OR context LIKE @search_text OR solution LIKE @search_text)" };
            var parameters = new List<NpgsqlParameter> { new NpgsqlParameter("@search_text", $"%{searchText}%") };

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                if (includeGlobal)
                {
                    whereConditions.Add("(session_id = @session_id OR session_id IS NULL)");
                }
                else
                {
                    whereConditions.Add("session_id = @session_id");
                }
                parameters.Add(new NpgsqlParameter("@session_id", sessionId));
            }
            else if (!includeGlobal)
            {
                whereConditions.Add("session_id IS NOT NULL");
            }

            var whereClause = "WHERE " + string.Join(" AND ", whereConditions);

            var command = new NpgsqlCommand(
                $@"SELECT id, session_id, workflow_run_id, task_id, title, description, context, solution, category, severity, tags, tech_stack, source_session, imported_at, import_source, error_message, stack_trace, tool_name, tool_parameters, reference_count, metadata, created_at, updated_at, last_referenced_at, content_hash
                   FROM {_tableName}
                   {whereClause}
                   ORDER BY reference_count DESC, created_at DESC",
                connection);
            command.Parameters.AddRange(parameters.ToArray());

            var items = new List<KnowledgeItem>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(ReadKnowledgeItem(reader));
            }

            return items;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to search knowledge items. SearchText: {SearchText}", searchText);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task IncrementReferenceCountAsync(Guid knowledgeId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"UPDATE {_tableName}
                   SET reference_count = reference_count + 1,
                       last_referenced_at = @last_referenced_at
                   WHERE id = @id",
                connection);

            command.Parameters.AddWithValue("@id", knowledgeId);
            command.Parameters.AddWithValue("@last_referenced_at", DateTimeOffset.UtcNow);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to increment reference count. KnowledgeId: {KnowledgeId}", knowledgeId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<KnowledgeItem>> GetGlobalKnowledgeAsync(CancellationToken cancellationToken = default)
    {
        return await GetBySessionIdAsync(null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<KnowledgeItem>> GetRelevantGlobalKnowledgeAsync(
        IReadOnlyList<string>? techStackTags,
        IReadOnlyList<string>? projectTags,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Get global knowledge items ordered by reference count
            var command = new NpgsqlCommand(
                $@"SELECT id, session_id, workflow_run_id, task_id, title, description, context, solution,
                          category, severity, tags, tech_stack, source_session, imported_at, import_source,
                          error_message, stack_trace, tool_name, tool_parameters, reference_count, metadata,
                          created_at, updated_at, last_referenced_at, content_hash
                   FROM {_tableName}
                   WHERE session_id IS NULL
                   ORDER BY reference_count DESC, last_referenced_at DESC, created_at DESC
                   LIMIT (@max_results * 3)",
                connection);

            command.Parameters.AddWithValue("@max_results", maxResults * 3);

            var candidates = new List<KnowledgeItem>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                candidates.Add(ReadKnowledgeItem(reader));
            }

            // Score and filter in memory
            var scoredItems = candidates
                .Select(knowledge =>
                {
                    var score = 0;

                    // Tech stack matching
                    if (techStackTags != null && techStackTags.Count > 0 && knowledge.TechStack.Count > 0)
                    {
                        var matchingTech = knowledge.TechStack.Intersect(techStackTags, StringComparer.OrdinalIgnoreCase).Count();
                        score += matchingTech * 10;
                    }

                    // Tag matching
                    if (projectTags != null && projectTags.Count > 0 && knowledge.Tags.Count > 0)
                    {
                        var matchingTags = knowledge.Tags.Intersect(projectTags, StringComparer.OrdinalIgnoreCase).Count();
                        score += matchingTags * 5;
                    }

                    // Severity weight
                    score += knowledge.Severity switch
                    {
                        KnowledgeSeverity.Critical => 20,
                        KnowledgeSeverity.Error => 15,
                        KnowledgeSeverity.Warning => 10,
                        _ => 5
                    };

                    // Reference count boost
                    score += Math.Min(knowledge.ReferenceCount, 10);

                    return new { Knowledge = knowledge, Score = score };
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Knowledge.ReferenceCount)
                .ThenByDescending(x => x.Knowledge.LastReferencedAt ?? x.Knowledge.CreatedAt)
                .Take(maxResults)
                .Select(x => x.Knowledge)
                .ToList();

            return scoredItems;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get relevant global knowledge");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<KnowledgeItem?> GetByContentHashAsync(
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
            throw new ArgumentException("Content hash cannot be null or whitespace.", nameof(contentHash));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new NpgsqlCommand(
                $@"SELECT id, session_id, workflow_run_id, task_id, title, description, context, solution,
                          category, severity, tags, tech_stack, source_session, imported_at, import_source,
                          error_message, stack_trace, tool_name, tool_parameters, reference_count, metadata,
                          created_at, updated_at, last_referenced_at, content_hash
                   FROM {_tableName}
                   WHERE content_hash = @content_hash
                   LIMIT 1",
                connection);

            command.Parameters.AddWithValue("@content_hash", contentHash);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return ReadKnowledgeItem(reader);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to get knowledge item by content hash");
            throw;
        }
    }

    private KnowledgeItem ReadKnowledgeItem(NpgsqlDataReader reader)
    {
        var id = reader.GetGuid(reader.GetOrdinal("id"));
        var sessionIdOrdinal = reader.GetOrdinal("session_id");
        var sessionId = reader.IsDBNull(sessionIdOrdinal) ? null : reader.GetString(sessionIdOrdinal);
        var workflowRunIdOrdinal = reader.GetOrdinal("workflow_run_id");
        var workflowRunId = reader.IsDBNull(workflowRunIdOrdinal) ? null : reader.GetString(workflowRunIdOrdinal);
        var taskIdOrdinal = reader.GetOrdinal("task_id");
        var taskId = reader.IsDBNull(taskIdOrdinal) ? (Guid?)null : reader.GetGuid(taskIdOrdinal);
        var title = reader.GetString(reader.GetOrdinal("title"));
        var description = reader.GetString(reader.GetOrdinal("description"));
        var contextOrdinal = reader.GetOrdinal("context");
        var context = reader.IsDBNull(contextOrdinal) ? null : reader.GetString(contextOrdinal);
        var solutionOrdinal = reader.GetOrdinal("solution");
        var solution = reader.IsDBNull(solutionOrdinal) ? null : reader.GetString(solutionOrdinal);
        var category = Enum.Parse<KnowledgeCategory>(reader.GetString(reader.GetOrdinal("category")));
        var severity = Enum.Parse<KnowledgeSeverity>(reader.GetString(reader.GetOrdinal("severity")));

        var tagsJson = reader.GetString(reader.GetOrdinal("tags"));
        var tags = string.IsNullOrWhiteSpace(tagsJson)
            ? (IReadOnlyList<string>)new List<string>()
            : (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(tagsJson) ?? new List<string>());

        var techStackJson = reader.GetString(reader.GetOrdinal("tech_stack"));
        var techStack = string.IsNullOrWhiteSpace(techStackJson)
            ? (IReadOnlyList<string>)new List<string>()
            : (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(techStackJson) ?? new List<string>());

        var sourceSessionOrdinal = reader.GetOrdinal("source_session");
        var sourceSession = reader.IsDBNull(sourceSessionOrdinal) ? null : reader.GetString(sourceSessionOrdinal);
        var importedAtOrdinal = reader.GetOrdinal("imported_at");
        var importedAt = reader.IsDBNull(importedAtOrdinal) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(importedAtOrdinal);
        var importSourceOrdinal = reader.GetOrdinal("import_source");
        var importSource = reader.IsDBNull(importSourceOrdinal) ? null : reader.GetString(importSourceOrdinal);
        var errorMessageOrdinal = reader.GetOrdinal("error_message");
        var errorMessage = reader.IsDBNull(errorMessageOrdinal) ? null : reader.GetString(errorMessageOrdinal);
        var stackTraceOrdinal = reader.GetOrdinal("stack_trace");
        var stackTrace = reader.IsDBNull(stackTraceOrdinal) ? null : reader.GetString(stackTraceOrdinal);
        var toolNameOrdinal = reader.GetOrdinal("tool_name");
        var toolName = reader.IsDBNull(toolNameOrdinal) ? null : reader.GetString(toolNameOrdinal);

        var toolParametersJson = reader.GetString(reader.GetOrdinal("tool_parameters"));
        var toolParameters = string.IsNullOrWhiteSpace(toolParametersJson)
            ? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>()
            : (IReadOnlyDictionary<string, object>)(JsonSerializer.Deserialize<Dictionary<string, object>>(toolParametersJson) ?? new Dictionary<string, object>());

        var referenceCount = reader.GetInt32(reader.GetOrdinal("reference_count"));

        var metadataJson = reader.GetString(reader.GetOrdinal("metadata"));
        var metadata = string.IsNullOrWhiteSpace(metadataJson)
            ? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()
            : (IReadOnlyDictionary<string, string>)(JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? new Dictionary<string, string>());

        var createdAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at"));
        var updatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at"));
        var lastReferencedAtOrdinal = reader.GetOrdinal("last_referenced_at");
        var lastReferencedAt = reader.IsDBNull(lastReferencedAtOrdinal) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(lastReferencedAtOrdinal);
        var contentHashOrdinal = reader.GetOrdinal("content_hash");
        var contentHash = reader.IsDBNull(contentHashOrdinal) ? null : reader.GetString(contentHashOrdinal);

        return new KnowledgeItem
        {
            Id = id,
            SessionId = sessionId,
            WorkflowRunId = workflowRunId,
            TaskId = taskId,
            Title = title,
            Description = description,
            Context = context,
            Solution = solution,
            Category = category,
            Severity = severity,
            Tags = tags,
            TechStack = techStack,
            SourceSession = sourceSession,
            ImportedAt = importedAt,
            ImportSource = importSource,
            ErrorMessage = errorMessage,
            StackTrace = stackTrace,
            ToolName = toolName,
            ToolParameters = toolParameters,
            ReferenceCount = referenceCount,
            Metadata = metadata,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            LastReferencedAt = lastReferencedAt,
            ContentHash = contentHash
        };
    }

    private void AddKnowledgeParameters(NpgsqlCommand command, KnowledgeItem knowledge)
    {
        command.Parameters.AddWithValue("@id", knowledge.Id);
        command.Parameters.AddWithValue("@session_id", knowledge.SessionId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@workflow_run_id", knowledge.WorkflowRunId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@task_id", knowledge.TaskId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@title", knowledge.Title);
        command.Parameters.AddWithValue("@description", knowledge.Description);
        command.Parameters.AddWithValue("@context", knowledge.Context ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@solution", knowledge.Solution ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@category", knowledge.Category.ToString());
        command.Parameters.AddWithValue("@severity", knowledge.Severity.ToString());
        command.Parameters.AddWithValue("@tags", JsonSerializer.Serialize(knowledge.Tags));
        command.Parameters.AddWithValue("@tech_stack", JsonSerializer.Serialize(knowledge.TechStack));
        command.Parameters.AddWithValue("@source_session", knowledge.SourceSession ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@imported_at", knowledge.ImportedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@import_source", knowledge.ImportSource ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@error_message", knowledge.ErrorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@stack_trace", knowledge.StackTrace ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@tool_name", knowledge.ToolName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@tool_parameters", JsonSerializer.Serialize(knowledge.ToolParameters));
        command.Parameters.AddWithValue("@reference_count", knowledge.ReferenceCount);
        command.Parameters.AddWithValue("@metadata", JsonSerializer.Serialize(knowledge.Metadata));
        command.Parameters.AddWithValue("@created_at", knowledge.CreatedAt);
        command.Parameters.AddWithValue("@updated_at", knowledge.UpdatedAt);
        command.Parameters.AddWithValue("@last_referenced_at", knowledge.LastReferencedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@content_hash", knowledge.ContentHash ?? (object)DBNull.Value);
    }

    private async Task EnsureTableExistsAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            // Create table with proper PostgreSQL syntax
            var createTableCommand = new NpgsqlCommand(
                $@"CREATE TABLE IF NOT EXISTS {_tableName} (
                    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    session_id VARCHAR(100) NULL,
                    workflow_run_id VARCHAR(100) NULL,
                    task_id UUID NULL,
                    title VARCHAR(500) NOT NULL,
                    description TEXT NOT NULL,
                    context TEXT NULL,
                    solution TEXT NULL,
                    category VARCHAR(50) NOT NULL,
                    severity VARCHAR(20) NOT NULL,
                    tags TEXT NOT NULL DEFAULT '[]',
                    tech_stack TEXT NOT NULL DEFAULT '[]',
                    source_session VARCHAR(100) NULL,
                    imported_at TIMESTAMPTZ NULL,
                    import_source VARCHAR(500) NULL,
                    error_message TEXT NULL,
                    stack_trace TEXT NULL,
                    tool_name VARCHAR(200) NULL,
                    tool_parameters TEXT NOT NULL DEFAULT '{{}}',
                    reference_count INTEGER NOT NULL DEFAULT 0,
                    metadata TEXT NOT NULL DEFAULT '{{}}',
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    last_referenced_at TIMESTAMPTZ NULL,
                    content_hash VARCHAR(64) NULL
                )",
                connection);

            await createTableCommand.ExecuteNonQueryAsync().ConfigureAwait(false);

            // Create indexes separately (PostgreSQL requires indexes outside CREATE TABLE)
            var indexCommands = new[]
            {
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_session_id ON {_tableName} (session_id)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_workflow_run_id ON {_tableName} (workflow_run_id)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_category ON {_tableName} (category)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_severity ON {_tableName} (severity)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_content_hash ON {_tableName} (content_hash)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_created_at ON {_tableName} (created_at DESC)",
                $"CREATE INDEX IF NOT EXISTS ix_{_tableName}_reference_count ON {_tableName} (reference_count DESC)"
            };

            foreach (var indexSql in indexCommands)
            {
                var indexCommand = new NpgsqlCommand(indexSql, connection);
                await indexCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            _logger?.LogInformation("Ensured knowledge table exists: {TableName}", _tableName);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to ensure knowledge table exists");
            throw;
        }
    }
}
