using DotNetAgents.Agents.Messaging;
using DotNetAgents.Ecosystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNetAgents.Agents.Messaging.Redis;

/// <summary>
/// Extension methods for registering Redis Pub/Sub message bus services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Redis Pub/Sub message bus to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure Redis options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRedisPubSubMessageBus(
        this IServiceCollection services,
        Action<RedisOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the Redis message bus plugin
        services.AddPlugin(new RedisMessageBusPlugin());

        if (configure != null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IAgentMessageBus>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisOptions>>();
            var registry = sp.GetRequiredService<DotNetAgents.Agents.Registry.IAgentRegistry>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<RedisPubSubAgentMessageBus>>();
            return new RedisPubSubAgentMessageBus(options, registry, logger);
        });

        return services;
    }
}
