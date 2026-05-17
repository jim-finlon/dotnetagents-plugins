using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.Agents.Messaging.Redis;

/// <summary>
/// Plugin for Redis Pub/Sub message bus implementation.
/// </summary>
public class RedisMessageBusPlugin : PluginBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RedisMessageBusPlugin"/> class.
    /// </summary>
    public RedisMessageBusPlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "messaging-redis",
            Name = "Redis Pub/Sub Message Bus",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides Redis Pub/Sub implementation for real-time agent-to-agent messaging. Supports publish/subscribe patterns and channels.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "Infrastructure",
            Tags = new List<string> { "messaging", "redis", "pubsub", "message-bus", "infrastructure" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/messaging.md"
        };
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation(
            "Redis Pub/Sub Message Bus plugin initialized. Use AddRedisPubSubMessageBus() to configure.");

        return Task.CompletedTask;
    }
}
