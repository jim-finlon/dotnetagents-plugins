using DotNetAgents.Abstractions.Exceptions;
using DotNetAgents.Abstractions.Retrieval;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace DotNetAgents.VectorStores.Weaviate;

/// <summary>
/// Weaviate vector store implementation for persistent vector storage and similarity search.
/// </summary>
public class WeaviateVectorStore : IVectorStore
{
    private readonly HttpClient _httpClient;
    private readonly string _className;
    private readonly string _vectorProperty;
    private readonly ILogger<WeaviateVectorStore>? _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaviateVectorStore"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client configured for Weaviate API.</param>
    /// <param name="className">The Weaviate class name for storing vectors. Default: "Document".</param>
    /// <param name="vectorProperty">The property name for the vector. Default: "vector".</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when httpClient is null.</exception>
    public WeaviateVectorStore(
        HttpClient httpClient,
        string className = "Document",
        string vectorProperty = "vector",
        ILogger<WeaviateVectorStore>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _className = className ?? throw new ArgumentNullException(nameof(className));
        _vectorProperty = vectorProperty ?? throw new ArgumentNullException(nameof(vectorProperty));
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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
            // Prepare the object payload
            var weaviateObject = new
            {
                @class = _className,
                id = id,
                properties = metadata ?? new Dictionary<string, object>(),
                vector = vector
            };

            var json = JsonSerializer.Serialize(weaviateObject, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Upsert using Weaviate's batch API or object API
            var response = await _httpClient.PutAsync(
                $"/v1/objects/{id}",
                content,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogError(
                    "Failed to upsert vector in Weaviate. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);
                throw new AgentException(
                    $"Failed to upsert vector in Weaviate: {response.StatusCode}",
                    ErrorCategory.Unknown);
            }

            _logger?.LogDebug("Upserted vector in Weaviate. Id: {Id}, Dimension: {Dimension}", id, vector.Length);

            return id;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error upserting vector in Weaviate. Id: {Id}", id);
            throw new AgentException("Failed to upsert vector in Weaviate", ErrorCategory.Unknown, ex);
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
            // Build GraphQL query for Weaviate
            var whereClause = BuildWhereClause(filter);
            var query = $@"
{{
  Get {{
    {_className}(
      limit: {topK}
      nearVector: {{
        vector: {JsonSerializer.Serialize(queryVector)}
      }}
      {whereClause}
    ) {{
      _additional {{
        id
        distance
      }}
      {BuildPropertiesQuery(metadata: filter)}
    }}
  }}
}}";

            var requestBody = new
            {
                query = query
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                "/v1/graphql",
                content,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger?.LogError(
                    "Failed to search vectors in Weaviate. Status: {StatusCode}, Error: {Error}",
                    response.StatusCode,
                    errorContent);
                throw new AgentException(
                    $"Failed to search vectors in Weaviate: {response.StatusCode}",
                    ErrorCategory.Unknown);
            }

            var result = await response.Content.ReadFromJsonAsync<WeaviateGraphQLResponse>(
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (result?.Data?.Get == null)
            {
                return Array.Empty<VectorSearchResult>();
            }

            var searchResults = new List<VectorSearchResult>();
            foreach (var item in result.Data.Get)
            {
                var itemId = item.Additional?.Id ?? string.Empty;
                var distance = item.Additional?.Distance ?? 0.0;
                var score = (float)(1.0 - distance); // Convert distance to similarity score

                var metadata = new Dictionary<string, object>();
                if (item.Properties != null)
                {
                    foreach (var prop in item.Properties)
                    {
                        metadata[prop.Key] = prop.Value ?? string.Empty;
                    }
                }

                searchResults.Add(new VectorSearchResult
                {
                    Id = itemId,
                    Score = score,
                    Metadata = metadata
                });
            }

            _logger?.LogDebug(
                "Searched vectors in Weaviate. Query dimension: {Dimension}, Results: {Count}",
                queryVector.Length,
                searchResults.Count);

            return searchResults;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error searching vectors in Weaviate");
            throw new AgentException("Failed to search vectors in Weaviate", ErrorCategory.Unknown, ex);
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
            var deletedCount = 0;
            foreach (var id in idList)
            {
                var response = await _httpClient.DeleteAsync(
                    $"/v1/objects/{id}",
                    cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    deletedCount++;
                }
                else
                {
                    _logger?.LogWarning(
                        "Failed to delete vector in Weaviate. Id: {Id}, Status: {StatusCode}",
                        id,
                        response.StatusCode);
                }
            }

            _logger?.LogDebug("Deleted {Count} vectors from Weaviate", deletedCount);

            return deletedCount;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP error deleting vectors from Weaviate");
            throw new AgentException("Failed to delete vectors from Weaviate", ErrorCategory.Unknown, ex);
        }
    }

    private string BuildWhereClause(IDictionary<string, object>? filter)
    {
        if (filter == null || filter.Count == 0)
            return string.Empty;

        var conditions = new List<string>();
        foreach (var kvp in filter)
        {
            var value = kvp.Value is string str
                ? $"\"{str}\""
                : kvp.Value?.ToString() ?? "null";
            conditions.Add($"{kvp.Key}: {{value: {value}}}");
        }

        return conditions.Count > 0
            ? $"where: {{operator: And, operands: [{string.Join(", ", conditions.Select(c => $"{{path: [{c}]}}"))}]}}"
            : string.Empty;
    }

    private string BuildPropertiesQuery(IDictionary<string, object>? metadata)
    {
        if (metadata == null || metadata.Count == 0)
            return string.Empty;

        return string.Join("\n      ", metadata.Keys);
    }

    private class WeaviateGraphQLResponse
    {
        public WeaviateData? Data { get; set; }
    }

    private class WeaviateData
    {
        public List<WeaviateObject>? Get { get; set; }
    }

    private class WeaviateObject
    {
        public WeaviateAdditional? Additional { get; set; }
        public Dictionary<string, object?>? Properties { get; set; }
    }

    private class WeaviateAdditional
    {
        public string? Id { get; set; }
        public double? Distance { get; set; }
    }
}
