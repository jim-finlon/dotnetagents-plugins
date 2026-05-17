namespace DotNetAgents.Agents.Messaging.SignalR;

/// <summary>
/// Configuration options for SignalR agent message bus.
/// </summary>
public class SignalRAgentMessageBusOptions
{
    /// <summary>
    /// Gets or sets the SignalR hub URL.
    /// </summary>
    public string HubUrl { get; set; } = "/hubs/agentmessages";

    /// <summary>
    /// Gets or sets the access token provider function (for authentication).
    /// </summary>
    public Func<Task<string?>>? AccessTokenProvider { get; set; }

    /// <summary>
    /// Gets or sets the automatic reconnect delay.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum number of reconnect attempts.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets whether to skip negotiation (use WebSockets directly).
    /// </summary>
    public bool SkipNegotiation { get; set; } = false;

    /// <summary>
    /// Gets or sets the transport type preference.
    /// </summary>
    public Microsoft.AspNetCore.Http.Connections.HttpTransportType? TransportType { get; set; }
}
