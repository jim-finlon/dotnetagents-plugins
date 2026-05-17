using DotNetAgents.Knowledge.Models;
using DotNetAgents.Knowledge.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using KnowledgeCategory = DotNetAgents.Knowledge.Models.KnowledgeCategory;
using KnowledgeSeverity = DotNetAgents.Knowledge.Models.KnowledgeSeverity;

namespace DotNetAgents.Storage.SqlServer;

/// <summary>
/// SQL Server implementation of <see cref="IKnowledgeStore"/> for persistent knowledge storage.
/// </summary>
public class SqlServerKnowledgeStore : IKnowledgeStore
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly ILogger<SqlServerKnowledgeStore>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerKnowledgeStore"/> class.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <param name="tableName">The table name for storing knowledge items. Default: "KnowledgeItems".</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public SqlServerKnowledgeStore(
        string connectionString,
        string tableName = "KnowledgeItems",
        ILogger<SqlServerKnowledgeStore>? logger = null)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"SELECT Id, SessionId, WorkflowRunId, TaskId, Title, Description, Context, Solution,
                          Category, Severity, Tags, TechStack, SourceSession, ImportedAt, ImportSource,
                          ErrorMessage, StackTrace, ToolName, ToolParameters, ReferenceCount, Metadata,
                          CreatedAt, UpdatedAt, LastReferencedAt, ContentHash
                   FROM [{_tableName}]
                   WHERE Id = @Id",
                connection);

            command.Parameters.AddWithValue("@Id", knowledgeId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return ReadKnowledgeItem(reader);
        }
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
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

            var command = new SqlCommand(
                $@"INSERT INTO [{_tableName}]
                   (Id, SessionId, WorkflowRunId, TaskId, Title, Description, Context, Solution,
                    Category, Severity, Tags, TechStack, SourceSession, ImportedAt, ImportSource,
                    ErrorMessage, StackTrace, ToolName, ToolParameters, ReferenceCount, Metadata,
                    CreatedAt, UpdatedAt, LastReferencedAt, ContentHash)
                   VALUES
                   (@Id, @SessionId, @WorkflowRunId, @TaskId, @Title, @Description, @Context, @Solution,
                    @Category, @Severity, @Tags, @TechStack, @SourceSession, @ImportedAt, @ImportSource,
                    @ErrorMessage, @StackTrace, @ToolName, @ToolParameters, @ReferenceCount, @Metadata,
                    @CreatedAt, @UpdatedAt, @LastReferencedAt, @ContentHash)",
                connection);

            AddKnowledgeParameters(command, knowledgeToCreate);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(
                "Knowledge item created. KnowledgeId: {KnowledgeId}, SessionId: {SessionId}",
                knowledgeToCreate.Id,
                knowledgeToCreate.SessionId);

            return knowledgeToCreate;
        }
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var updatedKnowledge = knowledge with
            {
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var command = new SqlCommand(
                $@"UPDATE [{_tableName}] SET
                   SessionId = @SessionId,
                   WorkflowRunId = @WorkflowRunId,
                   TaskId = @TaskId,
                   Title = @Title,
                   Description = @Description,
                   Context = @Context,
                   Solution = @Solution,
                   Category = @Category,
                   Severity = @Severity,
                   Tags = @Tags,
                   TechStack = @TechStack,
                   SourceSession = @SourceSession,
                   ImportedAt = @ImportedAt,
                   ImportSource = @ImportSource,
                   ErrorMessage = @ErrorMessage,
                   StackTrace = @StackTrace,
                   ToolName = @ToolName,
                   ToolParameters = @ToolParameters,
                   ReferenceCount = @ReferenceCount,
                   Metadata = @Metadata,
                   UpdatedAt = @UpdatedAt,
                   LastReferencedAt = @LastReferencedAt
                   WHERE Id = @Id",
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
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"DELETE FROM [{_tableName}] WHERE Id = @Id",
                connection);

            command.Parameters.AddWithValue("@Id", knowledgeId);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Knowledge item deleted. KnowledgeId: {KnowledgeId}", knowledgeId);
        }
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            SqlCommand command;
            if (sessionId == null)
            {
                command = new SqlCommand(
                    $@"SELECT Id, SessionId, WorkflowRunId, TaskId, Title, Description, Context, Solution,
                              Category, Severity, Tags, TechStack, SourceSession, ImportedAt, ImportSource,
                              ErrorMessage, StackTrace, ToolName, ToolParameters, ReferenceCount, Metadata,
                              CreatedAt, UpdatedAt, LastReferencedAt, ContentHash
                       FROM [{_tableName}]
                       WHERE SessionId IS NULL
                       ORDER BY CreatedAt DESC",
                    connection);
            }
            else
            {
                command = new SqlCommand(
                    $@"SELECT Id, SessionId, WorkflowRunId, TaskId, Title, Description, Context, Solution,
                              Category, Severity, Tags, TechStack, SourceSession, ImportedAt, ImportSource,
                              ErrorMessage, StackTrace, ToolName, ToolParameters, ReferenceCount, Metadata,
                              CreatedAt, UpdatedAt, LastReferencedAt, ContentHash
                       FROM [{_tableName}]
                       WHERE SessionId = @SessionId
                       ORDER BY CreatedAt DESC",
                    connection);
                command.Parameters.AddWithValue("@SessionId", sessionId);
            }

            var items = new List<KnowledgeItem>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(ReadKnowledgeItem(reader));
            }

            return items;
        }
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Build WHERE clause
            var whereConditions = new List<string>();
            var parameters = new List<SqlParameter>();

            if (!string.IsNullOrWhiteSpace(query.SessionId))
            {
                if (query.IncludeGlobal)
                {
                    whereConditions.Add("(SessionId = @SessionId OR SessionId IS NULL)");
                }
                else
                {
                    whereConditions.Add("SessionId = @SessionId");
                }
                parameters.Add(new SqlParameter("@SessionId", query.SessionId));
            }
            else if (!query.IncludeGlobal)
            {
                whereConditions.Add("SessionId IS NOT NULL");
            }

            if (query.Category.HasValue)
            {
                whereConditions.Add("Category = @Category");
                parameters.Add(new SqlParameter("@Category", query.Category.Value.ToString()));
            }

            if (query.Severity.HasValue)
            {
                whereConditions.Add("Severity = @Severity");
                parameters.Add(new SqlParameter("@Severity", query.Severity.Value.ToString()));
            }

            if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                whereConditions.Add("(Title LIKE @SearchText OR Description LIKE @SearchText OR Context LIKE @SearchText OR Solution LIKE @SearchText)");
                parameters.Add(new SqlParameter("@SearchText", $"%{query.SearchText}%"));
            }

            var whereClause = whereConditions.Count > 0 ? "WHERE " + string.Join(" AND ", whereConditions) : "";

            // Get total count
            var countCommand = new SqlCommand(
                $@"SELECT COUNT(*) FROM [{_tableName}] {whereClause}",
                connection);
            countCommand.Parameters.AddRange(parameters.ToArray());
            var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

            // Build ORDER BY
            var orderBy = query.SortDescending ? "DESC" : "ASC";
            var sortBy = query.SortBy switch
            {
                "CreatedAt" => "CreatedAt",
                "ReferenceCount" => "ReferenceCount",
                "UpdatedAt" => "UpdatedAt",
                _ => "CreatedAt"
            };

            // Get paginated results
            var skip = (query.Page - 1) * query.PageSize;
            var selectCommand = new SqlCommand(
                $@"SELECT Id, SessionId, WorkflowRunId, TaskId, Title, Description, Context, Solution,
                          Category, Severity, Tags, TechStack, SourceSession, ImportedAt, ImportSource,
                          ErrorMessage, StackTrace, ToolName, ToolParameters, ReferenceCount, Metadata,
                          CreatedAt, UpdatedAt, LastReferencedAt, ContentHash
                   FROM [{_tableName}]
                   {whereClause}
                   ORDER BY {sortBy} {orderBy}
                   OFFSET {skip} ROWS
                   FETCH NEXT {query.PageSize} ROWS ONLY",
                connection);
            selectCommand.Parameters.AddRange(parameters.ToArray());

            var items = new List<KnowledgeItem>();

            using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
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
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var whereConditions = new List<string> { "(Title LIKE @SearchText OR Description LIKE @SearchText OR Context LIKE @SearchText OR Solution LIKE @SearchText)" };
            var parameters = new List<SqlParameter> { new SqlParameter("@SearchText", $"%{searchText}%") };

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                if (includeGlobal)
                {
                    whereConditions.Add("(SessionId = @SessionId OR SessionId IS NULL)");
                }
                else
                {
                    whereConditions.Add("SessionId = @SessionId");
                }
                parameters.Add(new SqlParameter("@SessionId", sessionId));
            }
            else if (!includeGlobal)
            {
                whereConditions.Add("SessionId IS NOT NULL");
            }

            var whereClause = "WHERE " + string.Join(" AND ", whereConditions);

            var command = new SqlCommand(
                $@"SELECT Id, SessionId, WorkflowRunId, TaskId, Title, Description, Context, Solution,
                          Category, Severity, Tags, TechStack, SourceSession, ImportedAt, ImportSource,
                          ErrorMessage, StackTrace, ToolName, ToolParameters, ReferenceCount, Metadata,
                          CreatedAt, UpdatedAt, LastReferencedAt, ContentHash
                   FROM [{_tableName}]
                   {whereClause}
                   ORDER BY ReferenceCount DESC, CreatedAt DESC",
                connection);
            command.Parameters.AddRange(parameters.ToArray());

            var items = new List<KnowledgeItem>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(ReadKnowledgeItem(reader));
            }

            return items;
        }
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"UPDATE [{_tableName}]
                   SET ReferenceCount = ReferenceCount + 1,
                       LastReferencedAt = @LastReferencedAt
                   WHERE Id = @Id",
                connection);

            command.Parameters.AddWithValue("@Id", knowledgeId);
            command.Parameters.AddWithValue("@LastReferencedAt", DateTimeOffset.UtcNow);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Get global knowledge items ordered by reference count
            var command = new SqlCommand(
                $@"SELECT TOP (@MaxResults * 3) Id, SessionId, WorkflowRunId, TaskId, Title, Description, Context, Solution,
                          Category, Severity, Tags, TechStack, SourceSession, ImportedAt, ImportSource,
                          ErrorMessage, StackTrace, ToolName, ToolParameters, ReferenceCount, Metadata,
                          CreatedAt, UpdatedAt, LastReferencedAt, ContentHash
                   FROM [{_tableName}]
                   WHERE SessionId IS NULL
                   ORDER BY ReferenceCount DESC, LastReferencedAt DESC, CreatedAt DESC",
                connection);

            command.Parameters.AddWithValue("@MaxResults", maxResults * 3);

            var candidates = new List<KnowledgeItem>();

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
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
        catch (SqlException ex)
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
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var command = new SqlCommand(
                $@"SELECT TOP 1 Id, SessionId, WorkflowRunId, TaskId, Title, Description, Context, Solution,
                          Category, Severity, Tags, TechStack, SourceSession, ImportedAt, ImportSource,
                          ErrorMessage, StackTrace, ToolName, ToolParameters, ReferenceCount, Metadata,
                          CreatedAt, UpdatedAt, LastReferencedAt, ContentHash
                   FROM [{_tableName}]
                   WHERE ContentHash = @ContentHash",
                connection);

            command.Parameters.AddWithValue("@ContentHash", contentHash);

            using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            return ReadKnowledgeItem(reader);
        }
        catch (SqlException ex)
        {
            _logger?.LogError(ex, "Failed to get knowledge item by content hash");
            throw;
        }
    }

    private KnowledgeItem ReadKnowledgeItem(SqlDataReader reader)
    {
        var id = reader.GetGuid(reader.GetOrdinal("Id"));
        var sessionIdOrdinal = reader.GetOrdinal("SessionId");
        var sessionId = reader.IsDBNull(sessionIdOrdinal) ? null : reader.GetString(sessionIdOrdinal);
        var workflowRunIdOrdinal = reader.GetOrdinal("WorkflowRunId");
        var workflowRunId = reader.IsDBNull(workflowRunIdOrdinal) ? null : reader.GetString(workflowRunIdOrdinal);
        var taskIdOrdinal = reader.GetOrdinal("TaskId");
        var taskId = reader.IsDBNull(taskIdOrdinal) ? (Guid?)null : reader.GetGuid(taskIdOrdinal);
        var title = reader.GetString(reader.GetOrdinal("Title"));
        var description = reader.GetString(reader.GetOrdinal("Description"));
        var contextOrdinal = reader.GetOrdinal("Context");
        var context = reader.IsDBNull(contextOrdinal) ? null : reader.GetString(contextOrdinal);
        var solutionOrdinal = reader.GetOrdinal("Solution");
        var solution = reader.IsDBNull(solutionOrdinal) ? null : reader.GetString(solutionOrdinal);
        var category = Enum.Parse<KnowledgeCategory>(reader.GetString(reader.GetOrdinal("Category")));
        var severity = Enum.Parse<KnowledgeSeverity>(reader.GetString(reader.GetOrdinal("Severity")));

        var tagsJson = reader.GetString(reader.GetOrdinal("Tags"));
        var tags = string.IsNullOrWhiteSpace(tagsJson)
            ? (IReadOnlyList<string>)new List<string>()
            : (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(tagsJson) ?? new List<string>());

        var techStackJson = reader.GetString(reader.GetOrdinal("TechStack"));
        var techStack = string.IsNullOrWhiteSpace(techStackJson)
            ? (IReadOnlyList<string>)new List<string>()
            : (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(techStackJson) ?? new List<string>());

        var sourceSessionOrdinal = reader.GetOrdinal("SourceSession");
        var sourceSession = reader.IsDBNull(sourceSessionOrdinal) ? null : reader.GetString(sourceSessionOrdinal);
        var importedAtOrdinal = reader.GetOrdinal("ImportedAt");
        var importedAt = reader.IsDBNull(importedAtOrdinal) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(importedAtOrdinal);
        var importSourceOrdinal = reader.GetOrdinal("ImportSource");
        var importSource = reader.IsDBNull(importSourceOrdinal) ? null : reader.GetString(importSourceOrdinal);
        var errorMessageOrdinal = reader.GetOrdinal("ErrorMessage");
        var errorMessage = reader.IsDBNull(errorMessageOrdinal) ? null : reader.GetString(errorMessageOrdinal);
        var stackTraceOrdinal = reader.GetOrdinal("StackTrace");
        var stackTrace = reader.IsDBNull(stackTraceOrdinal) ? null : reader.GetString(stackTraceOrdinal);
        var toolNameOrdinal = reader.GetOrdinal("ToolName");
        var toolName = reader.IsDBNull(toolNameOrdinal) ? null : reader.GetString(toolNameOrdinal);

        var toolParametersJson = reader.GetString(reader.GetOrdinal("ToolParameters"));
        var toolParameters = string.IsNullOrWhiteSpace(toolParametersJson)
            ? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>()
            : (IReadOnlyDictionary<string, object>)(JsonSerializer.Deserialize<Dictionary<string, object>>(toolParametersJson) ?? new Dictionary<string, object>());

        var referenceCount = reader.GetInt32(reader.GetOrdinal("ReferenceCount"));

        var metadataJson = reader.GetString(reader.GetOrdinal("Metadata"));
        var metadata = string.IsNullOrWhiteSpace(metadataJson)
            ? (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()
            : (IReadOnlyDictionary<string, string>)(JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? new Dictionary<string, string>());

        var createdAt = reader.GetDateTimeOffset(reader.GetOrdinal("CreatedAt"));
        var updatedAt = reader.GetDateTimeOffset(reader.GetOrdinal("UpdatedAt"));
        var lastReferencedAtOrdinal = reader.GetOrdinal("LastReferencedAt");
        var lastReferencedAt = reader.IsDBNull(lastReferencedAtOrdinal) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(lastReferencedAtOrdinal);
        var contentHashOrdinal = reader.GetOrdinal("ContentHash");
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

    private void AddKnowledgeParameters(SqlCommand command, KnowledgeItem knowledge)
    {
        command.Parameters.AddWithValue("@Id", knowledge.Id);
        command.Parameters.AddWithValue("@SessionId", knowledge.SessionId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@WorkflowRunId", knowledge.WorkflowRunId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@TaskId", knowledge.TaskId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Title", knowledge.Title);
        command.Parameters.AddWithValue("@Description", knowledge.Description);
        command.Parameters.AddWithValue("@Context", knowledge.Context ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Solution", knowledge.Solution ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Category", knowledge.Category.ToString());
        command.Parameters.AddWithValue("@Severity", knowledge.Severity.ToString());
        command.Parameters.AddWithValue("@Tags", JsonSerializer.Serialize(knowledge.Tags));
        command.Parameters.AddWithValue("@TechStack", JsonSerializer.Serialize(knowledge.TechStack));
        command.Parameters.AddWithValue("@SourceSession", knowledge.SourceSession ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ImportedAt", knowledge.ImportedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ImportSource", knowledge.ImportSource ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ErrorMessage", knowledge.ErrorMessage ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@StackTrace", knowledge.StackTrace ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ToolName", knowledge.ToolName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ToolParameters", JsonSerializer.Serialize(knowledge.ToolParameters));
        command.Parameters.AddWithValue("@ReferenceCount", knowledge.ReferenceCount);
        command.Parameters.AddWithValue("@Metadata", JsonSerializer.Serialize(knowledge.Metadata));
        command.Parameters.AddWithValue("@CreatedAt", knowledge.CreatedAt);
        command.Parameters.AddWithValue("@UpdatedAt", knowledge.UpdatedAt);
        command.Parameters.AddWithValue("@LastReferencedAt", knowledge.LastReferencedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@ContentHash", knowledge.ContentHash ?? (object)DBNull.Value);
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
                           SessionId NVARCHAR(100) NULL,
                           WorkflowRunId NVARCHAR(100) NULL,
                           TaskId UNIQUEIDENTIFIER NULL,
                           Title NVARCHAR(500) NOT NULL,
                           Description NVARCHAR(MAX) NOT NULL,
                           Context NVARCHAR(MAX) NULL,
                           Solution NVARCHAR(MAX) NULL,
                           Category NVARCHAR(50) NOT NULL,
                           Severity NVARCHAR(20) NOT NULL,
                           Tags NVARCHAR(MAX) NOT NULL DEFAULT '[]',
                           TechStack NVARCHAR(MAX) NOT NULL DEFAULT '[]',
                           SourceSession NVARCHAR(100) NULL,
                           ImportedAt DATETIMEOFFSET NULL,
                           ImportSource NVARCHAR(500) NULL,
                           ErrorMessage NVARCHAR(MAX) NULL,
                           StackTrace NVARCHAR(MAX) NULL,
                           ToolName NVARCHAR(200) NULL,
                           ToolParameters NVARCHAR(MAX) NOT NULL DEFAULT '{{}}',
                           ReferenceCount INT NOT NULL DEFAULT 0,
                           Metadata NVARCHAR(MAX) NOT NULL DEFAULT '{{}}',
                           CreatedAt DATETIMEOFFSET NOT NULL,
                           UpdatedAt DATETIMEOFFSET NOT NULL,
                           LastReferencedAt DATETIMEOFFSET NULL,
                           ContentHash NVARCHAR(64) NULL,
                           INDEX IX_SessionId (SessionId),
                           INDEX IX_WorkflowRunId (WorkflowRunId),
                           INDEX IX_Category (Category),
                           INDEX IX_Severity (Severity),
                           INDEX IX_ContentHash (ContentHash),
                           INDEX IX_CreatedAt (CreatedAt),
                           INDEX IX_ReferenceCount (ReferenceCount)
                       )
                   END",
                connection);

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);

            _logger?.LogInformation("Ensured knowledge table exists: {TableName}", _tableName);
        }
        catch (SqlException ex)
        {
            _logger?.LogError(ex, "Failed to ensure knowledge table exists");
            throw;
        }
    }
}
