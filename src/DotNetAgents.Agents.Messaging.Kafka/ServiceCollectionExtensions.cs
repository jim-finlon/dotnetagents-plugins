using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DotNetAgents.Agents.Messaging.Kafka;

/// <summary>
/// Extension methods for registering Kafka message bus services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Kafka agent message bus to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure Kafka options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKafkaAgentMessageBus(
        this IServiceCollection services,
        Action<KafkaOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the Kafka message bus plugin
        services.AddPlugin(new KafkaMessageBusPlugin());

        if (configure != null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IAgentMessageBus, KafkaAgentMessageBus>();
        return services;
    }

    /// <summary>
    /// Adds the Kafka agent message bus to the service collection with configuration from IConfiguration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configurationSectionName">The name of the configuration section (default: "Kafka").</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKafkaAgentMessageBus(
        this IServiceCollection services,
        string configurationSectionName = "Kafka")
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the Kafka message bus plugin
        services.AddPlugin(new KafkaMessageBusPlugin());

        services.AddOptions<KafkaOptions>()
            .BindConfiguration(configurationSectionName);

        services.TryAddSingleton<IAgentMessageBus, KafkaAgentMessageBus>();
        return services;
    }
}
