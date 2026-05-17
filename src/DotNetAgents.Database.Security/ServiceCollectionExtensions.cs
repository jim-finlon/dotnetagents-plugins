using DotNetAgents.Security.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAgents.Database.Security;

/// <summary>
/// Extension methods for registering database security services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds database security services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDatabaseSecurity(this IServiceCollection services)
    {
        services.AddScoped<IDatabaseSecretsManager, DatabaseSecretsManager>();
        services.AddScoped<ISecureConnectionManager, SecureConnectionManager>();
        return services;
    }
}
