using DotNetAgents.Agents.Messaging;
using DotNetAgents.Ecosystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DotNetAgents.Agents.Messaging.SignalR;

/// <summary>
/// Extension methods for registering SignalR message bus services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds SignalR agent message bus to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional action to configure options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSignalRAgentMessageBus(
        this IServiceCollection services,
        Action<SignalRAgentMessageBusOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register the SignalR message bus plugin
        services.AddPlugin(new SignalRMessageBusPlugin());

        if (configure != null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IAgentMessageBus, SignalRAgentMessageBus>();
        return services;
    }

    /// <summary>
    /// Adds SignalR agent message bus to the service collection with explicit options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSignalRAgentMessageBus(
        this IServiceCollection services,
        SignalRAgentMessageBusOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // Register the SignalR message bus plugin
        services.AddPlugin(new SignalRMessageBusPlugin());

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        services.TryAddSingleton<IAgentMessageBus, SignalRAgentMessageBus>();
        return services;
    }


}
