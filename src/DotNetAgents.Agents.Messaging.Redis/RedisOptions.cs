namespace DotNetAgents.Agents.Messaging.Redis;

/// <summary>
/// Configuration options for Redis Pub/Sub message bus.
/// </summary>
public class RedisOptions
{
    /// <summary>
    /// Gets or sets the Redis connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the channel prefix for agent messages.
    /// </summary>
    public string ChannelPrefix { get; set; } = "dotnetagents:agent:";

    /// <summary>
    /// Gets or sets the broadcast channel name.
    /// </summary>
    public string BroadcastChannel { get; set; } = "dotnetagents:broadcast";

    /// <summary>
    /// Gets or sets the message type channel prefix.
    /// </summary>
    public string MessageTypeChannelPrefix { get; set; } = "dotnetagents:type:";
}
