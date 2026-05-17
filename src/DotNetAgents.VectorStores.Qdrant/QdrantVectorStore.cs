using DotNetAgents.Abstractions.Exceptions;
using DotNetAgents.Abstractions.Retrieval;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace DotNetAgents.VectorStores.Qdrant;

/// <summary>
/// Qdrant vector store implementation for persistent vector storage and similarity search.
/// </summary>
public class QdrantVectorStore : IVectorStore
{
    private readonly HttpClient _httpClient;
    private readonly string _collectionName;
    private readonly ILogger<QdrantVectorStore>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="QdrantVectorStore"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client configured for Qdrant API.</param>
    /// <param name="collectionName">The Qdrant collection name for storing vectors. Default: "documents".</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when httpClient is null.</exception>
    public QdrantVectorStore(
        HttpClient httpClient,
        string collectionName = "documents",
        ILogger<QdrantVectorStore>? logger = null)
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
            // Prepare payload for Qdrant upsert
            var points = new[]
            {
                new
                {
                    id = id,
                    vector = vector,
                    payload = metadata ?? new Dictionary<string, object>()
                }
            };

            var requestBody = new
            {
                points = points
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync(
                $"/collections/{_collectionName}/points",
                content,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogError(
                    "Failed to upsert vector in Qdrant. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);
                throw new AgentException(
                    $"Failed to upsert vector in Qdrant: {response.StatusCode}",
                    ErrorCategory.Unknown);
            }

            _logger?.LogDebug("Upserted vector in Qdrant. Id: {Id}, Dimension: {Dimension}", id, vector.Length);

            return id;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error upserting vector in Qdrant. Id: {Id}", id);
            throw new AgentException("Failed to upsert vector in Qdrant", ErrorCategory.Unknown, ex);
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
            // Build filter for Qdrant
            object? qdrantFilter = null;
            if (filter != null && filter.Count > 0)
            {
                qdrantFilter = BuildFilter(filter);
            }

            var requestBody = new
            {
                vector = queryVector,
                limit = topK,
                with_payload = true,
                filter = qdrantFilter
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"/collections/{_collectionName}/points/search",
                content,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogError(
                    "Failed to search vectors in Qdrant. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);
                throw new AgentException(
                    $"Failed to search vectors in Qdrant: {response.StatusCode}",
                    ErrorCategory.Unknown);
            }

            var result = await response.Content.ReadFromJsonAsync<QdrantSearchResponse>(
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (result?.Result == null)
            {
                return Array.Empty<VectorSearchResult>();
            }

            var searchResults = new List<VectorSearchResult>();
            foreach (var point in result.Result)
            {
                var pointId = point.Id?.ToString() ?? string.Empty;
                var score = (float)(point.Score ?? 0.0);

                var metadata = new Dictionary<string, object>();
                if (point.Payload != null)
                {
                    foreach (var kvp in point.Payload)
                    {
                        metadata[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
                    }
                }

                searchResults.Add(new VectorSearchResult
                {
                    Id = pointId,
                    Score = score,
                    Metadata = metadata
                });
            }

            _logger?.LogDebug(
                "Searched vectors in Qdrant. Query dimension: {Dimension}, Results: {Count}",
                queryVector.Length,
                searchResults.Count);

            return searchResults;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error searching vectors in Qdrant");
            throw new AgentException("Failed to search vectors in Qdrant", ErrorCategory.Unknown, ex);
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
            // Convert string IDs to appropriate format (Qdrant supports string or integer IDs)
            var pointIds = idList.Select(id => (object)id).ToList();

            var requestBody = new
            {
                points = pointIds
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"/collections/{_collectionName}/points/delete",
                content,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogError(
                    "Failed to delete vectors from Qdrant. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);
                throw new AgentException(
                    $"Failed to delete vectors from Qdrant: {response.StatusCode}",
                    ErrorCategory.Unknown);
            }

            var result = await response.Content.ReadFromJsonAsync<QdrantDeleteResponse>(
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);

            var deletedCount = result?.Result?.Status == "acknowledged" ? idList.Count : 0;

            _logger?.LogDebug("Deleted {Count} vectors from Qdrant", deletedCount);

            return deletedCount;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error deleting vectors from Qdrant");
            throw new AgentException("Failed to delete vectors from Qdrant", ErrorCategory.Unknown, ex);
        }
    }

    private object? BuildFilter(IDictionary<string, object> filter)
    {
        if (filter.Count == 0)
            return null;

        if (filter.Count == 1)
        {
            var kvp = filter.First();
            return new
            {
                must = new[]
                {
                    new
                    {
                        key = kvp.Key,
                        match = new { value = kvp.Value }
                    }
                }
            };
        }

        // Multiple conditions - use "must" with "and"
        var conditions = filter.Select(kvp => new
        {
            key = kvp.Key,
            match = new { value = kvp.Value }
        }).ToArray();

        return new
        {
            must = conditions
        };
    }

    private class QdrantSearchResponse
    {
        public List<QdrantPoint>? Result { get; set; }
    }

    private class QdrantPoint
    {
        public object? Id { get; set; }
        public double? Score { get; set; }
        public Dictionary<string, object?>? Payload { get; set; }
    }

    private class QdrantDeleteResponse
    {
        public QdrantResult? Result { get; set; }
    }

    private class QdrantResult
    {
        public string? Status { get; set; }
    }
}
