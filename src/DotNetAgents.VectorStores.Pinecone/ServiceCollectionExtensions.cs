using DotNetAgents.Abstractions.Retrieval;
using DotNetAgents.Ecosystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.VectorStores.Pinecone;

/// <summary>
/// Extension methods for registering Pinecone vector store in dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Pinecone vector store to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="apiKey">The Pinecone API key.</param>
    /// <param name="indexName">The name of the Pinecone index.</param>
    /// <param name="environment">The Pinecone environment (e.g., "us-east1-gcp").</param>
    /// <param name="configureHttpClient">Optional action to configure the HTTP client.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddPineconeVectorStore(
        this IServiceCollection services,
        string apiKey,
        string indexName,
        string environment,
        Action<HttpClient>? configureHttpClient = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);

        // Register the Pinecone vector store plugin
        services.AddPlugin(new PineconeVectorStorePlugin());

        services.AddHttpClient<PineconeVectorStore>(client =>
        {
            configureHttpClient?.Invoke(client);
        });

        services.AddSingleton<IVectorStore>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(PineconeVectorStore));
            var logger = sp.GetService<ILogger<PineconeVectorStore>>();
            return new PineconeVectorStore(apiKey, indexName, environment, httpClient, logger);
        });

        return services;
    }

    /// <summary>
    /// Adds Pinecone vector store to the service collection using a custom HTTP client.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="apiKey">The Pinecone API key.</param>
    /// <param name="indexName">The name of the Pinecone index.</param>
    /// <param name="environment">The Pinecone environment (e.g., "us-east1-gcp").</param>
    /// <param name="httpClient">The HTTP client to use.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddPineconeVectorStore(
        this IServiceCollection services,
        string apiKey,
        string indexName,
        string environment,
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        ArgumentNullException.ThrowIfNull(httpClient);

        // Register the Pinecone vector store plugin (idempotent)
        services.AddPlugin(new PineconeVectorStorePlugin());

        services.AddSingleton<IVectorStore>(sp =>
        {
            var logger = sp.GetService<ILogger<PineconeVectorStore>>();
            return new PineconeVectorStore(apiKey, indexName, environment, httpClient, logger);
        });

        return services;
    }
}
