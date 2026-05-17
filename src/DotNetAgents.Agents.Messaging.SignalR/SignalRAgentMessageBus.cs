using DotNetAgents.Agents.Messaging;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace DotNetAgents.Agents.Messaging.SignalR;

/// <summary>
/// SignalR-based implementation of <see cref="IAgentMessageBus"/> for distributed agent communication.
/// </summary>
public class SignalRAgentMessageBus : IAgentMessageBus, IAsyncDisposable
{
    private readonly SignalRAgentMessageBusOptions _options;
    private readonly ILogger<SignalRAgentMessageBus>? _logger;
    private HubConnection? _connection;
    private readonly ConcurrentDictionary<string, Func<AgentMessage, CancellationToken, Task>> _agentSubscriptions = new();
    private readonly ConcurrentDictionary<string, Func<AgentMessage, CancellationToken, Task>> _typeSubscriptions = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRAgentMessageBus"/> class.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="logger">Optional logger instance.</param>
    public SignalRAgentMessageBus(
        IOptions<SignalRAgentMessageBusOptions> options,
        ILogger<SignalRAgentMessageBus>? logger = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRAgentMessageBus"/> class with direct options.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="logger">Optional logger instance.</param>
    public SignalRAgentMessageBus(
        SignalRAgentMessageBusOptions options,
        ILogger<SignalRAgentMessageBus>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
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

        if (message.TimeToLive.HasValue &&
            DateTimeOffset.UtcNow - message.Timestamp > message.TimeToLive.Value)
        {
            _logger?.LogWarning(
                "Message {MessageId} has expired and will not be delivered",
                message.MessageId);
            return MessageSendResult.FailureResult(
                message.MessageId,
                "Message has expired.");
        }

        try
        {
            var connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            var messageJson = JsonSerializer.Serialize(message);
            await connection.InvokeAsync("SendMessage", messageJson, cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug(
                "Sent message {MessageId} from {FromAgentId} to {ToAgentId}",
                message.MessageId,
                message.FromAgentId,
                message.ToAgentId);

            return MessageSendResult.SuccessResult(message.MessageId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error sending message {MessageId}", message.MessageId);
            return MessageSendResult.FailureResult(
                message.MessageId,
                $"Failed to send message: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<MessageSendResult> BroadcastAsync(
        AgentMessage message,
        Func<DotNetAgents.Agents.Registry.AgentInfo, bool>? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        if (message.TimeToLive.HasValue &&
            DateTimeOffset.UtcNow - message.Timestamp > message.TimeToLive.Value)
        {
            _logger?.LogWarning(
                "Broadcast message {MessageId} has expired and will not be delivered",
                message.MessageId);
            return MessageSendResult.FailureResult(
                message.MessageId,
                "Message has expired.");
        }

        try
        {
            var connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

            var messageJson = JsonSerializer.Serialize(message);
            var filterJson = filter != null ? JsonSerializer.Serialize(filter) : null;
            await connection.InvokeAsync("BroadcastMessage", messageJson, filterJson, cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug(
                "Broadcast message {MessageId} from {FromAgentId}",
                message.MessageId,
                message.FromAgentId);

            return MessageSendResult.SuccessResult(message.MessageId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error broadcasting message {MessageId}", message.MessageId);
            return MessageSendResult.FailureResult(
                message.MessageId,
                $"Failed to broadcast message: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<IDisposable> SubscribeAsync(
        string agentId,
        Func<AgentMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(handler);

        var connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        // Register handler
        var subscriptionKey = $"agent:{agentId}";
        _agentSubscriptions[subscriptionKey] = handler;

        // Subscribe on server
        await connection.InvokeAsync("SubscribeToAgent", agentId, cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("Subscribed to messages for agent {AgentId}", agentId);

        // Set up message handler
        connection.On<string>("ReceiveMessage", async (messageJson) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<AgentMessage>(messageJson);
                if (message != null && message.ToAgentId == agentId)
                {
                    await handler(message, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling message for agent {AgentId}", agentId);
            }
        });

        return new SignalRSubscription(async () =>
        {
            _agentSubscriptions.TryRemove(subscriptionKey, out _);
            try
            {
                await connection.InvokeAsync("UnsubscribeFromAgent", agentId, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Ignore errors during unsubscribe
            }
        });
    }

    /// <inheritdoc />
    public async Task<IDisposable> SubscribeByTypeAsync(
        string messageType,
        Func<AgentMessage, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentNullException.ThrowIfNull(handler);

        var connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

        // Register handler
        var subscriptionKey = $"type:{messageType}";
        _typeSubscriptions[subscriptionKey] = handler;

        // Subscribe on server
        await connection.InvokeAsync("SubscribeToMessageType", messageType, cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("Subscribed to messages of type {MessageType}", messageType);

        // Set up message handler
        connection.On<string>("ReceiveMessageByType", async (messageJson) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<AgentMessage>(messageJson);
                if (message != null && message.MessageType == messageType)
                {
                    await handler(message, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling message of type {MessageType}", messageType);
            }
        });

        return new SignalRSubscription(async () =>
        {
            _typeSubscriptions.TryRemove(subscriptionKey, out _);
            try
            {
                await connection.InvokeAsync("UnsubscribeFromMessageType", messageType, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Ignore errors during unsubscribe
            }
        });
    }

    private async Task<HubConnection> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection != null && _connection.State == HubConnectionState.Connected)
        {
            return _connection;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection != null && _connection.State == HubConnectionState.Connected)
            {
                return _connection;
            }

            if (_connection != null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }

            var builder = new HubConnectionBuilder()
                .WithUrl(_options.HubUrl, options =>
                {
                    if (_options.AccessTokenProvider != null)
                    {
                        options.AccessTokenProvider = _options.AccessTokenProvider;
                    }
                    if (_options.SkipNegotiation)
                    {
                        options.SkipNegotiation = true;
                    }
                    if (_options.TransportType.HasValue)
                    {
                        options.Transports = _options.TransportType.Value;
                    }
                })
                .WithAutomaticReconnect(new[] { _options.ReconnectDelay });

            _connection = builder.Build();

            // Set up connection event handlers
            _connection.Closed += async (error) =>
            {
                _logger?.LogWarning(error, "SignalR connection closed");
                await Task.CompletedTask;
            };

            _connection.Reconnecting += async (error) =>
            {
                _logger?.LogWarning(error, "SignalR connection reconnecting");
                await Task.CompletedTask;
            };

            _connection.Reconnected += async (connectionId) =>
            {
                _logger?.LogInformation("SignalR connection reconnected. Connection ID: {ConnectionId}", connectionId);

                // Re-subscribe to all subscriptions
                foreach (var (key, handler) in _agentSubscriptions)
                {
                    if (key.StartsWith("agent:"))
                    {
                        var agentId = key.Substring(6);
                        await _connection.InvokeAsync("SubscribeToAgent", agentId, cancellationToken).ConfigureAwait(false);
                    }
                }

                foreach (var (key, handler) in _typeSubscriptions)
                {
                    if (key.StartsWith("type:"))
                    {
                        var messageType = key.Substring(5);
                        await _connection.InvokeAsync("SubscribeToMessageType", messageType, cancellationToken).ConfigureAwait(false);
                    }
                }

                await Task.CompletedTask;
            };

            await _connection.StartAsync(cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("SignalR connection established to {HubUrl}", _options.HubUrl);

            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (_connection != null)
        {
            try
            {
                await _connection.StopAsync().ConfigureAwait(false);
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error disposing SignalR connection");
            }
            finally
            {
                _connection = null;
            }
        }

        _connectionLock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class SignalRSubscription : IDisposable
    {
        private readonly Func<Task> _unsubscribe;
        private bool _disposed;

        public SignalRSubscription(Func<Task> unsubscribe)
        {
            _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _unsubscribe().GetAwaiter().GetResult();
                }
                catch
                {
                    // Ignore errors during unsubscribe
                }
                _disposed = true;
            }
        }
    }
}
