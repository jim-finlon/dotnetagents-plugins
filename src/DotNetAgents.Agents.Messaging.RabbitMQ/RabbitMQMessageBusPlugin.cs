using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.Agents.Messaging.RabbitMQ;

/// <summary>
/// Plugin for RabbitMQ message bus implementation.
/// </summary>
public class RabbitMQMessageBusPlugin : PluginBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQMessageBusPlugin"/> class.
    /// </summary>
    public RabbitMQMessageBusPlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "messaging-rabbitmq",
            Name = "RabbitMQ Message Bus",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides RabbitMQ implementation for agent-to-agent messaging. Supports guaranteed message delivery, queues, and pub/sub patterns.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "Infrastructure",
            Tags = new List<string> { "messaging", "rabbitmq", "message-bus", "infrastructure" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/messaging.md"
        };
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation(
            "RabbitMQ Message Bus plugin initialized. Use AddRabbitMQMessageBus() to configure.");

        return Task.CompletedTask;
    }
}
