using DotNetAgents.Agents.Messaging;
using DotNetAgents.Ecosystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNetAgents.Agents.Messaging.RabbitMQ;

/// <summary>
/// Extension methods for registering RabbitMQ message bus services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds RabbitMQ message bus to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure RabbitMQ options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRabbitMQMessageBus(
        this IServiceCollection services,
        Action<RabbitMQOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the RabbitMQ message bus plugin
        services.AddPlugin(new RabbitMQMessageBusPlugin());

        if (configure != null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IAgentMessageBus>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMQOptions>>();
            var registry = sp.GetRequiredService<DotNetAgents.Agents.Registry.IAgentRegistry>();
            var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<RabbitMQAgentMessageBus>>();
            return new RabbitMQAgentMessageBus(options, registry, logger);
        });

        return services;
    }
}
