using DotNetAgents.Agents.Messaging;
using DotNetAgents.Agents.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;

namespace DotNetAgents.Agents.Messaging.RabbitMQ;

/// <summary>
/// RabbitMQ implementation of <see cref="IAgentMessageBus"/>.
/// Suitable for distributed deployments requiring reliable message delivery.
/// </summary>
public class RabbitMQAgentMessageBus : IAgentMessageBus, IDisposable
{
    private readonly RabbitMQOptions _options;
    private readonly IAgentRegistry _agentRegistry;
    private readonly ILogger<RabbitMQAgentMessageBus>? _logger;
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ConcurrentDictionary<string, string> _queueNames = new();
    private readonly ConcurrentDictionary<string, List<IDisposable>> _subscriptions = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMQAgentMessageBus"/> class.
    /// </summary>
    /// <param name="options">RabbitMQ configuration options.</param>
    /// <param name="agentRegistry">The agent registry for finding agents.</param>
    /// <param name="logger">Optional logger instance.</param>
    public RabbitMQAgentMessageBus(
        IOptions<RabbitMQOptions> options,
        IAgentRegistry agentRegistry,
        ILogger<RabbitMQAgentMessageBus>? logger = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        // Declare exchange
        _channel.ExchangeDeclare(
            _options.ExchangeName,
            ExchangeType.Topic,
            durable: _options.DurableExchange,
            autoDelete: false);

        _logger?.LogInformation("RabbitMQ message bus initialized. Exchange: {ExchangeName}", _options.ExchangeName);
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
            return MessageSendResult.FailureResult(message.MessageId, "ToAgentId cannot be empty");
        }

        if (message.TimeToLive.HasValue &&
            (DateTimeOffset.UtcNow - message.Timestamp).TotalMilliseconds > message.TimeToLive.Value.TotalMilliseconds)
        {
            return MessageSendResult.FailureResult(message.MessageId, "Message has expired");
        }

        try
        {
            var routingKey = message.ToAgentId == "*" ? "broadcast" : $"agent.{message.ToAgentId}";
            var body = RabbitMQMessageSerializer.Serialize(message);

            var properties = _channel.CreateBasicProperties();
            properties.MessageId = message.MessageId;
            properties.Timestamp = new AmqpTimestamp(message.Timestamp.ToUnixTimeSeconds());
            properties.Type = message.MessageType;
            properties.CorrelationId = message.CorrelationId;
            properties.Headers = new Dictionary<string, object>();
            foreach (var header in message.Headers)
            {
                properties.Headers[header.Key] = header.Value;
            }

            if (message.TimeToLive.HasValue)
            {
                properties.Expiration = ((long)message.TimeToLive.Value.TotalMilliseconds).ToString();
            }

            _channel.BasicPublish(
                exchange: _options.ExchangeName,
                routingKey: routingKey,
                basicProperties: properties,
                body: body);

            _logger?.LogDebug(
                "Sent message {MessageId} to {ToAgentId} via RabbitMQ",
                message.MessageId,
                message.ToAgentId);

            return MessageSendResult.SuccessResult(message.MessageId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send message {MessageId} via RabbitMQ", message.MessageId);
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

        var agents = await _agentRegistry.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var targetAgents = filter != null
            ? agents.Where(filter).ToList()
            : agents.ToList();

        if (targetAgents.Count == 0)
        {
            return MessageSendResult.SuccessResult(message.MessageId);
        }

        var results = new List<MessageSendResult>();
        foreach (var agent in targetAgents)
        {
            var targetedMessage = message with { ToAgentId = agent.AgentId };
            var result = await SendAsync(targetedMessage, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        var successCount = results.Count(r => r.Success);
        if (successCount == targetAgents.Count)
        {
            return MessageSendResult.SuccessResult(message.MessageId);
        }

            return MessageSendResult.FailureResult(
                message.MessageId,
                $"Failed to send to {targetAgents.Count - successCount} of {targetAgents.Count} agents");
    }

    /// <inheritdoc />
    public Task<IDisposable> SubscribeAsync(
        string agentId,
        Func<AgentMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(agentId);
        ArgumentNullException.ThrowIfNull(handler);

        var queueName = EnsureQueueForAgent(agentId);
        var consumer = new EventingBasicConsumer(_channel);
        var subscription = new RabbitMQSubscription(_channel, consumer, queueName);

        consumer.Received += async (model, ea) =>
        {
            try
            {
                var message = RabbitMQMessageSerializer.Deserialize(ea.Body.ToArray());
                if (message != null)
                {
                    await handler(message, CancellationToken.None).ConfigureAwait(false);
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                else
                {
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing message for agent {AgentId}", agentId);
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        var consumerTag = _channel.BasicConsume(queueName, autoAck: false, consumer);
        subscription.SetConsumerTag(consumerTag);

        if (!_subscriptions.TryGetValue(agentId, out var subscriptions))
        {
            subscriptions = new List<IDisposable>();
            _subscriptions[agentId] = subscriptions;
        }
        subscriptions.Add(subscription);

        return Task.FromResult<IDisposable>(subscription);
    }

    /// <inheritdoc />
    public Task<IDisposable> SubscribeByTypeAsync(
        string messageType,
        Func<AgentMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(messageType);
        ArgumentNullException.ThrowIfNull(handler);

        var queueName = $"{_options.QueuePrefix}.type.{messageType}";
        _channel.QueueDeclare(queueName, durable: _options.DurableQueues, exclusive: false, autoDelete: _options.AutoDeleteQueues);
        _channel.QueueBind(queueName, _options.ExchangeName, $"*.{messageType}");

        var consumer = new EventingBasicConsumer(_channel);
        var subscription = new RabbitMQSubscription(_channel, consumer, queueName);

        consumer.Received += async (model, ea) =>
        {
            try
            {
                var message = RabbitMQMessageSerializer.Deserialize(ea.Body.ToArray());
                if (message != null && message.MessageType == messageType)
                {
                    await handler(message, CancellationToken.None).ConfigureAwait(false);
                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                else
                {
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing message of type {MessageType}", messageType);
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            }
        };

        var consumerTag = _channel.BasicConsume(queueName, autoAck: false, consumer);
        subscription.SetConsumerTag(consumerTag);

        return Task.FromResult<IDisposable>(subscription);
    }


    private string EnsureQueueForAgent(string agentId)
    {
        return _queueNames.GetOrAdd(agentId, id =>
        {
            var queueName = $"{_options.QueuePrefix}.{id}";
            _channel.QueueDeclare(queueName, durable: _options.DurableQueues, exclusive: false, autoDelete: _options.AutoDeleteQueues);
            _channel.QueueBind(queueName, _options.ExchangeName, $"agent.{id}");
            return queueName;
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _channel?.Close();
        _channel?.Dispose();
        _connection?.Close();
        _connection?.Dispose();

        foreach (var subscriptions in _subscriptions.Values)
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a RabbitMQ subscription that can be disposed.
/// </summary>
internal class RabbitMQSubscription : IDisposable
{
    private readonly IModel _channel;
    private readonly EventingBasicConsumer _consumer;
    private readonly string _queueName;
    private string? _consumerTag;
    private bool _disposed;

    public RabbitMQSubscription(IModel channel, EventingBasicConsumer consumer, string queueName)
    {
        _channel = channel;
        _consumer = consumer;
        _queueName = queueName;
    }

    public void SetConsumerTag(string consumerTag)
    {
        _consumerTag = consumerTag;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (!string.IsNullOrEmpty(_consumerTag))
            {
                _channel.BasicCancel(_consumerTag);
            }
        }
        catch
        {
            // Ignore errors during cleanup
        }

        _disposed = true;
    }
}
