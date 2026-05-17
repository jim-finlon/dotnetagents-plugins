using DotNetAgents.Abstractions.Retrieval;
using DotNetAgents.Ecosystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.VectorStores.PostgreSQL;

/// <summary>
/// Extension methods for registering PostgreSQL vector store in dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL vector store to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="tableName">The table name for storing vectors. Default: "vectors".</param>
    /// <param name="vectorDimensions">The number of dimensions for vectors. Default: 1536 (OpenAI embeddings).</param>
    /// <param name="distanceFunction">The distance function to use for similarity search. Default: Cosine.</param>
    /// <returns>The service collection for method chaining.</returns>
    public static IServiceCollection AddPostgreSQLVectorStore(
        this IServiceCollection services,
        string connectionString,
        string tableName = "vectors",
        int vectorDimensions = 1536,
        VectorDistanceFunction distanceFunction = VectorDistanceFunction.Cosine)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Register the PostgreSQL vector store plugin
        services.AddPlugin(new PostgreSQLVectorStorePlugin());

        services.AddSingleton<IVectorStore>(sp =>
        {
            var logger = sp.GetService<ILogger<PostgreSQLVectorStore>>();
            return new PostgreSQLVectorStore(
                connectionString,
                tableName,
                vectorDimensions,
                distanceFunction,
                logger);
        });

        return services;
    }
}
