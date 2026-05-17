using DotNetAgents.Agents.Messaging;
using DotNetAgents.Agents.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace DotNetAgents.Agents.Messaging.Redis;

/// <summary>
/// Redis Pub/Sub implementation of <see cref="IAgentMessageBus"/>.
/// Suitable for distributed deployments requiring high-performance pub/sub messaging.
/// </summary>
public class RedisPubSubAgentMessageBus : IAgentMessageBus, IDisposable
{
    private readonly RedisOptions _options;
    private readonly IAgentRegistry _agentRegistry;
    private readonly ILogger<RedisPubSubAgentMessageBus>? _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly ISubscriber _subscriber;
    private readonly ConcurrentDictionary<string, List<IDisposable>> _subscriptions = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisPubSubAgentMessageBus"/> class.
    /// </summary>
    /// <param name="options">Redis configuration options.</param>
    /// <param name="agentRegistry">The agent registry for finding agents.</param>
    /// <param name="logger">Optional logger instance.</param>
    public RedisPubSubAgentMessageBus(
        IOptions<RedisOptions> options,
        IAgentRegistry agentRegistry,
        ILogger<RedisPubSubAgentMessageBus>? logger = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
        _logger = logger;

        _redis = ConnectionMultiplexer.Connect(_options.ConnectionString);
        _subscriber = _redis.GetSubscriber();

        _logger?.LogInformation("Redis Pub/Sub message bus initialized. Connection: {ConnectionString}", _options.ConnectionString);
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
            var channel = message.ToAgentId == "*"
                ? RedisChannel.Literal(_options.BroadcastChannel)
                : RedisChannel.Literal($"{_options.ChannelPrefix}{message.ToAgentId}");

            var serialized = RedisMessageSerializer.Serialize(message);
            var subscriberCount = await _subscriber.PublishAsync(channel, serialized).ConfigureAwait(false);

            _logger?.LogDebug(
                "Sent message {MessageId} to {ToAgentId} via Redis. Subscribers: {SubscriberCount}",
                message.MessageId,
                message.ToAgentId,
                subscriberCount);

            return MessageSendResult.SuccessResult(message.MessageId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send message {MessageId} via Redis", message.MessageId);
            return MessageSendResult.FailureResult(message.MessageId, ex.Message);
        }
    }

    /// <summary>
    /// Wrapper for Redis subscription that implements IDisposable.
    /// </summary>
    private class RedisSubscription : IDisposable
    {
        private readonly ISubscriber _subscriber;
        private readonly RedisChannel _channel;
        private bool _disposed;

        public RedisSubscription(ISubscriber subscriber, RedisChannel channel)
        {
            _subscriber = subscriber;
            _channel = channel;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                _subscriber.Unsubscribe(_channel);
            }
            catch
            {
                // Ignore errors during cleanup
            }

            _disposed = true;
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

        var channel = RedisChannel.Literal($"{_options.ChannelPrefix}{agentId}");
        _subscriber.Subscribe(channel, (ch, value) =>
        {
            try
            {
                var message = RedisMessageSerializer.Deserialize(value!);
                if (message != null)
                {
                    handler(message, CancellationToken.None).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing message for agent {AgentId}", agentId);
            }
        });

        var subscription = new RedisSubscription(_subscriber, channel);
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

        var channel = RedisChannel.Literal($"{_options.MessageTypeChannelPrefix}{messageType}");
        _subscriber.Subscribe(channel, (ch, value) =>
        {
            try
            {
                var message = RedisMessageSerializer.Deserialize(value!);
                if (message != null && message.MessageType == messageType)
                {
                    handler(message, CancellationToken.None).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing message of type {MessageType}", messageType);
            }
        });

        var subscription = new RedisSubscription(_subscriber, channel);
        return Task.FromResult<IDisposable>(subscription);
    }


    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var subscriptions in _subscriptions.Values)
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }

        _subscriber?.UnsubscribeAll();
        _redis?.Close();
        _redis?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
