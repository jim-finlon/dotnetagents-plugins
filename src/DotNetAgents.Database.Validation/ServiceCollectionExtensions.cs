using Microsoft.Extensions.DependencyInjection;

namespace DotNetAgents.Database.Validation;

/// <summary>
/// Extension methods for registering database validation services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds database validation services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDatabaseValidation(this IServiceCollection services)
    {
        services.AddScoped<IDatabaseValidator, PreFlightValidator>();
        services.AddScoped<PostOperationValidator>();
        return services;
    }
}
