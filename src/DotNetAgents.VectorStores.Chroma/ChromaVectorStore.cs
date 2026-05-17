using DotNetAgents.Abstractions.Exceptions;
using DotNetAgents.Abstractions.Retrieval;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace DotNetAgents.VectorStores.Chroma;

/// <summary>
/// Chroma vector store implementation for persistent vector storage and similarity search.
/// </summary>
public class ChromaVectorStore : IVectorStore
{
    private readonly HttpClient _httpClient;
    private readonly string _collectionName;
    private readonly ILogger<ChromaVectorStore>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromaVectorStore"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client configured for Chroma API.</param>
    /// <param name="collectionName">The Chroma collection name for storing vectors. Default: "documents".</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when httpClient is null.</exception>
    public ChromaVectorStore(
        HttpClient httpClient,
        string collectionName = "documents",
        ILogger<ChromaVectorStore>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _collectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
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

        try
        {
            // Ensure collection exists
            await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);

            // Prepare payload for Chroma upsert
            var requestBody = new
            {
                ids = new[] { id },
                embeddings = new[] { vector },
                metadatas = metadata != null ? new[] { metadata } : null,
                documents = metadata?.ContainsKey("text") == true
                    ? new[] { metadata["text"]?.ToString() }
                    : null
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"/api/v1/collections/{_collectionName}/upsert",
                content,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogError(
                    "Failed to upsert vector in Chroma. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);
                throw new AgentException(
                    $"Failed to upsert vector in Chroma: {response.StatusCode}",
                    ErrorCategory.Unknown);
            }

            _logger?.LogDebug("Upserted vector in Chroma. Id: {Id}, Dimension: {Dimension}", id, vector.Length);

            return id;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error upserting vector in Chroma. Id: {Id}", id);
            throw new AgentException("Failed to upsert vector in Chroma", ErrorCategory.Unknown, ex);
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
        if (topK <= 0)
            throw new ArgumentException("TopK must be positive.", nameof(topK));

        try
        {
            // Build where clause for Chroma filter
            object? whereClause = null;
            if (filter != null && filter.Count > 0)
            {
                whereClause = BuildWhereClause(filter);
            }

            var requestBody = new
            {
                query_embeddings = new[] { queryVector },
                n_results = topK,
                where = whereClause,
                include = new[] { "metadatas", "documents", "distances" }
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"/api/v1/collections/{_collectionName}/query",
                content,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogError(
                    "Failed to search vectors in Chroma. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);
                throw new AgentException(
                    $"Failed to search vectors in Chroma: {response.StatusCode}",
                    ErrorCategory.Unknown);
            }

            var result = await response.Content.ReadFromJsonAsync<ChromaQueryResponse>(
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (result?.Ids == null || result.Ids.Count == 0)
            {
                return Array.Empty<VectorSearchResult>();
            }

            var searchResults = new List<VectorSearchResult>();
            var ids = result.Ids.FirstOrDefault() ?? new List<string>();
            var distances = result.Distances?.FirstOrDefault() ?? new List<double>();
            var metadatas = result.Metadatas?.FirstOrDefault() ?? new List<Dictionary<string, object?>>();

            for (int i = 0; i < ids.Count && i < distances.Count; i++)
            {
                var id = ids[i];
                var distance = distances[i];
                var score = (float)(1.0 - distance); // Convert distance to similarity score

                var metadata = new Dictionary<string, object>();
                if (i < metadatas.Count && metadatas[i] != null)
                {
                    foreach (var kvp in metadatas[i])
                    {
                        metadata[kvp.Key] = kvp.Value ?? string.Empty;
                    }
                }

                searchResults.Add(new VectorSearchResult
                {
                    Id = id,
                    Score = score,
                    Metadata = metadata
                });
            }

            _logger?.LogDebug(
                "Searched vectors in Chroma. Query dimension: {Dimension}, Results: {Count}",
                queryVector.Length,
                searchResults.Count);

            return searchResults;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error searching vectors in Chroma");
            throw new AgentException("Failed to search vectors in Chroma", ErrorCategory.Unknown, ex);
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
            var requestBody = new
            {
                ids = idList
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"/api/v1/collections/{_collectionName}/delete",
                content,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogError(
                    "Failed to delete vectors from Chroma. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);
                throw new AgentException(
                    $"Failed to delete vectors from Chroma: {response.StatusCode}",
                    ErrorCategory.Unknown);
            }

            _logger?.LogDebug("Deleted {Count} vectors from Chroma", idList.Count);

            return idList.Count;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error deleting vectors from Chroma");
            throw new AgentException("Failed to delete vectors from Chroma", ErrorCategory.Unknown, ex);
        }
    }

    private async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Check if collection exists
            var getResponse = await _httpClient.GetAsync(
                $"/api/v1/collections/{_collectionName}",
                cancellationToken).ConfigureAwait(false);

            if (getResponse.IsSuccessStatusCode)
            {
                return; // Collection exists
            }

            // Create collection if it doesn't exist
            var createBody = new
            {
                name = _collectionName
            };

            var json = JsonSerializer.Serialize(createBody, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var createResponse = await _httpClient.PostAsync(
                "/api/v1/collections",
                content,
                cancellationToken).ConfigureAwait(false);

            if (!createResponse.IsSuccessStatusCode)
            {
                var errorContent = await createResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogWarning(
                    "Failed to create Chroma collection. Status: {StatusCode}, Error: {Error}",
                    createResponse.StatusCode,
                    errorContent);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogWarning(ex, "Error ensuring Chroma collection exists");
            // Don't throw - collection might already exist or be created elsewhere
        }
    }

    private object? BuildWhereClause(IDictionary<string, object> filter)
    {
        if (filter.Count == 0)
            return null;

        var conditions = new Dictionary<string, object>();
        foreach (var kvp in filter)
        {
            conditions[kvp.Key] = kvp.Value;
        }

        return conditions;
    }

    private class ChromaQueryResponse
    {
        public List<List<string>>? Ids { get; set; }
        public List<List<double>>? Distances { get; set; }
        public List<List<Dictionary<string, object?>>>? Metadatas { get; set; }
    }
}
