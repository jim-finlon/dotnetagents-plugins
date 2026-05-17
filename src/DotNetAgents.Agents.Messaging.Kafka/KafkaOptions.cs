namespace DotNetAgents.Agents.Messaging.Kafka;

/// <summary>
/// Configuration options for Kafka message bus.
/// </summary>
public class KafkaOptions
{
    /// <summary>
    /// Gets or sets the Kafka bootstrap servers (comma-separated list of host:port).
    /// </summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// Gets or sets the topic prefix for agent messages.
    /// </summary>
    public string TopicPrefix { get; set; } = "dotnetagents";

    /// <summary>
    /// Gets or sets the consumer group ID for message consumption.
    /// </summary>
    public string ConsumerGroupId { get; set; } = "dotnetagents-consumers";

    /// <summary>
    /// Gets or sets the number of partitions for topics (default: 3).
    /// </summary>
    public int TopicPartitions { get; set; } = 3;

    /// <summary>
    /// Gets or sets the replication factor for topics (default: 1).
    /// </summary>
    public short TopicReplicationFactor { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether to auto-create topics if they don't exist.
    /// </summary>
    public bool AutoCreateTopics { get; set; } = true;

    /// <summary>
    /// Gets or sets the message retention time in milliseconds (default: 7 days).
    /// </summary>
    public long MessageRetentionMs { get; set; } = 7 * 24 * 60 * 60 * 1000L; // 7 days

    /// <summary>
    /// Gets or sets the producer acks setting (0, 1, or -1/all).
    /// </summary>
    public string ProducerAcks { get; set; } = "all";

    /// <summary>
    /// Gets or sets the consumer auto offset reset (earliest, latest, none).
    /// </summary>
    public string ConsumerAutoOffsetReset { get; set; } = "earliest";

    /// <summary>
    /// Gets or sets whether to enable idempotent producer.
    /// </summary>
    public bool EnableIdempotence { get; set; } = true;

    /// <summary>
    /// Gets or sets additional Kafka configuration properties.
    /// </summary>
    public Dictionary<string, string> AdditionalConfig { get; set; } = new();
}
