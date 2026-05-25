# Feature: Messaging

Messaging plugins connect agents to event streams, queues, and real-time
notifications.

## Use Cases

- enqueue background work
- publish agent status updates
- notify UI clients
- distribute tasks to workers
- consume domain events

## Public Plugin Families

- Kafka
- RabbitMQ
- Redis
- SignalR

## Technical Pattern

Use typed messages and explicit envelopes:

```csharp
public sealed record AgentWorkQueued(
    string WorkId,
    string WorkKind,
    DateTimeOffset QueuedAtUtc);
```

Do not pass raw prompt text or secrets through a broad event bus unless the bus
is designed for that data class.

## Implementation Checklist

- choose delivery semantics intentionally
- include message ids
- make handlers idempotent where possible
- set retry/dead-letter policy
- redact payloads before publishing
- test duplicate and malformed messages
