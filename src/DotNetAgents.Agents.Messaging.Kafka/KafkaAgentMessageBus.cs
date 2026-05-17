using Confluent.Kafka;
using DotNetAgents.Agents.Messaging;
using DotNetAgents.Agents.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace DotNetAgents.Agents.Messaging.Kafka;

/// <summary>
/// Kafka-based implementation of <see cref="IAgentMessageBus"/>.
/// Suitable for high-throughput, distributed deployments.
/// </summary>
public class KafkaAgentMessageBus : IAgentMessageBus, IDisposable
{
    private readonly IAgentRegistry _agentRegistry;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaAgentMessageBus>? _logger;
    private readonly IProducer<string, byte[]> _producer;
    private readonly ConcurrentDictionary<string, IConsumer<string, byte[]>> _consumers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _consumerCancellationSources = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaAgentMessageBus"/> class.
    /// </summary>
    /// <param name="agentRegistry">The agent registry.</param>
    /// <param name="options">Kafka configuration options.</param>
    /// <param name="logger">Optional logger instance.</param>
    public KafkaAgentMessageBus(
        IAgentRegistry agentRegistry,
        IOptions<KafkaOptions> options,
        ILogger<KafkaAgentMessageBus>? logger = null)
    {
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = _options.ProducerAcks switch
            {
                "0" => Acks.None,
                "1" => Acks.Leader,
                "all" or "-1" => Acks.All,
                _ => Acks.All
            },
            EnableIdempotence = _options.EnableIdempotence
        };

        // Merge additional config
        foreach (var kvp in _options.AdditionalConfig)
        {
            producerConfig.Set(kvp.Key, kvp.Value);
        }

        _producer = new ProducerBuilder<string, byte[]>(producerConfig)
            .SetKeySerializer(Serializers.Utf8)
            .SetValueSerializer(Serializers.ByteArray)
            .Build();

        _logger?.LogInformation(
            "KafkaAgentMessageBus initialized with bootstrap servers: {BootstrapServers}",
            _options.BootstrapServers);
    }

    /// <inheritdoc />
    public async Task<MessageSendResult> SendAsync(
        AgentMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(message.ToAgentId))
        {
            return MessageSendResult.FailureResult(
                message.MessageId,
                "ToAgentId cannot be empty for direct send.");
        }

        if (message.ToAgentId == "*")
        {
            return await BroadcastAsync(message, null, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var topic = GetTopicForAgent(message.ToAgentId);
            var key = message.CorrelationId ?? message.MessageId;
            var value = KafkaMessageSerializer.Serialize(message);

            var deliveryResult = await _producer.ProduceAsync(
                topic,
                new Message<string, byte[]>
                {
                    Key = key,
                    Value = value,
                    Headers = ConvertHeaders(message.Headers)
                },
                cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug(
                "Sent message {MessageId} to Kafka topic {Topic} (partition {Partition}, offset {Offset})",
                message.MessageId,
                topic,
                deliveryResult.Partition,
                deliveryResult.Offset);

            return MessageSendResult.SuccessResult(message.MessageId);
        }
        catch (ProduceException<string, byte[]> ex)
        {
            _logger?.LogError(
                ex,
                "Failed to send message {MessageId} to Kafka",
                message.MessageId);
            return MessageSendResult.FailureResult(message.MessageId, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<MessageSendResult> BroadcastAsync(
        AgentMessage message,
        Func<AgentInfo, bool>? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var allAgents = await _agentRegistry.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var targetAgents = filter != null
                ? allAgents.Where(filter).ToList()
                : allAgents.ToList();

            var topic = GetBroadcastTopic();
            var key = message.CorrelationId ?? message.MessageId;
            var value = KafkaMessageSerializer.Serialize(message);

            var deliveryResult = await _producer.ProduceAsync(
                topic,
                new Message<string, byte[]>
                {
                    Key = key,
                    Value = value,
                    Headers = ConvertHeaders(message.Headers)
                },
                cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug(
                "Broadcast message {MessageId} to Kafka topic {Topic} (targeting {Count} agents)",
                message.MessageId,
                topic,
                targetAgents.Count);

            return MessageSendResult.SuccessResult(message.MessageId);
        }
        catch (ProduceException<string, byte[]> ex)
        {
            _logger?.LogError(
                ex,
                "Failed to broadcast message {MessageId} to Kafka",
                message.MessageId);
            return MessageSendResult.FailureResult(message.MessageId, ex.Message);
        }
    }

    /// <inheritdoc />
    public Task<IDisposable> SubscribeAsync(
        string agentId,
        Func<AgentMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);
        ArgumentNullException.ThrowIfNull(handler);
        cancellationToken.ThrowIfCancellationRequested();

        var topic = GetTopicForAgent(agentId);
        return SubscribeToTopicAsync(topic, agentId, handler, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IDisposable> SubscribeByTypeAsync(
        string messageType,
        Func<AgentMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageType);
        ArgumentNullException.ThrowIfNull(handler);
        cancellationToken.ThrowIfCancellationRequested();

        var topic = GetTopicForMessageType(messageType);
        return SubscribeToTopicAsync(topic, messageType, handler, cancellationToken);
    }

    private async Task<IDisposable> SubscribeToTopicAsync(
        string topic,
        string subscriptionKey,
        Func<AgentMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        // Ensure topic exists
        await EnsureTopicExistsAsync(topic).ConfigureAwait(false);

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = _options.ConsumerAutoOffsetReset switch
            {
                "earliest" => AutoOffsetReset.Earliest,
                "latest" => AutoOffsetReset.Latest,
                "none" => AutoOffsetReset.Error,
                _ => AutoOffsetReset.Earliest
            },
            EnableAutoCommit = true
        };

        // Merge additional config
        foreach (var kvp in _options.AdditionalConfig)
        {
            consumerConfig.Set(kvp.Key, kvp.Value);
        }

        var consumer = new ConsumerBuilder<string, byte[]>(consumerConfig)
            .SetKeyDeserializer(Deserializers.Utf8)
            .SetValueDeserializer(Deserializers.ByteArray)
            .Build();

        var cts = new CancellationTokenSource();
        _consumers[subscriptionKey] = consumer;
        _consumerCancellationSources[subscriptionKey] = cts;

        consumer.Subscribe(topic);

        // Start background consumption
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var result = consumer.Consume(cts.Token);
                        if (result?.Message?.Value != null)
                        {
                            var message = KafkaMessageSerializer.Deserialize(result.Message.Value);
                            if (message != null)
                            {
                                await handler(message, cts.Token).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (ConsumeException ex)
                    {
                        _logger?.LogError(ex, "Error consuming message from Kafka topic {Topic}", topic);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            finally
            {
                consumer.Close();
            }
        }, cts.Token);

        _logger?.LogDebug("Subscribed to Kafka topic {Topic} for {SubscriptionKey}", topic, subscriptionKey);

        return new KafkaSubscriptionDisposable(subscriptionKey, this);
    }

    private async Task EnsureTopicExistsAsync(string topic)
    {
        // In a production system, you'd use AdminClient to create topics
        // For now, we'll rely on auto-creation if enabled
        if (!_options.AutoCreateTopics)
        {
            _logger?.LogWarning(
                "Auto-create topics is disabled. Ensure topic {Topic} exists in Kafka.",
                topic);
        }
    }

    private string GetTopicForAgent(string agentId)
    {
        return $"{_options.TopicPrefix}-agent-{agentId}";
    }

    private string GetTopicForMessageType(string messageType)
    {
        return $"{_options.TopicPrefix}-type-{messageType}";
    }

    private string GetBroadcastTopic()
    {
        return $"{_options.TopicPrefix}-broadcast";
    }

    private Headers ConvertHeaders(Dictionary<string, string> headers)
    {
        var kafkaHeaders = new Headers();
        foreach (var kvp in headers)
        {
            kafkaHeaders.Add(kvp.Key, System.Text.Encoding.UTF8.GetBytes(kvp.Value));
        }
        return kafkaHeaders;
    }

    internal void Unsubscribe(string subscriptionKey)
    {
        if (_consumerCancellationSources.TryRemove(subscriptionKey, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        if (_consumers.TryRemove(subscriptionKey, out var consumer))
        {
            consumer.Close();
            consumer.Dispose();
        }

        _logger?.LogDebug("Unsubscribed from Kafka subscription {SubscriptionKey}", subscriptionKey);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            if (_disposed)
                return;

            // Cancel all subscriptions
            foreach (var cts in _consumerCancellationSources.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            // Close all consumers
            foreach (var consumer in _consumers.Values)
            {
                consumer.Close();
                consumer.Dispose();
            }

            _producer.Flush(TimeSpan.FromSeconds(10));
            _producer.Dispose();

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private sealed class KafkaSubscriptionDisposable : IDisposable
    {
        private readonly string _subscriptionKey;
        private readonly KafkaAgentMessageBus _bus;
        private bool _disposed;

        public KafkaSubscriptionDisposable(string subscriptionKey, KafkaAgentMessageBus bus)
        {
            _subscriptionKey = subscriptionKey;
            _bus = bus;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _bus.Unsubscribe(_subscriptionKey);
                _disposed = true;
            }
        }
    }
}
