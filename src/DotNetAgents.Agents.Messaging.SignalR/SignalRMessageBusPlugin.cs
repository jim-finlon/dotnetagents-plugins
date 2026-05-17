using DotNetAgents.Ecosystem;
using Microsoft.Extensions.Logging;

namespace DotNetAgents.Agents.Messaging.SignalR;

/// <summary>
/// Plugin for SignalR message bus implementation.
/// </summary>
public class SignalRMessageBusPlugin : PluginBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRMessageBusPlugin"/> class.
    /// </summary>
    public SignalRMessageBusPlugin()
    {
        Metadata = new PluginMetadata
        {
            Id = "messaging-signalr",
            Name = "SignalR Message Bus",
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Description = "Provides SignalR implementation for web-based real-time agent-to-agent messaging. Supports WebSocket connections and hub-based communication.",
            Author = "DotNetAgents",
            License = "MIT",
            Category = "Infrastructure",
            Tags = new List<string> { "messaging", "signalr", "websocket", "message-bus", "infrastructure" },
            Dependencies = new List<string>(),
            RepositoryUrl = "https://github.com/dotnetagents/DotNetAgents",
            DocumentationUrl = "https://github.com/dotnetagents/DotNetAgents/docs/guides/messaging.md"
        };
    }

    /// <inheritdoc />
    protected override Task OnInitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        Logger?.LogInformation(
            "SignalR Message Bus plugin initialized. Use AddSignalRAgentMessageBus() to configure.");

        return Task.CompletedTask;
    }
}
