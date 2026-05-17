using DotNetAgents.Abstractions.Exceptions;
using DotNetAgents.Abstractions.Retrieval;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;

namespace DotNetAgents.VectorStores.PostgreSQL;

/// <summary>
/// PostgreSQL vector store implementation using pgvector extension for persistent vector storage and similarity search.
/// </summary>
public class PostgreSQLVectorStore : IVectorStore
{
    private readonly string _connectionString;
    private readonly string _tableName;
    private readonly string _idColumn;
    private readonly string _vectorColumn;
    private readonly string _metadataColumn;
    private readonly VectorDistanceFunction _distanceFunction;
    private readonly int _vectorDimensions;
    private readonly ILogger<PostgreSQLVectorStore>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLVectorStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The table name for storing vectors. Default: "vectors".</param>
    /// <param name="vectorDimensions">The number of dimensions for vectors. Default: 1536 (OpenAI embeddings).</param>
    /// <param name="distanceFunction">The distance function to use for similarity search. Default: Cosine.</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public PostgreSQLVectorStore(
        string connectionString,
        string tableName = "vectors",
        int vectorDimensions = 1536,
        VectorDistanceFunction distanceFunction = VectorDistanceFunction.Cosine,
        ILogger<PostgreSQLVectorStore>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
        _vectorDimensions = vectorDimensions > 0
            ? vectorDimensions
            : throw new ArgumentException("Vector dimensions must be positive.", nameof(vectorDimensions));
        _distanceFunction = distanceFunction;
        _logger = logger;

        _idColumn = "id";
        _vectorColumn = "embedding";
        _metadataColumn = "metadata";

        // Ensure table and extension exist
        EnsureExtensionAndTableExistsAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task<string> UpsertAsync(
        string id,
        float[] vector,
        IDictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(vector);
        if (vector.Length == 0)
            throw new ArgumentException("Vector cannot be empty.", nameof(vector));
        if (vector.Length != _vectorDimensions)
            throw new ArgumentException(
                $"Vector dimension mismatch. Expected {_vectorDimensions}, got {vector.Length}.",
                nameof(vector));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Convert vector to pgvector format
            var vectorString = FormatVectorForPgVector(vector);
            var metadataJson = metadata != null ? JsonSerializer.Serialize(metadata) : null;

            var command = new NpgsqlCommand(
                $@"INSERT INTO {_tableName} ({_idColumn}, {_vectorColumn}, {_metadataColumn})
                   VALUES (@id, @vector::vector, @metadata::jsonb)
                   ON CONFLICT ({_idColumn})
                   DO UPDATE SET {_vectorColumn} = @vector::vector, {_metadataColumn} = @metadata::jsonb",
                connection);

            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@vector", vectorString);
            command.Parameters.AddWithValue("@metadata", metadataJson ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug("Upserted vector. Id: {Id}, Dimension: {Dimension}", id, vector.Length);

            return id;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to upsert vector. Id: {Id}", id);
            throw new AgentException(
                $"Failed to upsert vector to PostgreSQL: {ex.Message}",
                ErrorCategory.Unknown,
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector,
        int topK = 10,
        IDictionary<string, object>? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queryVector);
        if (queryVector.Length == 0)
            throw new ArgumentException("Query vector cannot be empty.", nameof(queryVector));
        if (queryVector.Length != _vectorDimensions)
            throw new ArgumentException(
                $"Query vector dimension mismatch. Expected {_vectorDimensions}, got {queryVector.Length}.",
                nameof(queryVector));
        if (topK <= 0)
            throw new ArgumentException("TopK must be positive.", nameof(topK));

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var vectorString = FormatVectorForPgVector(queryVector);
            var distanceOperator = GetDistanceOperator(_distanceFunction);

            // Build WHERE clause for metadata filtering
            var whereClause = string.Empty;
            var parameters = new List<NpgsqlParameter>
            {
                new("@query_vector", vectorString),
                new("@top_k", topK)
            };

            if (filter != null && filter.Count > 0)
            {
                var filterConditions = new List<string>();
                var paramIndex = 2;
                foreach (var kvp in filter)
                {
                    var keyParamName = $"@filter_key_{paramIndex}";
                    var valueParamName = $"@filter_value_{paramIndex}";
                    // Use parameterized key to prevent SQL injection
                    filterConditions.Add($"{_metadataColumn}->>{keyParamName} = {valueParamName}");
                    parameters.Add(new NpgsqlParameter(keyParamName, kvp.Key));
                    parameters.Add(new NpgsqlParameter(valueParamName, kvp.Value?.ToString() ?? string.Empty));
                    paramIndex++;
                }
                whereClause = "WHERE " + string.Join(" AND ", filterConditions);
            }

            var command = new NpgsqlCommand(
                $@"SELECT {_idColumn}, {_vectorColumn} {distanceOperator} @query_vector::vector AS distance, {_metadataColumn}
                   FROM {_tableName}
                   {whereClause}
                   ORDER BY {_vectorColumn} {distanceOperator} @query_vector::vector
                   LIMIT @top_k",
                connection);

            foreach (var param in parameters)
            {
                command.Parameters.Add(param);
            }

            var results = new List<VectorSearchResult>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var resultId = reader.GetString(reader.GetOrdinal(_idColumn));
                var distance = reader.GetDouble(reader.GetOrdinal("distance"));

                // Convert distance to similarity score (higher is more similar)
                // For cosine distance: similarity = 1 - distance
                // For L2 distance: similarity = 1 / (1 + distance)
                // For inner product: similarity = -distance (already negative)
                var score = _distanceFunction switch
                {
                    VectorDistanceFunction.Cosine => (float)(1.0 - distance),
                    VectorDistanceFunction.L2 => (float)(1.0 / (1.0 + distance)),
                    VectorDistanceFunction.InnerProduct => (float)(-distance),
                    _ => (float)(1.0 - distance)
                };

                var metadataOrdinal = reader.GetOrdinal(_metadataColumn);
                IDictionary<string, object>? metadata = null;
                if (!reader.IsDBNull(metadataOrdinal))
                {
                    var metadataJson = reader.GetString(metadataOrdinal);
                    if (!string.IsNullOrWhiteSpace(metadataJson))
                    {
                        metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson);
                    }
                }

                results.Add(new VectorSearchResult
                {
                    Id = resultId,
                    Score = score,
                    Metadata = metadata ?? new Dictionary<string, object>()
                });
            }

            _logger?.LogDebug(
                "PostgreSQL search completed. Query dimension: {Dimension}, Results: {Count}, Distance function: {DistanceFunction}",
                queryVector.Length,
                results.Count,
                _distanceFunction);

            return results;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to search vectors in PostgreSQL");
            throw new AgentException(
                $"Failed to search vectors in PostgreSQL: {ex.Message}",
                ErrorCategory.Unknown,
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task<int> DeleteAsync(
        IEnumerable<string> ids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var idList = ids.ToList();
        if (idList.Count == 0)
            return 0;

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            // Use ANY array for efficient deletion
            var command = new NpgsqlCommand(
                $@"DELETE FROM {_tableName} WHERE {_idColumn} = ANY(@ids)",
                connection);

            command.Parameters.AddWithValue("@ids", idList.ToArray());

            var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug("Deleted {Count} vectors", rowsAffected);

            return rowsAffected;
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to delete vectors");
            throw new AgentException(
                $"Failed to delete vectors from PostgreSQL: {ex.Message}",
                ErrorCategory.Unknown,
                ex);
        }
    }

    /// <summary>
    /// Creates an HNSW index on the vector column for faster similarity searches.
    /// </summary>
    /// <param name="m">The number of connections per layer (default: 16).</param>
    /// <param name="efConstruction">The size of the candidate list during construction (default: 64).</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CreateHnswIndexAsync(
        int m = 16,
        int efConstruction = 64,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var indexName = $"idx_{_tableName}_{_vectorColumn}_hnsw";
            var opClass = _distanceFunction switch
            {
                VectorDistanceFunction.Cosine => "vector_cosine_ops",
                VectorDistanceFunction.L2 => "vector_l2_ops",
                VectorDistanceFunction.InnerProduct => "vector_ip_ops",
                _ => "vector_cosine_ops"
            };

            var command = new NpgsqlCommand(
                $@"CREATE INDEX IF NOT EXISTS {indexName}
                   ON {_tableName}
                   USING hnsw ({_vectorColumn} {opClass})
                   WITH (m = {m}, ef_construction = {efConstruction})",
                connection);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(
                "Created HNSW index. Table: {TableName}, Index: {IndexName}, M: {M}, EF_Construction: {EFConstruction}",
                _tableName,
                indexName,
                m,
                efConstruction);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to create HNSW index");
            throw new AgentException(
                $"Failed to create HNSW index: {ex.Message}",
                ErrorCategory.Unknown,
                ex);
        }
    }

    /// <summary>
    /// Creates an IVFFlat index on the vector column for faster similarity searches.
    /// </summary>
    /// <param name="lists">The number of clusters (default: 100).</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CreateIvfflatIndexAsync(
        int lists = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var indexName = $"idx_{_tableName}_{_vectorColumn}_ivfflat";
            var opClass = _distanceFunction switch
            {
                VectorDistanceFunction.Cosine => "vector_cosine_ops",
                VectorDistanceFunction.L2 => "vector_l2_ops",
                VectorDistanceFunction.InnerProduct => "vector_ip_ops",
                _ => "vector_cosine_ops"
            };

            var command = new NpgsqlCommand(
                $@"CREATE INDEX IF NOT EXISTS {indexName}
                   ON {_tableName}
                   USING ivfflat ({_vectorColumn} {opClass})
                   WITH (lists = {lists})",
                connection);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(
                "Created IVFFlat index. Table: {TableName}, Index: {IndexName}, Lists: {Lists}",
                _tableName,
                indexName,
                lists);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to create IVFFlat index");
            throw new AgentException(
                $"Failed to create IVFFlat index: {ex.Message}",
                ErrorCategory.Unknown,
                ex);
        }
    }

    private static string FormatVectorForPgVector(float[] vector)
    {
        // pgvector format: [0.1, 0.2, 0.3, ...]
        return "[" + string.Join(", ", vector.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + "]";
    }

    private static string GetDistanceOperator(VectorDistanceFunction distanceFunction)
    {
        return distanceFunction switch
        {
            VectorDistanceFunction.Cosine => "<=>",
            VectorDistanceFunction.L2 => "<->",
            VectorDistanceFunction.InnerProduct => "<#>",
            _ => "<=>"
        };
    }

    private async Task EnsureExtensionAndTableExistsAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            // Enable pgvector extension
            var extensionCommand = new NpgsqlCommand(
                "CREATE EXTENSION IF NOT EXISTS vector",
                connection);
            await extensionCommand.ExecuteNonQueryAsync().ConfigureAwait(false);

            // Create table if it doesn't exist
            var tableCommand = new NpgsqlCommand(
                $@"CREATE TABLE IF NOT EXISTS {_tableName} (
                    {_idColumn} VARCHAR(255) PRIMARY KEY,
                    {_vectorColumn} VECTOR({_vectorDimensions}) NOT NULL,
                    {_metadataColumn} JSONB,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );

                CREATE INDEX IF NOT EXISTS idx_{_tableName}_created_at ON {_tableName}(created_at);",
                connection);

            await tableCommand.ExecuteNonQueryAsync().ConfigureAwait(false);

            _logger?.LogInformation(
                "Ensured pgvector extension and table exist. Table: {TableName}, Dimensions: {Dimensions}",
                _tableName,
                _vectorDimensions);
        }
        catch (PostgresException ex)
        {
            _logger?.LogError(ex, "Failed to ensure extension and table exist");
            throw new AgentException(
                $"Failed to ensure pgvector extension and table exist: {ex.Message}",
                ErrorCategory.Unknown,
                ex);
        }
    }
}

/// <summary>
/// Distance function for vector similarity search.
/// </summary>
public enum VectorDistanceFunction
{
    /// <summary>
    /// Cosine distance (1 - cosine similarity). Best for normalized vectors.
    /// </summary>
    Cosine,

    /// <summary>
    /// L2 (Euclidean) distance. Best for general purpose similarity.
    /// </summary>
    L2,

    /// <summary>
    /// Inner product (negative dot product). Best for normalized vectors with inner product similarity.
    /// </summary>
    InnerProduct
}
