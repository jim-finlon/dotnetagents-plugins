using Microsoft.Extensions.DependencyInjection;

namespace DotNetAgents.Database.Orchestration;

/// <summary>
/// Extension methods for registering database orchestration services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds database orchestration services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDatabaseOrchestration(this IServiceCollection services)
    {
        services.AddScoped<IErrorRecoveryService, ErrorRecoveryService>();
        services.AddScoped<IDatabaseOperationOrchestrator, DatabaseOperationOrchestrator>();
        services.AddSingleton<IDatabaseMetricsCollector, DatabaseMetricsCollector>();
        return services;
    }
}
