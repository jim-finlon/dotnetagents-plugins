using DotNetAgents.Abstractions.Retrieval;
using DotNetAgents.Ecosystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.VectorStores.Chroma;

/// <summary>
/// Extension methods for registering Chroma vector store services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Chroma vector store to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The Chroma base URL (e.g., "http://localhost:8000").</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <param name="collectionName">The Chroma collection name for storing vectors. Default: "documents".</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddChromaVectorStore(
        this IServiceCollection services,
        string baseUrl,
        string? apiKey = null,
        string collectionName = "documents")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        // Register the Chroma vector store plugin
        services.AddPlugin(new ChromaVectorStorePlugin());

        services.AddHttpClient<ChromaVectorStore>(client =>
        {
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Add("x-chroma-token", apiKey);
            }
        });

        services.AddSingleton<IVectorStore>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(ChromaVectorStore));
            var logger = serviceProvider.GetService<ILogger<ChromaVectorStore>>();

            return new ChromaVectorStore(httpClient, collectionName, logger);
        });

        return services;
    }
}
