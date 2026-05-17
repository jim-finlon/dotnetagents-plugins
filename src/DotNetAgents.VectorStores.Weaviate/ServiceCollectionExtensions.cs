using DotNetAgents.Abstractions.Retrieval;
using DotNetAgents.Ecosystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.VectorStores.Weaviate;

/// <summary>
/// Extension methods for registering Weaviate vector store services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Weaviate vector store to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="baseUrl">The Weaviate base URL (e.g., "http://localhost:8080").</param>
    /// <param name="apiKey">Optional API key for authentication.</param>
    /// <param name="className">The Weaviate class name for storing vectors. Default: "Document".</param>
    /// <param name="vectorProperty">The property name for the vector. Default: "vector".</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddWeaviateVectorStore(
        this IServiceCollection services,
        string baseUrl,
        string? apiKey = null,
        string className = "Document",
        string vectorProperty = "vector")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        // Register the Weaviate vector store plugin
        services.AddPlugin(new WeaviateVectorStorePlugin());

        services.AddHttpClient<WeaviateVectorStore>(client =>
        {
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            }
        });

        services.AddSingleton<IVectorStore>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(WeaviateVectorStore));
            var logger = serviceProvider.GetService<ILogger<WeaviateVectorStore>>();

            return new WeaviateVectorStore(httpClient, className, vectorProperty, logger);
        });

        return services;
    }
}
