using Microsoft.Extensions.DependencyInjection;

namespace DotNetAgents.Database.AI;

/// <summary>
/// Extension methods for registering AI database services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds AI database services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDatabaseAI(this IServiceCollection services)
    {
        services.AddScoped<AIQueryOptimizer>();
        services.AddScoped<AITypeMapper>();
        services.AddScoped<AIProcedureConverter>();
        return services;
    }
}
