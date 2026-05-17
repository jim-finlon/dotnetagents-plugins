namespace DotNetAgents.Agents.Messaging.RabbitMQ;

/// <summary>
/// Configuration options for RabbitMQ message bus.
/// </summary>
public class RabbitMQOptions
{
    /// <summary>
    /// Gets or sets the RabbitMQ host name.
    /// </summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    /// Gets or sets the RabbitMQ port.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// Gets or sets the username for authentication.
    /// </summary>
    public string UserName { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the password for authentication.
    /// </summary>
    public string Password { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the virtual host.
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Gets or sets the exchange name for agent messages.
    /// </summary>
    public string ExchangeName { get; set; } = "dotnetagents.messages";

    /// <summary>
    /// Gets or sets the queue name prefix.
    /// </summary>
    public string QueuePrefix { get; set; } = "dotnetagents.agent";

    /// <summary>
    /// Gets or sets a value indicating whether the exchange should be durable.
    /// </summary>
    public bool DurableExchange { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether queues should be durable.
    /// </summary>
    public bool DurableQueues { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether queues should be auto-deleted when not in use.
    /// </summary>
    public bool AutoDeleteQueues { get; set; } = false;
}
