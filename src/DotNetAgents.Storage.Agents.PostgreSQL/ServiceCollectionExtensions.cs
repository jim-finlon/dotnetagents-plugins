using DotNetAgents.Agents.Registry;
using DotNetAgents.Agents.Tasks;
using DotNetAgents.Ecosystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNetAgents.Storage.Agents.PostgreSQL;

/// <summary>
/// Extension methods for registering PostgreSQL agent storage services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds PostgreSQL agent registry to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgreSQLAgentRegistry(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        // Register the PostgreSQL agent storage plugin
        services.AddPlugin(new PostgreSQLAgentStoragePlugin());

        services.TryAddSingleton<IAgentRegistry>(sp =>
        {
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<PostgreSQLAgentRegistry>>();
            return new PostgreSQLAgentRegistry(connectionString, logger);
        });

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL task queue and store to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgreSQLTaskQueue(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        // Register the PostgreSQL agent storage plugin (idempotent)
        services.AddPlugin(new PostgreSQLAgentStoragePlugin());

        services.TryAddSingleton<ITaskQueue>(sp =>
        {
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<PostgreSQLTaskQueue>>();
            return new PostgreSQLTaskQueue(connectionString, logger);
        });

        services.TryAddSingleton<ITaskStore>(sp =>
        {
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<PostgreSQLTaskStore>>();
            return new PostgreSQLTaskStore(connectionString, logger);
        });

        return services;
    }
}
