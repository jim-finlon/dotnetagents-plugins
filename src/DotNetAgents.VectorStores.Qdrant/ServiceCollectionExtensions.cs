using DotNetAgents.Abstractions.Retrieval;
using DotNetAgents.Ecosystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.VectorStores.Qdrant;

/// <summary>
/// Extension methods for registering Qdrant vector store services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Qdrant vector store to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The Qdrant base URL (e.g., "http://localhost:6333").</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <param name="collectionName">The Qdrant collection name for storing vectors. Default: "documents".</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddQdrantVectorStore(
        this IServiceCollection services,
        string baseUrl,
        string? apiKey = null,
        string collectionName = "documents")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        // Register the Qdrant vector store plugin
        services.AddPlugin(new QdrantVectorStorePlugin());

        services.AddHttpClient<QdrantVectorStore>(client =>
        {
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Add("api-key", apiKey);
            }
        });

        services.AddSingleton<IVectorStore>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(QdrantVectorStore));
            var logger = serviceProvider.GetService<ILogger<QdrantVectorStore>>();

            return new QdrantVectorStore(httpClient, collectionName, logger);
        });

        return services;
    }
}
