using DotNetAgents.Abstractions.Exceptions;
using DotNetAgents.Abstractions.Retrieval;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetAgents.VectorStores.Pinecone;

/// <summary>
/// Pinecone vector store implementation for persistent vector storage and similarity search.
/// </summary>
public class PineconeVectorStore : IVectorStore
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _indexName;
    private readonly string _baseUrl;
    private readonly string _environment;
    private readonly ILogger<PineconeVectorStore>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PineconeVectorStore"/> class.
    /// </summary>
    /// <param name="apiKey">The Pinecone API key.</param>
    /// <param name="indexName">The name of the Pinecone index.</param>
    /// <param name="environment">The Pinecone environment (e.g., "us-east1-gcp").</param>
    /// <param name="httpClient">Optional HTTP client. If null, a new client is created.</param>
    /// <param name="logger">Optional logger for tracking operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public PineconeVectorStore(
        string apiKey,
        string indexName,
        string environment,
        HttpClient? httpClient = null,
        ILogger<PineconeVectorStore>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _indexName = indexName ?? throw new ArgumentNullException(nameof(indexName));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger;

        _baseUrl = $"https://{indexName}-{environment}.svc.pinecone.io";
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Api-Key", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("Content-Type", "application/json");
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
            var request = new PineconeUpsertRequest
            {
                Vectors = new[]
                {
                    new PineconeVector
                    {
                        Id = id,
                        Values = vector,
                        Metadata = metadata
                    }
                }
            };

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/vectors/upsert",
                content,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            _logger?.LogDebug("Upserted vector. Id: {Id}, Dimension: {Dimension}", id, vector.Length);

            return id;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "Failed to upsert vector. Id: {Id}", id);
            throw new AgentException(
                $"Failed to upsert vector to Pinecone: {ex.Message}",
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
        if (topK <= 0)
            throw new ArgumentException("TopK must be positive.", nameof(topK));

        try
        {
            var request = new PineconeQueryRequest
            {
                Vector = queryVector,
                TopK = topK,
                IncludeMetadata = true,
                Filter = filter != null ? ConvertFilter(filter) : null
            };

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/query",
                content,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var queryResponse = await response.Content.ReadFromJsonAsync<PineconeQueryResponse>(
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (queryResponse?.Matches == null)
            {
                return Array.Empty<VectorSearchResult>();
            }

            var results = queryResponse.Matches.Select(match => new VectorSearchResult
            {
                Id = match.Id ?? string.Empty,
                Score = match.Score ?? 0.0f,
                Metadata = match.Metadata.HasValue
                    ? ConvertMetadata(match.Metadata.Value)
                    : new Dictionary<string, object>()
            }).ToList();

            _logger?.LogDebug(
                "Pinecone search completed. Query dimension: {Dimension}, Results: {Count}",
                queryVector.Length,
                results.Count);

            return results;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "Failed to search vectors in Pinecone");
            throw new AgentException(
                $"Failed to search vectors in Pinecone: {ex.Message}",
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
            var request = new PineconeDeleteRequest
            {
                Ids = idList.ToArray()
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/vectors/delete",
                content,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            _logger?.LogDebug("Deleted {Count} vectors", idList.Count);

            return idList.Count;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "Failed to delete vectors");
            throw new AgentException(
                $"Failed to delete vectors from Pinecone: {ex.Message}",
                ErrorCategory.Unknown,
                ex);
        }
    }

    private static JsonElement? ConvertFilter(IDictionary<string, object> filter)
    {
        // Convert metadata filter to Pinecone filter format
        // Pinecone uses MongoDB-style filters
        var filterDict = new Dictionary<string, object>();

        foreach (var kvp in filter)
        {
            filterDict[$"metadata.{kvp.Key}"] = kvp.Value;
        }

        return JsonSerializer.SerializeToElement(filterDict);
    }

    private static IDictionary<string, object> ConvertMetadata(JsonElement metadata)
    {
        var result = new Dictionary<string, object>();

        if (metadata.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in metadata.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString()!,
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null!,
                    _ => prop.Value.ToString()
                };
            }
        }

        return result;
    }

    private record PineconeUpsertRequest
    {
        [JsonPropertyName("vectors")]
        public PineconeVector[]? Vectors { get; init; }
    }

    private record PineconeVector
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("values")]
        public float[]? Values { get; init; }

        [JsonPropertyName("metadata")]
        public IDictionary<string, object>? Metadata { get; init; }
    }

    private record PineconeQueryRequest
    {
        [JsonPropertyName("vector")]
        public float[]? Vector { get; init; }

        [JsonPropertyName("topK")]
        public int TopK { get; init; }

        [JsonPropertyName("includeMetadata")]
        public bool IncludeMetadata { get; init; }

        [JsonPropertyName("filter")]
        public JsonElement? Filter { get; init; }
    }

    private record PineconeQueryResponse
    {
        [JsonPropertyName("matches")]
        public PineconeMatch[]? Matches { get; init; }
    }

    private record PineconeMatch
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("score")]
        public float? Score { get; init; }

        [JsonPropertyName("metadata")]
        public JsonElement? Metadata { get; init; }
    }

    private record PineconeDeleteRequest
    {
        [JsonPropertyName("ids")]
        public string[]? Ids { get; init; }
    }

}
