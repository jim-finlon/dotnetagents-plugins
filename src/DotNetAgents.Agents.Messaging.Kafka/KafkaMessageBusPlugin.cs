using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.Agents.Messaging.Kafka;

/// <summary>
/// Plugin for Kafka message bus implementation.
/// </summary>
public class KafkaMessageBusPlugin : PluginBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaMessageBusPlugin"/> class.
    /// </summary>
    public KafkaMessageBusPlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "messaging-kafka",
            Name = "Kafka Message Bus",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides Apache Kafka implementation for high-throughput agent-to-agent messaging. Supports distributed messaging, partitioning, and consumer groups.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "Infrastructure",
            Tags = new List<string> { "messaging", "kafka", "message-bus", "infrastructure" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/messaging.md"
        };
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation(
            "Kafka Message Bus plugin initialized. Use AddKafkaAgentMessageBus() to configure.");

        return Task.CompletedTask;
    }
}
