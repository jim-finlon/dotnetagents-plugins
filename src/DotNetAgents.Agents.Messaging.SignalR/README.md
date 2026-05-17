# DotNetAgents.Agents.Messaging.SignalR

SignalR-based implementation of `IAgentMessageBus` for distributed agent communication using .NET 10.

## Features

- Real-time agent-to-agent messaging via SignalR
- Automatic reconnection with configurable retry logic
- Support for agent-specific and message-type subscriptions
- Message expiration (TTL) support
- .NET 10 optimized with `IAsyncDisposable`

## Client-Side Usage

```csharp
using DotNetAgents.Agents.Messaging.SignalR;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSignalRAgentMessageBus(options =>
{
    options.HubUrl = "https://your-server.com/hubs/agentmessages";
    options.ReconnectDelay = TimeSpan.FromSeconds(5);
    options.MaxReconnectAttempts = 5;
});

var serviceProvider = services.BuildServiceProvider();
var messageBus = serviceProvider.GetRequiredService<IAgentMessageBus>();

// Send a message
var message = new AgentMessage
{
    FromAgentId = "agent-1",
    ToAgentId = "agent-2",
    MessageType = "task_request",
    Payload = new { TaskId = "task-123" }
};

var result = await messageBus.SendAsync(message);

// Subscribe to messages
var subscription = await messageBus.SubscribeAsync(
    "agent-2",
    async (msg, ct) =>
    {
        Console.WriteLine($"Received: {msg.MessageId}");
    });
```

## Server-Side Setup

To use this message bus, you need to set up a SignalR hub on your server. Create a hub class in your ASP.NET Core application:

```csharp
using Microsoft.AspNetCore.SignalR;
using DotNetAgents.Agents.Messaging;
using System.Text.Json;

public class AgentMessageHub : Hub
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> _agentSubscriptions = new();

    public async Task SendMessage(string messageJson)
    {
        var message = JsonSerializer.Deserialize<AgentMessage>(messageJson);
        if (message == null) return;

        if (_agentSubscriptions.TryGetValue(message.ToAgentId, out var connectionIds))
        {
            await Clients.Clients(connectionIds.ToList())
                .SendAsync("ReceiveMessage", messageJson);
        }
    }

    public async Task BroadcastMessage(string messageJson, string? filterJson = null)
    {
        await Clients.All.SendAsync("ReceiveMessage", messageJson);
    }

    public Task SubscribeToAgent(string agentId)
    {
        _agentSubscriptions.AddOrUpdate(
            agentId,
            new HashSet<string> { Context.ConnectionId },
            (key, existing) =>
            {
                existing.Add(Context.ConnectionId);
                return existing;
            });
        return Task.CompletedTask;
    }

    public Task UnsubscribeFromAgent(string agentId)
    {
        if (_agentSubscriptions.TryGetValue(agentId, out var connectionIds))
        {
            connectionIds.Remove(Context.ConnectionId);
        }
        return Task.CompletedTask;
    }

    public Task SubscribeToMessageType(string messageType)
    {
        // Similar implementation for type-based subscriptions
        return Task.CompletedTask;
    }
}
```

Then in your `Program.cs`:

```csharp
builder.Services.AddSignalR();
// ... other services

var app = builder.Build();
app.MapHub<AgentMessageHub>("/hubs/agentmessages");
```

## Configuration Options

- `HubUrl`: The SignalR hub URL (default: "/hubs/agentmessages")
- `AccessTokenProvider`: Optional function to provide authentication tokens
- `ReconnectDelay`: Delay between reconnection attempts (default: 5 seconds)
- `MaxReconnectAttempts`: Maximum reconnection attempts (default: 5)
- `SkipNegotiation`: Skip SignalR negotiation (use WebSockets directly)
- `TransportType`: Preferred transport type (WebSockets, Server-Sent Events, Long Polling)

## Requirements

- .NET 10.0
- SignalR server running with compatible hub implementation
- Network connectivity between client and server
